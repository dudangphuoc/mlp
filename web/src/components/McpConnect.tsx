import { useQuery } from "@tanstack/react-query";
import { Copy } from "lucide-react";
import { useState } from "react";
import { listMcpTools, modelContext } from "../lib/webmcp";
import { translate as t } from "../i18n";

function Snippet({ title, code }: { title: string; code: string }) {
  const [done, setDone] = useState(false);
  return (
    <div className="card p-0">
      <div className="flex items-center justify-between border-b border-line px-3 py-2 text-xs font-medium">{title}
        <button className="btn-ghost h-8 px-2 text-xs" onClick={() => { void navigator.clipboard.writeText(code); setDone(true); setTimeout(() => setDone(false), 1500); }}>
          <Copy className="h-3.5 w-3.5" />{done ? t("Copied") : t("Copy")}</button>
      </div>
      <pre className="overflow-x-auto p-3 font-mono text-xs">{code}</pre>
    </div>
  );
}

/** Settings → MCP: endpoint, client configurations (Claude Code / Desktop, Cursor, curl), the tools an agent gets. */
export function McpConnect({ go }: { go: (p: string) => void }) {
  const url = `${window.location.origin}/mcp`;
  const tools = useQuery({ queryKey: ["mcp-tools"], queryFn: listMcpTools, staleTime: 60_000 });
  const webmcp = !!modelContext();
  const key = "<API_KEY>";
  return (
    <div className="space-y-4">
      <div className="grid gap-3 md:grid-cols-3">
        <div className="card"><div className="text-xs text-fg-muted">Endpoint</div><div className="mt-1 break-all font-mono text-sm">{url}</div><div className="mt-1 text-xs text-fg-subtle">Streamable HTTP · 2025-06-18</div></div>
        <div className="card"><div className="text-xs text-fg-muted">{t("Authentication")}</div><div className="mt-1 text-sm">Bearer API key</div>
          <button className="mt-1 text-xs text-primary hover:underline" onClick={() => go("settings?tab=api-keys")}>API key →</button></div>
        <div className="card"><div className="text-xs text-fg-muted">WebMCP</div><div className={`mt-1 text-sm ${webmcp ? "text-success" : "text-fg-muted"}`}>{webmcp ? t("Active in this browser") : t("Not available in this browser")}</div>
          <div className="mt-1 text-xs text-fg-subtle">document.modelContext</div></div>
      </div>
      <div className="grid gap-3 xl:grid-cols-2">
        <Snippet title="Claude Code" code={`claude mcp add --transport http mlplatform ${url} \\\n  --header "Authorization: Bearer ${key}"`} />
        <Snippet title="Claude Desktop · Cursor (mcp.json)" code={JSON.stringify({ mcpServers: { mlplatform: { type: "http", url, headers: { Authorization: `Bearer ${key}` } } } }, null, 2)} />
        <Snippet title="curl" code={`curl -s ${url} -H "Authorization: Bearer ${key}" \\\n  -H "content-type: application/json" \\\n  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'`} />
        <Snippet title="Python (mcp)" code={`from mcp import ClientSession\nfrom mcp.client.streamable_http import streamablehttp_client\n\nasync with streamablehttp_client("${url}", headers={"Authorization": "Bearer ${key}"}) as (r, w, _):\n    async with ClientSession(r, w) as s:\n        await s.initialize()\n        print(await s.call_tool("list_labeling_projects", {}))`} />
      </div>
      <div className="card p-0">
        <div className="border-b border-line px-3 py-2 text-sm font-medium">{t("Tools")} <span className="text-fg-subtle">{tools.data?.length ?? ""}</span></div>
        {tools.isLoading ? <div className="skeleton m-3 h-24" /> : (
          <ul className="divide-y divide-line text-sm">{(tools.data ?? []).map((x) => (
            <li key={x.name} className="flex flex-wrap items-baseline gap-x-3 gap-y-0.5 px-3 py-2">
              <span className="font-mono text-xs">{x.name}</span>
              <span className={`rounded px-1.5 text-[10px] ${x.annotations?.readOnlyHint ? "bg-surface-2 text-fg-muted" : "bg-warning/15 text-warning"}`}>{x.annotations?.readOnlyHint ? t("read") : t("changes")}</span>
              <span className="min-w-0 flex-1 basis-64 text-xs text-fg-muted">{x.description}</span>
            </li>))}</ul>)}
      </div>
    </div>
  );
}
