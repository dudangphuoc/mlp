export interface HistoryEntry { op: string; status: string; ms: number; at: number; body: string }

const KEY = "mlp.api-explorer.history";
const MAX = 30;

export function readHistory(): HistoryEntry[] {
  try { return JSON.parse(localStorage.getItem(KEY) ?? "[]") as HistoryEntry[]; } catch { return []; }
}

export function pushHistory(entry: HistoryEntry): HistoryEntry[] {
  const next = [entry, ...readHistory()].slice(0, MAX);
  try { localStorage.setItem(KEY, JSON.stringify(next)); } catch { return next; }
  return next;
}
