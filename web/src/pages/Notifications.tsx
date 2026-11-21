import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { ago } from "../lib/format";
import { ErrorBox } from "../components/ui";
import { translate as t } from "../i18n";
import { Bell, CircleCheck, CircleX, Info, Settings2, TriangleAlert, type LucideIcon } from "lucide-react";

const icon: Record<string, LucideIcon> = { error: CircleX, warning: TriangleAlert, success: CircleCheck, info: Info };
const tone: Record<string, string> = { error: "text-rose-600", warning: "text-amber-600", success: "text-emerald-600", info: "text-sky-600" };

/** Realtime unread count via WatchNotifications (server stream); reconnects automatically on disconnect. */
function useUnread(ws: string) {
  const qc = useQueryClient();
  const [unread, setUnread] = useState(0);
  useEffect(() => {
    const ctrl = new AbortController();
    let stopped = false;
    (async () => {
      while (!stopped) {
        try {
          for await (const ev of api.notify.watchNotifications({ workspaceId: ws }, { signal: ctrl.signal })) {
            setUnread(Number(ev.unread));
            if (ev.notification) qc.invalidateQueries({ queryKey: ["notifications", ws] });
          }
        } catch { /* connection dropped → retry */ }
        if (!stopped) await new Promise((r) => setTimeout(r, 3000));
      }
    })();
    return () => { stopped = true; ctrl.abort(); };
  }, [ws, qc]);
  return [unread, setUnread] as const;
}

/** S-16-1 Notification center: bell + dropdown panel. */
export function NotificationBell({ ws, go }: { ws: string; go: (path: string) => void }) {
  const qc = useQueryClient();
  const [unread, setUnread] = useUnread(ws);
  const [open, setOpen] = useState(false);
  const [onlyUnread, setOnlyUnread] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  const list = useQuery({ queryKey: ["notifications", ws, onlyUnread], enabled: open,
    queryFn: () => api.notify.listNotifications({ workspaceId: ws, unreadOnly: onlyUnread }) });
  const mark = useMutation({
    mutationFn: (v: { ids?: string[]; all?: boolean }) => api.notify.markRead({ workspaceId: ws, ids: v.ids ?? [], all: v.all ?? false }),
    onSuccess: (r) => { setUnread(Number(r.unread)); qc.invalidateQueries({ queryKey: ["notifications", ws] }); },
  });
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => { if (box.current && !box.current.contains(e.target as Node)) setOpen(false); };
    const esc = (e: KeyboardEvent) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", close);
    document.addEventListener("keydown", esc);
    return () => { document.removeEventListener("mousedown", close); document.removeEventListener("keydown", esc); };
  }, [open]);
  // Notification links like "#/deployments" (from the server) → path within the current workspace.
  const openLink = (link: string) => { setOpen(false); go(link.replace(/^#?\/?/, "")); };

  return (
    <div ref={box} className="relative">
      <button className="relative inline-flex h-8 items-center gap-1.5 rounded-md px-2 text-sm text-fg-muted hover:bg-surface-2 hover:text-fg"
        onClick={() => setOpen(!open)} aria-expanded={open} aria-label={t("Notifications, {n} unread", { n: unread })}>
        <Bell className="h-4 w-4" aria-hidden /><span className="hidden xl:inline">{t("Notifications")}</span>
        {unread > 0 && <span className="rounded-full bg-danger px-1.5 text-[11px] font-semibold leading-4 text-white">{unread > 99 ? "99+" : unread}</span>}
      </button>
      {open && (
        <div className="absolute right-0 top-full z-50 mt-2 w-[min(24rem,calc(100vw-2rem))] overflow-hidden rounded-xl border border-line bg-overlay text-fg shadow-2xl">
          <div className="flex items-center justify-between border-b border-line px-3 py-2 text-sm">
            <span className="font-medium">{t("Notifications")}</span>
            <div className="flex items-center gap-3 text-xs">
              <button className="text-primary hover:underline disabled:opacity-50" disabled={unread === 0} onClick={() => mark.mutate({ all: true })}>{t("Mark all as read")}</button>
              <button className="rounded px-1 text-fg-muted hover:bg-surface-2 hover:text-fg" onClick={() => { setOpen(false); go("settings?tab=notifications"); }} aria-label={t("Notification preferences")}><Settings2 className="h-4 w-4" /></button>
            </div>
          </div>
          <div className="flex gap-1 px-3 pt-2 text-xs">
            <button className={`rounded px-2 py-0.5 ${!onlyUnread ? "bg-surface-2 font-medium text-fg" : "text-fg-muted"}`} onClick={() => setOnlyUnread(false)}>{t("All")}</button>
            <button className={`rounded px-2 py-0.5 ${onlyUnread ? "bg-surface-2 font-medium text-fg" : "text-fg-muted"}`} onClick={() => setOnlyUnread(true)}>{t("Unread")} {unread}</button>
          </div>
          <div className="max-h-96 overflow-y-auto py-1">
            {(list.data?.notifications ?? []).map((n) => (
              <button key={n.id} className="flex w-full items-start gap-2 px-3 py-2 text-left text-sm hover:bg-surface-2"
                onClick={() => { if (!n.read) mark.mutate({ ids: [n.id] }); openLink(n.link); }}>
                <span className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${n.read ? "" : "bg-primary"}`} />
                {(() => { const I = icon[n.severity] ?? Info; return <I className={`mt-0.5 h-4 w-4 shrink-0 ${tone[n.severity] ?? ""}`} />; })()}
                <span className="min-w-0 flex-1">
                  <span className={n.read ? "text-fg-muted" : "text-fg"}>{t(n.title)}</span>
                  {n.count > 1 && <span className="ml-1 text-xs text-fg-subtle">&times;{n.count}</span>}
                  <span className="block text-xs text-fg-subtle">{n.updatedAt ? ago(n.updatedAt) : ""}</span>
                </span>
              </button>))}
            {list.data?.notifications.length === 0 && <div className="px-3 py-8 text-center text-sm text-fg-subtle">{t("No notifications.")}</div>}
            {list.isLoading && <div className="px-3 py-8 text-center text-sm text-fg-subtle">{t("Loading…")}</div>}
            {list.error && <div className="px-3 py-4 text-sm text-danger">{errorText(list.error)}</div>}
          </div>
        </div>)}
    </div>
  );
}

/** S-16-2 Notification preferences: type x channel grid (P1 is "In app" only; Email/Slack/Webhook are P2). */
export function NotificationSettingsPage({ ws }: { ws: string }) {
  const qc = useQueryClient();
  const prefs = useQuery({ queryKey: ["notify-prefs", ws], queryFn: () => api.notify.getPreferences({ workspaceId: ws }) });
  const update = useMutation({
    mutationFn: (changes: { kind: string; inApp: boolean }[]) => api.notify.updatePreferences({ workspaceId: ws, preferences: changes }),
    onSuccess: (r) => qc.setQueryData(["notify-prefs", ws], r),
  });
  const rows = prefs.data?.preferences ?? [];
  const groups = [...new Set(rows.map((r) => r.group))];
  return (
    <>
      <ErrorBox error={update.error ? errorText(update.error) : prefs.error ? errorText(prefs.error) : null} />
      <div className="card p-0">
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-xs text-slate-500">
              <th className="p-3">{t("Kind")}</th><th className="w-28">{t("In app")}</th>
              <th className="w-24 text-slate-300">Email <span className="text-[10px]">P2</span></th>
              <th className="w-24 text-slate-300">Slack <span className="text-[10px]">P2</span></th>
              <th className="w-24 text-slate-300">Webhook <span className="text-[10px]">P2</span></th>
            </tr>
          </thead>
          {groups.map((g) => {
            const items = rows.filter((r) => r.group === g);
            const changeable = items.filter((r) => !r.mandatory);
            return (
              <tbody key={g}>
                <tr className="bg-slate-50 text-xs font-semibold text-slate-500">
                  <td className="px-3 py-1.5">{g}</td>
                  <td colSpan={4} className="space-x-3">
                    <button className="text-brand-500" onClick={() => update.mutate(changeable.map((r) => ({ kind: r.kind, inApp: true })))}>{t("Enable all")}</button>
                    <button className="text-brand-500" onClick={() => update.mutate(changeable.map((r) => ({ kind: r.kind, inApp: false })))}>{t("Disable all")}</button>
                  </td>
                </tr>
                {items.map((r) => (
                  <tr key={r.kind} className="border-t border-slate-100">
                    <td className="px-3 py-2">{t(r.label)}{r.mandatory && <span className="ml-2 text-xs text-slate-400">{t("(required)")}</span>}</td>
                    <td>
                      <input type="checkbox" aria-label={`${t(r.label)} trong app`} checked={r.inApp} disabled={r.mandatory || update.isPending}
                        onChange={(e) => update.mutate([{ kind: r.kind, inApp: e.target.checked }])} />
                    </td>
                    <td><input type="checkbox" disabled aria-label="Email (P2)" /></td>
                    <td><input type="checkbox" disabled aria-label="Slack (P2)" /></td>
                    <td><input type="checkbox" disabled aria-label="Webhook (P2)" /></td>
                  </tr>))}
              </tbody>);
          })}
        </table>
      </div>
    </>
  );
}
