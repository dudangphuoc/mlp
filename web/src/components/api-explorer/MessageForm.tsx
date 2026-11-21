import { ScalarType, type DescField, type DescMessage, type JsonValue } from "@bufbuild/protobuf";
import { Field } from "../ui";
import { IdPicker } from "./IdPicker";

type JsonObject = Record<string, JsonValue>;

function typeLabel(f: DescField): string {
  if (f.fieldKind === "scalar") return ScalarType[f.scalar].toLowerCase();
  if (f.fieldKind === "enum") return f.enum.name;
  if (f.fieldKind === "message") return f.message.name;
  if (f.fieldKind === "list") return `${f.listKind === "message" ? f.message.name : f.listKind === "enum" ? f.enum.name : ScalarType[f.scalar].toLowerCase()}[]`;
  return "map";
}

const NUMERIC = new Set([ScalarType.DOUBLE, ScalarType.FLOAT, ScalarType.INT32, ScalarType.UINT32, ScalarType.SINT32, ScalarType.FIXED32, ScalarType.SFIXED32]);

export function MessageForm({ schema, value, onChange, refs, workspaceId }:
  { schema: DescMessage; value: JsonObject; onChange: (v: JsonObject) => void; refs: Record<string, { list?: string | null }>; workspaceId: string }) {
  const set = (key: string, v: JsonValue | undefined) => {
    const next = { ...value };
    if (v === undefined) delete next[key]; else next[key] = v;
    onChange(next);
  };
  return (
    <div className="space-y-3">
      {schema.fields.map((f) => {
        const key = f.jsonName;
        const current = value[key];
        const label = `${f.name} · ${typeLabel(f)}`;
        const ref = refs[f.name];
        if (f.fieldKind === "scalar" && f.scalar === ScalarType.BOOL)
          return <label key={key} className="flex items-center gap-2 text-sm"><input type="checkbox" checked={current === true} onChange={(e) => set(key, e.target.checked)} /><span className="font-mono text-xs">{label}</span></label>;
        if (f.fieldKind === "enum")
          return (
            <Field key={key} label={label}>
              <select className="input" value={String(current ?? f.enum.values[0]?.name ?? "")} onChange={(e) => set(key, e.target.value)}>
                {f.enum.values.map((v) => <option key={v.name} value={v.name}>{v.name}</option>)}
              </select>
            </Field>
          );
        if (f.fieldKind === "scalar" && ref?.list && f.name !== "workspace_id")
          return <Field key={key} label={label}><IdPicker list={ref.list} workspaceId={workspaceId} value={String(current ?? "")} onChange={(v) => set(key, v)} /></Field>;
        if (f.fieldKind === "scalar")
          return (
            <Field key={key} label={label}>
              <input className="input font-mono text-xs" type={NUMERIC.has(f.scalar) ? "number" : "text"} value={String(current ?? "")}
                onChange={(e) => set(key, NUMERIC.has(f.scalar) ? (e.target.value === "" ? undefined : Number(e.target.value)) : e.target.value)} />
            </Field>
          );
        return (
          <Field key={key} label={label}>
            <textarea className="input min-h-20 font-mono text-xs" defaultValue={JSON.stringify(current ?? (f.fieldKind === "map" ? {} : f.fieldKind === "list" ? [] : {}), null, 2)}
              onBlur={(e) => { try { set(key, JSON.parse(e.target.value) as JsonValue); } catch { /* keeps last valid value */ } }} />
          </Field>
        );
      })}
    </div>
  );
}
