---
name: csharp-15
description: C# 15 / .NET 11 coding guide for MLPlatform — union types, closed hierarchies, collection expression arguments, extension indexers, labeled break/continue, plus .NET 11 runtime/library performance APIs and breaking changes. Use whenever writing, refactoring, reviewing or upgrading C# in this repo (src/, tools/), or when deciding how to model a value that is "one of several shapes".
---

# C# 15 on .NET 11 — MLPlatform guide

Sources read 2026-11-21 on learn.microsoft.com: What's new in C# 15 (updated 2026-11-19), the language reference pages for `union`, `closed`, `extension`, collection expressions, jump statements, interfaces, unsafe code, patterns, configure-language-version, IDE0410, the compiler breaking changes for .NET 11, and What's new in .NET 11 (runtime, libraries, SDK) with its compatibility index and the ASP.NET Core 11 release notes. .NET 11 GA: SDK 11.0.100, released 2026-11-10.

Repo rules still apply on top of this guide: Native AOT with IL warnings as errors, no comments in C#, English identifiers, one type or feature slice per file (docs/refactor-plan.md).

## 1. Project setup

- `Directory.Build.props`: `<TargetFramework>net11.0</TargetFramework>` and `<LangVersion>15.0</LangVersion>`. Pin `15.0` rather than `latest`: Microsoft says `latest` varies by machine and can enable features the SDK's runtime does not support. `15.0` is the value listed in the language-version table; C# 15 is also the default for net11.0.
- `global.json`: `"version": "11.0.100"`, `"rollForward": "latestPatch"`, `"allowPrerelease": false`.
- Never set `<LangVersion>preview</LangVersion>`. The only C# 15 preview feature is the memory-safety work (`unsafe(expr)`, `safe`, relaxed pointer rules). This repo has no unsafe code and keeps it that way.
- To check the effective version in a file, write `#error version` temporarily (CS8304 prints it), then remove it.
- Package references: the nine `Microsoft.Extensions.*.Abstractions`/`Options`/`Primitives` packages are in the net11.0 shared framework. Remove explicit references, otherwise you get NU1510. `Microsoft.Extensions.Hosting` (full) is not in that list and stays.
- Lock files (`RestorePackagesWithLockFile`) must be regenerated after the TFM change: `dotnet restore --force-evaluate`.

## 2. Union types — model "one of N" without class hierarchies or tag strings

```csharp
public union JobOutcome(Succeeded, Failed, Cancelled);

public sealed record Succeeded(Guid RunId);
public sealed record Failed(string Reason);
public sealed record Cancelled(Guid ByUser);

static string Describe(JobOutcome outcome) => outcome switch
{
    Succeeded s => $"run {s.RunId}",
    Failed f => f.Reason,
    Cancelled c => $"cancelled by {c.ByUser}",
    null => "pending",
};
```

Rules:
- Case types can be any type convertible to `object`: records, classes, structs, interfaces, nullable types, other unions.
- Each case type converts implicitly to the union (`JobOutcome o = new Failed("oom");`). If two case types are equally applicable to a value, the conversion is an error.
- Type and declaration patterns test the **contents**, not the union value. `outcome is JobOutcome` is CS8121.
- A switch that covers every case type is exhaustive. A `default` union has a null `Value`, so add a `null` arm. Without it you get CS8655, and at runtime a `SwitchExpressionException`.
- Prefer an explicit `null` arm over `_` or `var` arms. A discard silently swallows a case type added later.
- Union declarations cannot have instance fields, auto-properties, field-like events, or public single-parameter constructors. Any constructor you write must chain to a generated one with `this(...)`.
- `union` is a contextual keyword. A type or field named `union` must be written `@union` (CS9370).

**Performance:**
- The compiler-generated union is a struct that **boxes value-type cases** into `object? Value`. Use record classes as case types, which are already references and cost nothing extra.
- For hot paths with value-type cases, write a custom struct union: `[Union]` plus `IUnion`, a public constructor per case, `object? Value`, and the non-boxing access pattern (`bool HasValue`, one `bool TryGetValue(out T)` per case). The compiler then calls `TryGetValue` for type patterns and `HasValue` for `null` patterns, with no boxing. You must keep `Value`, `HasValue` and `TryGetValue` consistent.
- If a union needs a private constructor or factories, nest a public `IUnionMembers` interface with static `Create(...)` methods, one per case.

**JSON / AOT:**
- System.Text.Json in .NET 11 serializes a union as its active case.
- Add `[JsonUnion]` with a classifier when the cases cannot be told apart from the JSON alone.
- Register every case type in the `JsonSerializerContext`.
- Unions work in minimal-API JSON bodies, but not in route, query, header or form binding.

**Where to use it in this repo:**
- Handler results that are "row or reason": `TryGetRun` → `union RunLookup(RunRow, RunNotFound)`. This replaces `null` plus a separate error lookup.
- Pipeline step definitions (`PipelineDef.Step`) and AI replies (`ChatReply` / `DecisionReply`).
- Command results that today return tuples such as `(trial, job?)`.

Do not use a union for state strings that are persisted as text (`job.state`). Keep those as strings or enums at the SQL boundary and map them to a union in the domain only if behaviour differs per state.

## 3. Closed hierarchies — exhaustive switches over a base class

```csharp
public closed record PlacementDecision;
public sealed record Placed(IReadOnlyList<Guid> Hosts) : PlacementDecision;
public sealed record Waiting(string Reason) : PlacementDecision;
public sealed record Rejected(string Code) : PlacementDecision;

var next = decision switch
{
    Placed p => Launch(p.Hosts),
    Waiting w => Wait(w.Reason),
    Rejected r => Fail(r.Code),
};
```

- `closed` applies to classes and records only. It makes the type implicitly abstract, and it cannot be combined with `sealed`, `static` or `abstract`.
- Direct subtypes must live in the same assembly and module. A plain (non-closed) direct descendant is still open to other assemblies.
- Exhaustiveness counts only **direct** descendants that are visible at the switch. Mark an intermediate type `closed` to make its subtree exhaustive too.
- An `internal` descendant switched over from another assembly gives CS8509. A subtype arm placed after its parent's arm gives CS8510 (unreachable).
- A nullable governing type still needs a `null` arm.
- A generic derived class must use every one of its type parameters in its base type specification.
- A type named `closed` must be written `@closed` (CS9380).
- System.Text.Json: `[JsonPolymorphic(InferClosedTypePolymorphism = true)]` derives the polymorphic metadata from the closed set.

**Union vs closed:** use `closed` when the cases share members or data (a base record with common properties), or when they are already a class family. Use `union` when the case types are unrelated or already exist elsewhere.

**Where to use it:**
- Every existing `abstract record`/`abstract class` whose subtypes all live in the same project becomes `closed`. Then drop the `_ => throw new UnreachableException()` arms.

## 4. Collection expression arguments — `with(...)`

```csharp
List<HostRow> fit = [with(capacity: spec.Nnodes), ..candidates];
HashSet<string> names = [with(StringComparer.OrdinalIgnoreCase), ..requested];
Dictionary<Guid, RunView> byId = [with(runs.Count)];
```

- `with(...)` must be the first element. It is not allowed for arrays, `Span<T>` or `ReadOnlySpan<T>`, and its arguments cannot be `dynamic`.
- The arguments do not take part in overload resolution or type inference (you get CS0121 or CS0411). The target type must already be known.
- Interface targets: `IList<T>` and `ICollection<T>` accept `with()` or `with(capacity)`. The read-only interfaces accept only an empty `with()`.
- For `[CollectionBuilder]` types, the with-arguments go before the `ReadOnlySpan<T>` parameter of `Create`.
- Evaluation order: the with-arguments run first, then the elements, left to right.
- A call to a method named `with` inside a collection expression must be written `@with(...)`.

**Performance:** always pass a capacity when the size is known (`[with(capacity: n)]`), and pass the comparer at construction instead of building and then copying. This replaces `new List<T>(n)` followed by `AddRange`, and `new HashSet<string>(xs, comparer)`.

## 5. Extension indexers

```csharp
public static class JsonNodeExtensions
{
    extension(JsonObject node)
    {
        public string? this[string key, string fallback] => node[key]?.GetValue<string>() ?? fallback;
    }
}
```

- Extension indexers are declared inside an `extension(receiver)` block, and the receiver must be named. `get`, `set`, expression-bodied and `ref` returns are allowed.
- Extension `Length`/`Count` plus an `int` indexer plus `Slice(int, int)` light up `^1`, ranges and list patterns on the receiver.
- Lookup order: a real indexer on the receiver, then implicit Index/Range on real members, then the extension indexer.
- Limits: never used for arrays or `string`; not allowed in expression trees; no spread optimisations.
- Use them for typed views over `JsonObject`, `IConfiguration` sections and proto maps. Do not use them to hide expensive work behind `[]`.

## 6. Labeled `break` / `continue`

```csharp
hosts:
foreach (var host in online)
{
    foreach (var gpu in host.Gpus)
    {
        if (gpu.Busy) continue hosts;
        if (gpu.Fits(spec)) { chosen = (host, gpu); break hosts; }
    }
}
```

- The label must sit directly on the loop or switch it names.
- `break` can target a loop or a switch; `continue` can target a loop only.
- IDE0410 (`csharp_style_prefer_labeled_jump_statements`, on by default) flags `goto` and Boolean-flag escapes from nested loops. Fix every hit.

## 7. Static interface members (non-virtual)

`public static` members with bodies on interfaces compile against older runtimes. Use them for small pure helpers that belong to a contract, for example `IClock.Elapsed(start, end)`. Do not use them as a dumping ground for logic that belongs in a domain type.

## 8. C# 14 features that are also on and under-used here (from the C# 14 release, not re-read)

- `field` keyword in property accessors: removes backing fields that exist only for validation.
- `extension` blocks with properties and static members: replace `static class XExtensions` grab bags with grouped receivers.
- Null-conditional assignment: `run?.Name = name;`.
- Implicit span conversions: pass arrays and strings straight to `ReadOnlySpan<T>` overloads.
- `nameof(List<>)`, lambda parameter modifiers without types, and partial constructors and events. Partial members help source generators.

## 9. .NET 11 runtime and library performance — what to use here

**Runtime (free after the upgrade):**
- JIT bounds-check elimination, generic-virtual and default-interface devirtualisation, and `SequenceEqual` constant folding.
- NativeAOT shared interface dispatch.
- ReadyToRun specialises `EqualityComparer<T>.Default` and `Comparer<T>.Default`.
- Async continuations skip `ExecutionContext` capture when no `AsyncLocal` is set.

**Runtime async:**
- Enabled with `<Features>runtime-async=on</Features>`; opt out per project with `<UseRuntimeAsync>false</UseRuntimeAsync>`. The runtime libraries are already built with it.
- Supported by NativeAOT and ReadyToRun.
- Gives lower async overhead and real async stack traces. It matters here because the loops (`SchedulerLoop`, `RecoveryLoop`, …) and every handler are async.
- Turn it on in `Directory.Build.props` only after an AOT publish plus the scenario suite pass with it on.

**Libraries:**

| Need | Use |
|---|---|
| Comparers keyed on a property | `EqualityComparer<T>.Create(x => x.Id)` |
| Outer joins in memory | `Enumerable.LeftJoin` / `RightJoin` / `FullJoin` |
| Base64 without allocating strings | `Base64.EncodeToUtf8` / `DecodeFromUtf8` (span) |
| Stream over bytes/strings without a copy | `ReadOnlyMemoryStream`, `ReadOnlySequenceStream`, `StringStream` |
| Pooled JSON writers | `Utf8JsonWriter.Reset(options)` |
| Stream large JSON lists | `JsonSerializer.SerializeAsyncEnumerable(..., topLevelValues: true)` (JSON Lines) |
| Compress request bodies | `ZstandardCompressedContent`, `BrotliCompressedContent` |
| Span-based compression | `ZLibEncoder`, `GZipEncoder`, `ZstandardEncoder` (`Reset()` to reuse) |
| HTTP/2 tuning | `SocketsHttpHandler.InitialHttp2MaxConcurrentStreams`, `ShouldEvictConnection` |
| Constant-time compare | `CryptographicOperations.FixedTimeEquals` (already used) |
| Cache metrics | `MemoryCacheOptions.TrackStatistics = true` |

**Rules that stay valid:**
- Measure before and after: build a BenchmarkDotNet project under `tools/` that runs with `nice` on one core, or time the scenarios.
- Never trade AOT safety for speed. No `MakeGenericType`, no reflection-based JSON.
- Prefer `FrozenDictionary`/`FrozenSet` for lookup tables built once (for example the `Permissions` matrix and the settings catalog).
- Prefer `SearchValues<T>` for repeated `IndexOfAny`.
- Prefer `stackalloc`/`ArrayPool` over temporary arrays in tight loops, `ValueTask` on hot async paths, `CompositeFormat` for repeated formats, `[LoggerMessage]` source-generated logging, and `params ReadOnlySpan<T>`.

## 10. Breaking changes to check when upgrading this repo

**Compiler:**
- `with(...)` inside `[...]` now binds as collection arguments; write `@with(...)` for a method named `with`.
- A `Span<T> x = [...]` collection expression declared inside a loop now has declaration-block lifetime. Assigning it outward fails; use an array or move the declaration.
- `union` and `closed` are contextual keywords: rename types or fields with those names, or escape them with `@`.
- `(X.Y)` inside switch arms now parses as a constant pattern.
- `nameof(this.X)` in attributes is an error.

**Hosting:** `IHost.RunAsync` now rethrows when a `BackgroundService` fails (non-zero exit). Every loop here catches and logs inside its own loop; keep it that way and make sure no exception escapes `ExecuteAsync`.

**DI:** disposal now attempts every service, then throws an `AggregateException`. `Dispose` must not throw.

**OpenTelemetry:**
- ASP.NET Core now emits `http.route` and server-span tags itself (`SuppressActivityOpenTelemetryData` defaults to `false`). Update `OpenTelemetry.Instrumentation.AspNetCore`, or expect duplicate tags.
- HttpClient connection metrics are now observable. Pull-based exporters need no change.

**Kestrel:**
- HTTP/2 and HTTP/3 reject connection-specific headers (`Connection`, `Keep-Alive`, `TE` other than trailers, `Transfer-Encoding`, `Upgrade`). Check the nginx and any other proxy in front of gRPC.
- HTTP/1.1 closes the connection when a request carries both `Content-Length` and `Transfer-Encoding`.

**OpenAPI:** `Microsoft.AspNetCore.OpenApi` now emits OpenAPI 3.2 by default. Set `OpenApiVersion = OpenApi3_1` if a consumer needs 3.1. `Microsoft.AspNetCore.Grpc.Swagger` is deprecated; do not adopt it.

**Configuration:** an empty string from a later chained configuration source now overrides earlier values. Check `MLP__…` environment variables that are set to an empty string.

**Numbers:** float-to-small-integer conversions now saturate instead of wrapping, and `Math.Round`, decimal and BigInteger conversions are correctly rounded. Check metric bucketing and the EvalMath code.

**Hardware:** x64 now needs x86-64-v2. ReadyToRun images target x86-64-v3 on Linux. Check older GPU hosts that run the agent or shim.

**NativeAOT publish:** `UseOptimizedPublish` defaults to `false`; leave it as is. The CLI now starts an MSBuild server by default, so keep `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0` in the scripts (machine heat rule).

**Containers:** use `mcr.microsoft.com/dotnet/sdk:11.0` and `aspnet:11.0`, and `runtime-deps:11.0-*-chiseled` for AOT. Confirm the exact tags on MCR before changing the Dockerfiles.

## 11. Review checklist for any C# change in this repo

1. A "kind" string, nullable pair or tuple carries one-of-N data → `union`, or a `closed` record family.
2. An abstract base type whose subtypes are all in this assembly → `closed`. Remove impossible `default` arms.
3. A collection whose size is known up front → `[with(capacity: n), ...]`. A comparer → `with(comparer)`.
4. A nested loop that exits through a flag or `goto` → labeled `break`/`continue`.
5. Lookups on `JsonObject`, config or maps repeated across files → extension indexer or extension property.
6. A backing field used only by one property → `field`.
7. Hot path: no LINQ inside per-item loops of the scheduler or gateway, no boxing of value-type union cases, spans over arrays, pooled writers.
8. Still AOT-clean (`dotnet publish` with IL warnings as errors at milestones), no comments, English names.
