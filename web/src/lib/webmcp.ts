/**
 * WebMCP: when the browser exposes `document.modelContext` (WebMCP draft; early Chromium previews used `navigator.modelContext`), the app
 * registers the platform's MCP tools (same list and behavior as POST /mcp, called with the signed-in session) plus page tools, so an
 * in-browser AI agent can operate the platform.
 */
type ToolDef = { name: string; title?: string; description: string; inputSchema: Record<string, unknown>; annotations?: Record<string, unknown> };
type WebTool = ToolDef & { execute: (args: Record<string, unknown>) => Promise<unknown> };
type ModelContext = {
  registerTool?: (t: WebTool, opts?: { signal?: AbortSignal }) => unknown;
  provideContext?: (c: { tools: WebTool[] }) => unknown;
};
type McpResult = { content?: { type: string; text?: string }[]; isError?: boolean };

/** The browser's model context, if any (spec: document.modelContext; older previews: navigator.modelContext). */
export function modelContext(): ModelContext | undefined {
  if (typeof document === "undefined") return undefined;
  return (document as unknown as { modelContext?: ModelContext }).modelContext
    ?? (navigator as unknown as { modelContext?: ModelContext }).modelContext;
}

async function rpc(method: string, params: unknown): Promise<{ result?: unknown; error?: { message: string } }> {
  const r = await fetch("/mcp", { method: "POST", credentials: "include", headers: { "content-type": "application/json" },
    body: JSON.stringify({ jsonrpc: "2.0", id: Date.now(), method, params }) });
  if (!r.ok) return { error: { message: `HTTP ${r.status}` } };
  return r.json();
}

/** WebMCP serializes whatever execute() resolves to as JSON: hand the agent the tool's JSON (or text), errors as { error }. */
function unwrap(r: McpResult): unknown {
  const text = (r.content ?? []).map((c) => c.text ?? "").join("\n");
  let value: unknown = text;
  try { value = JSON.parse(text); } catch { /* plain text */ }
  return r.isError ? { error: value } : value;
}

let active: AbortController | null = null;

/** Registers the tools; call again to re-register (e.g. after sign-in). Returns a cleanup. navigate(path) is the app router's go(). */
export async function registerWebMcp(navigate: (path: string) => void): Promise<() => void> {
  const mc = modelContext();
  if (!mc) return () => {};
  active?.abort();
  const ctl = new AbortController();
  active = ctl;
  const list = await rpc("tools/list", {});
  if (ctl.signal.aborted) return () => {};
  const server: WebTool[] = ((list.result as { tools?: ToolDef[] })?.tools ?? []).map((t) => ({
    ...t,
    annotations: { readOnlyHint: !!t.annotations?.readOnlyHint, consequentialHint: !t.annotations?.readOnlyHint },
    execute: async (args: Record<string, unknown>) => {
      const r = await rpc("tools/call", { name: t.name, arguments: args ?? {} });
      return r.error ? { error: r.error.message } : unwrap(r.result as McpResult);
    },
  }));
  const page: WebTool[] = [
    {
      name: "open_page",
      description: "Show a page of the platform to the user: overview, datasets, labeling, projects, jobs, models, optimize, deployments, fleet, settings — or a path such as labeling/{project_id}/annotate.",
      inputSchema: { type: "object", properties: { path: { type: "string" } }, required: ["path"] },
      annotations: { readOnlyHint: true },
      execute: async (a) => { navigate(String(a.path ?? "").replace(/^\/+/, "")); return { ok: true }; },
    },
    {
      name: "open_label_item",
      description: "Open one item of a labeling project in the labeling workspace.",
      inputSchema: { type: "object", properties: { project_id: { type: "string" }, path: { type: "string" } }, required: ["project_id", "path"] },
      annotations: { readOnlyHint: true },
      execute: async (a) => {
        if (typeof a.project_id !== "string" || typeof a.path !== "string" || !a.project_id || !a.path) return { error: "project_id and path are required" };
        navigate(`labeling/${a.project_id}/annotate?path=${encodeURIComponent(a.path)}`);
        return { ok: true };
      },
    },
  ];
  const tools = [...server, ...page];
  if (mc.registerTool) {
    for (const t of tools) {
      try { await mc.registerTool(t, { signal: ctl.signal }); } catch (e) { console.warn("WebMCP registerTool", t.name, e); }
    }
  } else if (mc.provideContext) mc.provideContext({ tools });
  return () => ctl.abort();
}

/** The MCP tool list as the server describes it (for the connection page). */
export async function listMcpTools(): Promise<ToolDef[]> {
  const r = await rpc("tools/list", {});
  return (r.result as { tools?: ToolDef[] })?.tools ?? [];
}
