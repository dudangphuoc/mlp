import { useEffect, useSyncExternalStore } from "react";

/** Names of entities shown in the breadcrumb (id → name). Pages register the entity they load. */
const names = new Map<string, string>();
const subs = new Set<() => void>();
let snapshot = 0;

export function useCrumb(id: string | undefined, name: string | undefined) {
  useEffect(() => {
    if (!id || !name || names.get(id) === name) return;
    names.set(id, name);
    snapshot++;
    subs.forEach((f) => f());
  }, [id, name]);
}

export function useCrumbNames(): (id: string) => string | undefined {
  useSyncExternalStore((f) => { subs.add(f); return () => subs.delete(f); }, () => snapshot);
  return (id) => names.get(id);
}
