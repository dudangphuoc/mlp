import { useCallback, useState } from "react";

/*
 * UI state that survives reloads (owner rule: refreshing the page must never lose what the user set up).
 * Everything lives in localStorage under "mlp.ui.*". Storage may be blocked (private mode) — then state is per-tab only.
 */
const PREFIX = "mlp.ui.";

export function readSticky<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(PREFIX + key);
    return raw === null ? fallback : (JSON.parse(raw) as T);
  } catch { return fallback; }
}

export function writeSticky<T>(key: string, value: T) {
  try {
    if (value === undefined || value === null || value === "") localStorage.removeItem(PREFIX + key);
    else localStorage.setItem(PREFIX + key, JSON.stringify(value));
  } catch { /* storage blocked */ }
}

/** useState that is remembered in localStorage. */
export function useSticky<T>(key: string, initial: T): [T, (v: T | ((old: T) => T)) => void] {
  const [v, setV] = useState<T>(() => readSticky(key, initial));
  const set = useCallback((nv: T | ((old: T) => T)) => {
    setV((old) => {
      const next = typeof nv === "function" ? (nv as (o: T) => T)(old) : nv;
      writeSticky(key, next);
      return next;
    });
  }, [key]);
  return [v, set];
}

/** Stable id of the current page for per-page state: path without the workspace segment and without ids. */
export function pageKey(): string {
  return window.location.pathname
    .replace(/^\/w\/[^/]+/, "")
    .split("/")
    .map((p) => (/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(p) ? ":id" : p))
    .join("/") || "/";
}

/** Query string last used on each sidebar section (tabs, filters…), restored when the user navigates back to it. */
export const lastQuery = {
  get: (section: string) => readSticky<string>("query:" + section, ""),
  set: (section: string, search: string) => writeSticky("query:" + section, search.replace(/^\?/, "")),
};

/** Pages update their own query with history.replaceState (filters, search); announce it so the shell can remember it. */
let watching = false;
export function watchUrl(onChange: () => void): () => void {
  if (!watching) {
    watching = true;
    const orig = window.history.replaceState.bind(window.history);
    window.history.replaceState = (data: unknown, unused: string, url?: string | URL | null) => {
      orig(data, unused, url);
      window.dispatchEvent(new Event("mlp:url"));
    };
  }
  window.addEventListener("mlp:url", onChange);
  window.addEventListener("popstate", onChange);
  return () => { window.removeEventListener("mlp:url", onChange); window.removeEventListener("popstate", onChange); };
}
