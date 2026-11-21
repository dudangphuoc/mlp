export type Effect = "read" | "write" | "delete" | "compute";
export type Audience = "user" | "internal" | "agent";

export interface Operation {
  id: string;
  path: string;
  verb: string;
  grpc?: { service: string; method: string };
  service: string;
  module: string;
  kind: "command" | "query" | "rest";
  effect: Effect;
  audience: Audience;
  access: string;
  permission?: string;
  roles: string[];
  streaming: boolean;
  scopeField?: string;
  refs: Record<string, { entity: string; list?: string | null }>;
  isPublic: boolean;
  input?: string;
  output?: string;
}

export interface ApiDocument {
  protoHash: string;
  operations: Operation[];
}

type Json = Record<string, any>;

let cached: Promise<ApiDocument> | null = null;

export function loadApiDocument(): Promise<ApiDocument> {
  cached ??= fetch("/openapi/v1.json", { credentials: "include" })
    .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
    .then(parse)
    .catch((e) => { cached = null; throw e; });
  return cached;
}

function schemaName(body: Json | undefined): string | undefined {
  const ref: string | undefined = body?.content?.["application/json"]?.schema?.$ref;
  return ref?.split("/").pop();
}

function parse(doc: Json): ApiDocument {
  const operations: Operation[] = [];
  for (const [path, item] of Object.entries<Json>(doc.paths ?? {})) {
    for (const [verb, op] of Object.entries<Json>(item)) {
      const grpc = op["x-grpc"];
      operations.push({
        id: op.operationId,
        path,
        verb: verb.toUpperCase(),
        grpc: grpc ? { service: grpc.service, method: grpc.method } : undefined,
        service: grpc ? String(grpc.service).split(".").pop()! : String(op.tags?.[0] ?? "rest").replace("rest:", ""),
        module: op["x-module"] ?? "",
        kind: grpc ? op["x-kind"] : "rest",
        effect: grpc ? op["x-effect"] : verb === "get" ? "read" : "write",
        audience: op["x-audience"] ?? "user",
        access: op["x-access"] ?? "",
        permission: op["x-permission"],
        roles: op["x-roles"] ?? [],
        streaming: !!op["x-streaming"],
        scopeField: op["x-scope-field"],
        refs: op["x-ref"] ?? {},
        isPublic: !!op["x-public"],
        input: schemaName(op.requestBody),
        output: schemaName(op.responses?.["200"]),
      });
    }
  }
  operations.sort((a, b) => a.service.localeCompare(b.service) || a.id.localeCompare(b.id));
  return { protoHash: doc.info?.["x-proto-hash"] ?? "", operations };
}

export const ROLES = ["owner", "admin", "editor", "reviewer", "annotator", "viewer"] as const;

export function allows(op: Operation, role: string): boolean | "entity" {
  if (op.access === "WorkspacePermission") return op.roles.includes(role);
  if (op.access === "ScopedPermission") return "entity";
  return true;
}
