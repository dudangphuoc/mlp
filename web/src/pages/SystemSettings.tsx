import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import type { Setting } from "../gen/mlplatform/v1/settings_pb";
import { ErrorBox, Field, RelTime, Skeleton } from "../components/ui";
import { translate as t } from "../i18n";

/** Dynamic instance configuration (email, Telegram, …) — Instance Admin only. The field catalog is defined by the server. */
export function SystemSettings() {
  const q = useQuery({ queryKey: ["system-settings"], queryFn: () => api.settings.listSettings({}) });
  if (q.isLoading) return <Skeleton rows={4} />;
  return (
    <>
      <ErrorBox error={q.error ? errorText(q.error) : null} onRetry={() => void q.refetch()} />
      <div className="grid items-start gap-4 2xl:grid-cols-2">{(q.data?.settings ?? []).map((s) => <SettingCard key={s.key} s={s} />)}</div>
    </>
  );
}

function SettingCard({ s }: { s: Setting }) {
  const qc = useQueryClient();
  const init = () => Object.fromEntries(s.fields.map((f) => [f.key, f.secret ? "" : f.value]));
  const [vals, setVals] = useState<Record<string, string>>(init);
  const [enabled, setEnabled] = useState(s.enabled);
  const [to, setTo] = useState("");
  const [test, setTest] = useState<{ ok: boolean; message: string } | null>(null);
  useEffect(() => { setVals(init()); setEnabled(s.enabled); }, [s]); // eslint-disable-line react-hooks/exhaustive-deps
  const save = useMutation({
    mutationFn: () => api.settings.updateSetting({ key: s.key, enabled, values: vals }),
    onSuccess: () => { setTest(null); void qc.invalidateQueries({ queryKey: ["system-settings"] }); },
  });
  const run = useMutation({ mutationFn: () => api.settings.testSetting({ key: s.key, to }), onSuccess: (r) => setTest({ ok: r.ok, message: r.message }) });
  const set = (k: string, v: string) => setVals((x) => ({ ...x, [k]: v }));
  return (
    <section className="card mb-4">
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold">{t(s.title)}</h2>
          <p className="mt-1 max-w-xl text-xs text-fg-muted">{t(s.description)}</p>
          {s.updatedAt && <p className="mt-1 text-xs text-fg-subtle">{t("Updated")} <RelTime at={s.updatedAt} /></p>}
        </div>
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />{t("Enabled")}
        </label>
      </div>
      <ErrorBox error={save.error ? errorText(save.error) : run.error ? errorText(run.error) : null} />
      <div className="grid gap-3 md:grid-cols-2">
        {s.fields.map((f) => (
          <Field key={f.key} label={t(f.label) + (f.required ? " *" : "")}>
            {f.type === "bool" ? (
              <select className="input" value={vals[f.key] || "false"} onChange={(e) => set(f.key, e.target.value)}>
                <option value="true">{t("Yes")}</option><option value="false">{t("No")}</option>
              </select>
            ) : f.type === "select" ? (
              <select className="input" value={vals[f.key]} onChange={(e) => set(f.key, e.target.value)}>
                {f.options.map((o) => <option key={o} value={o}>{o}</option>)}
              </select>
            ) : (
              <input className="input" type={f.secret ? "password" : f.type === "number" ? "number" : "text"} autoComplete="off"
                placeholder={f.secret && f.hasValue ? "••••••••" : ""} value={vals[f.key] ?? ""} onChange={(e) => set(f.key, e.target.value)} />
            )}
          </Field>))}
      </div>
      <div className="mt-4 flex flex-wrap items-end justify-between gap-3 border-t border-line pt-4">
        <div className="flex flex-wrap items-end gap-2">
          <Field label={t(s.testHint)}><input className="input w-64" value={to} onChange={(e) => setTo(e.target.value)} /></Field>
          <button className="btn-ghost" disabled={run.isPending || !s.updatedAt} onClick={() => run.mutate()}>{run.isPending ? t("Sending…") : t("Send test")}</button>
          {test && <span className={`text-sm ${test.ok ? "text-success" : "text-danger"}`}>{test.ok ? "✓ " : "✕ "}{t(test.message)}</span>}
        </div>
        <button className="btn-primary" disabled={save.isPending} onClick={() => save.mutate()}>{save.isPending ? t("Saving…") : t("Save")}</button>
      </div>
    </section>
  );
}
