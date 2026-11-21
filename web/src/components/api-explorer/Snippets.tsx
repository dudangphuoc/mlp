import { useState } from "react";
import { CommandBlock, Tabs } from "../ui";
import type { Operation } from "../../lib/openapi";

type Lang = "curl" | "buf" | "ts";

export function Snippets({ op, body, deploymentKey }: { op: Operation; body: string; deploymentKey?: string }) {
  const origin = window.location.origin;
  const langs: { key: Lang; label: string }[] = op.grpc
    ? [{ key: "buf", label: "buf curl" }, { key: "ts", label: "TypeScript" }]
    : [{ key: "curl", label: "curl" }];
  const [lang, setLang] = useState<Lang>(langs[0].key);
  const compact = JSON.stringify(JSON.parse(body || "{}"));
  const text =
    lang === "curl" ? `curl -X ${op.verb} ${origin}${op.path} \\\n  -H "Authorization: Bearer ${deploymentKey || "mlpd_…"}" -H "Content-Type: application/json" \\\n  -d '${compact}'`
    : lang === "buf" ? `buf curl --protocol grpcweb --schema proto \\\n  -H "Authorization: Bearer mlp_…" \\\n  -d '${compact}' \\\n  ${origin}/${op.grpc!.service}/${op.grpc!.method}`
    : `const client = createClient(${op.service}, transport);\nawait client.${op.grpc!.method.charAt(0).toLowerCase()}${op.grpc!.method.slice(1)}(${compact});`;
  return (
    <div className="space-y-2">
      {langs.length > 1 && <Tabs tabs={langs} value={lang} onChange={setLang} />}
      <CommandBlock text={text} secret={deploymentKey || undefined} />
    </div>
  );
}
