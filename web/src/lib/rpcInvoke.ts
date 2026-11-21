import { create, fromJson, toJson, type DescMessage, type DescMethod, type DescService, type JsonValue } from "@bufbuild/protobuf";
import { createClient } from "@connectrpc/connect";
import { transport } from "./api";

const modules = import.meta.glob("../gen/mlplatform/v1/*_pb.ts", { eager: true }) as Record<string, Record<string, unknown>>;

const services: DescService[] = Object.values(modules)
  .flatMap((m) => Object.values(m))
  .filter((x): x is DescService => typeof x === "object" && x !== null && (x as { kind?: string }).kind === "service");

export function findMethod(service: string, method: string): DescMethod | undefined {
  return services.find((s) => s.typeName === service)?.methods.find((m) => m.name === method);
}

export function emptyJson(schema: DescMessage): Record<string, JsonValue> {
  return toJson(schema, create(schema), { alwaysEmitImplicit: true }) as Record<string, JsonValue>;
}

export interface CallResult {
  ok: boolean;
  status: string;
  ms: number;
  json?: JsonValue;
  events?: JsonValue[];
  error?: string;
}

export async function invokeUnary(method: DescMethod, body: JsonValue, signal?: AbortSignal): Promise<CallResult> {
  const started = performance.now();
  try {
    const message = fromJson(method.input, body);
    const client = createClient(method.parent, transport) as unknown as Record<string, (req: unknown, opt: { signal?: AbortSignal }) => Promise<unknown>>;
    const res = await client[method.localName](message, { signal });
    return { ok: true, status: "OK", ms: performance.now() - started, json: toJson(method.output, res as never) };
  } catch (e) {
    return failure(e, started);
  }
}

export async function invokeStream(method: DescMethod, body: JsonValue, signal: AbortSignal, onEvent: (event: JsonValue) => void): Promise<CallResult> {
  const started = performance.now();
  try {
    const message = fromJson(method.input, body);
    const client = createClient(method.parent, transport) as unknown as Record<string, (req: unknown, opt: { signal?: AbortSignal }) => AsyncIterable<unknown>>;
    for await (const ev of client[method.localName](message, { signal })) onEvent(toJson(method.output, ev as never));
    return { ok: true, status: "OK", ms: performance.now() - started };
  } catch (e) {
    return signal.aborted ? { ok: true, status: "STOPPED", ms: performance.now() - started } : failure(e, started);
  }
}

function failure(e: unknown, started: number): CallResult {
  const err = e as { code?: number; rawMessage?: string; message?: string };
  const codes = ["OK", "CANCELLED", "UNKNOWN", "INVALID_ARGUMENT", "DEADLINE_EXCEEDED", "NOT_FOUND", "ALREADY_EXISTS", "PERMISSION_DENIED",
    "RESOURCE_EXHAUSTED", "FAILED_PRECONDITION", "ABORTED", "OUT_OF_RANGE", "UNIMPLEMENTED", "INTERNAL", "UNAVAILABLE", "DATA_LOSS", "UNAUTHENTICATED"];
  return { ok: false, status: err.code !== undefined ? codes[err.code] ?? String(err.code) : "ERROR", ms: performance.now() - started, error: err.rawMessage ?? err.message };
}

export async function invokeRest(verb: string, path: string, body: string | undefined, headers: Record<string, string>): Promise<CallResult> {
  const started = performance.now();
  try {
    const res = await fetch(path, { method: verb, body: verb === "GET" ? undefined : body, headers, credentials: "include" });
    const text = await res.text();
    let json: JsonValue | undefined;
    try { json = JSON.parse(text) as JsonValue; } catch { json = text; }
    return { ok: res.ok, status: String(res.status), ms: performance.now() - started, json };
  } catch (e) {
    return { ok: false, status: "ERROR", ms: performance.now() - started, error: (e as Error).message };
  }
}
