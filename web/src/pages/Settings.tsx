import { McpConnect } from "../components/McpConnect";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import type { Me } from "../gen/mlplatform/v1/iam_pb";
import { CommandBlock, ConfirmDialog, Dialog, ErrorBox, Field, PageHeader, RelTime, ShortId, StateBadge, Tabs } from "../components/ui";
import { DataTable } from "../components/DataTable";
import { NotificationSettingsPage } from "./Notifications";
import { SystemSettings } from "./SystemSettings";
import { ts } from "../lib/format";
import { translate as t } from "../i18n";

type Tab = "profile" | "members" | "api-keys" | "mcp" | "workspace" | "integrations" | "notifications" | "audit" | "system";
const ROLES = ["admin", "editor", "reviewer", "annotator", "viewer"];
const ROLE_LABEL: Record<string, string> = { owner: "Owner", admin: "Admin", editor: "Editor", reviewer: "Reviewer", annotator: "Annotator", viewer: "Viewer" };

/** S-01-2..5, S-01-10, S-06-1, S-16-2 — settings with the tab in the URL (30/02). Tabs show only with permission. */
export function SettingsPage({ ws, me, go }: { ws: string; me: Me; go: (p: string) => void }) {
  const role = me.workspaces.find((w) => w.workspaceId === ws)?.role ?? "viewer";
  const canMembers = role === "owner" || role === "admin";
  const tabs: { key: Tab; label: string }[] = [
    { key: "profile", label: t("Profile") },
    ...(canMembers ? [{ key: "members" as Tab, label: t("Members") }] : []),
    { key: "api-keys", label: "API key" },
    { key: "mcp", label: "MCP" },
    ...(role === "owner" ? [{ key: "workspace" as Tab, label: "Workspace" }] : []),
    { key: "integrations", label: t("Integrations") },
    { key: "notifications", label: t("Notifications") },
    ...(canMembers ? [{ key: "audit" as Tab, label: t("Audit log") }] : []),
    ...(me.isInstanceAdmin ? [{ key: "system" as Tab, label: t("System") }] : []),
  ];
  const raw = (new URLSearchParams(window.location.search).get("tab") as Tab) || "profile";
  const tab = tabs.some((t) => t.key === raw) ? raw : "profile";
  return (
    <>
      <PageHeader title={t("Settings")} subtitle={t("{name} · {role} in this workspace", { name: me.displayName, role: ROLE_LABEL[role] ?? role })} />
      <Tabs<Tab> tabs={tabs} value={tab} onChange={(t) => go(`settings?tab=${t}`)} />
      {tab === "profile" && <Profile me={me} />}
      {tab === "members" && <Members ws={ws} me={me} />}
      {tab === "api-keys" && <ApiKeys />}
      {tab === "mcp" && <McpConnect go={go} />}
      {tab === "workspace" && <WorkspaceTab ws={ws} me={me} />}
      {tab === "integrations" && <Integrations ws={ws} canManage={canMembers} />}
      {tab === "notifications" && <NotificationSettingsPage ws={ws} />}
      {tab === "audit" && <Audit ws={ws} />}
      {tab === "system" && <SystemSettings />}
    </>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="card mb-4 grid gap-4 md:grid-cols-3">
      <div><h2 className="text-sm font-semibold">{title}</h2></div>
      <div className="md:col-span-2">{children}</div>
    </section>
  );
}

function Profile({ me }: { me: Me }) {
  const qc = useQueryClient();
  const [name, setName] = useState(me.displayName);
  const [cur, setCur] = useState("");
  const [nw, setNw] = useState("");
  const [nw2, setNw2] = useState("");
  const save = useMutation({ mutationFn: () => api.iam.updateProfile({ displayName: name }), onSuccess: () => qc.invalidateQueries({ queryKey: ["me"] }) });
  const pw = useMutation({ mutationFn: () => api.iam.changePassword({ currentPassword: cur, newPassword: nw }), onSuccess: () => { setCur(""); setNw(""); setNw2(""); } });
  return (
    <>
      <Section title={t("Information")}>
        <div className="grid max-w-lg gap-3">
          <Field label="Email"><input className="input" value={me.email} disabled /></Field>
          <Field label={t("Username")}><input className="input" value={me.username} disabled /></Field>
          <Field label={t("Display name")}><input className="input" value={name} onChange={(e) => setName(e.target.value)} /></Field>
          <ErrorBox error={save.error ? errorText(save.error) : null} />
          <div className="flex items-center gap-3">
            <button className="btn-primary" disabled={save.isPending || !name.trim() || name === me.displayName} onClick={() => save.mutate()}>{t("Save changes")}</button>
            {save.isSuccess && <span className="text-sm text-success">{t("Saved")}</span>}
          </div>
        </div>
      </Section>
      <Section title={t("Password")}>
        <div className="grid max-w-lg gap-3">
          <Field label={t("Current password")}><input className="input" type="password" autoComplete="current-password" value={cur} onChange={(e) => setCur(e.target.value)} /></Field>
          <Field label={t("New password")}>
            <input className="input" type="password" autoComplete="new-password" value={nw} onChange={(e) => setNw(e.target.value)} />
            {nw && nw.length < 12 && <span className="mt-1 block text-xs text-fg-subtle">{t("{n} more characters needed", { n: 12 - nw.length })}</span>}</Field>
          <Field label={t("Repeat new password")}>
            <input className="input" type="password" autoComplete="new-password" value={nw2} onChange={(e) => setNw2(e.target.value)} />
            {nw2 && nw2 !== nw && <span className="mt-1 block text-xs text-fg-subtle">{t("Does not match")}</span>}</Field>
          <ErrorBox error={pw.error ? errorText(pw.error) : null} />
          <div className="flex items-center gap-3">
            <button className="btn-primary" disabled={pw.isPending || !cur || nw.length < 12 || nw !== nw2} onClick={() => pw.mutate()}>{t("Change password")}</button>
            {pw.isSuccess && <span className="text-sm text-success">{t("Password changed")}</span>}
          </div>
        </div>
      </Section>
    </>
  );
}

function Members({ ws, me }: { ws: string; me: Me }) {
  const qc = useQueryClient();
  const members = useQuery({ queryKey: ["members", ws], queryFn: () => api.workspace.listMembers({ workspaceId: ws }) });
  const invites = useQuery({ queryKey: ["invites", ws], queryFn: () => api.workspace.listInvitations({ workspaceId: ws }) });
  const [inviting, setInviting] = useState(false);
  const [removing, setRemoving] = useState<{ id: string; name: string } | null>(null);
  const refresh = () => { void qc.invalidateQueries({ queryKey: ["members", ws] }); void qc.invalidateQueries({ queryKey: ["invites", ws] }); };
  const setRole = useMutation({ mutationFn: (v: { userId: string; role: string }) => api.workspace.updateMemberRole({ workspaceId: ws, ...v }), onSuccess: refresh });
  const remove = useMutation({ mutationFn: (userId: string) => api.workspace.removeMember({ workspaceId: ws, userId }), onSuccess: () => { setRemoving(null); refresh(); } });
  const revoke = useMutation({ mutationFn: (id: string) => api.workspace.revokeInvitation({ workspaceId: ws, id }), onSuccess: refresh });
  const err = members.error ?? setRole.error ?? remove.error ?? revoke.error;
  const pending = (invites.data?.invitations ?? []).filter((i) => !i.acceptedAt);
  return (
    <>
      <ErrorBox error={err ? errorText(err) : null} />
      <DataTable rows={members.data?.members ?? []} loading={members.isLoading} rowKey={(m) => m.userId}
        actions={<button className="btn-primary h-8" onClick={() => setInviting(true)}>{t("+ Invite member")}</button>}
        columns={[
          { key: "name", header: t("Name"), value: (m) => m.displayName, cell: (m) => <div><div className="font-medium">{m.displayName}{m.userId === me.userId && <span className="ml-2 text-xs text-fg-subtle">{t("(you)")}</span>}</div><div className="text-xs text-fg-subtle">@{m.username}</div></div> },
          { key: "email", header: "Email", value: (m) => m.email, cell: (m) => <span className="text-fg-muted">{m.email}</span> },
          { key: "role", header: t("Role"), value: (m) => m.role, cell: (m) => m.role === "owner" || m.userId === me.userId
              ? <span className="badge bg-surface-2 text-fg-muted">{ROLE_LABEL[m.role] ?? m.role}</span>
              : <select className="input h-8 w-36" value={m.role} disabled={setRole.isPending} onClick={(e) => e.stopPropagation()}
                  onChange={(e) => setRole.mutate({ userId: m.userId, role: e.target.value })}>
                  {ROLES.map((r) => <option key={r} value={r}>{ROLE_LABEL[r]}</option>)}</select> },
          { key: "actions", header: "", align: "right", cell: (m) => m.role !== "owner" && m.userId !== me.userId &&
              <button className="text-xs text-danger hover:underline" onClick={() => setRemoving({ id: m.userId, name: m.displayName })}>{t("Remove")}</button> },
        ]} />
      {pending.length > 0 && (
        <div className="mt-6">
          <h2 className="mb-2 text-sm font-semibold">{t("Pending invitations ({n})", { n: pending.length })}</h2>
          <DataTable rows={pending} rowKey={(i) => i.id} search={false} urlKey="inv"
            columns={[
              { key: "email", header: "Email", value: (i) => i.email, cell: (i) => i.email },
              { key: "role", header: t("Role"), value: (i) => i.role, cell: (i) => ROLE_LABEL[i.role] ?? i.role },
              { key: "created", header: t("Sent"), value: (i) => ts(i.createdAt)?.getTime(), cell: (i) => <RelTime at={i.createdAt} /> },
              { key: "exp", header: t("Expires"), value: (i) => ts(i.expiresAt)?.getTime(), cell: (i) => (ts(i.expiresAt)?.getTime() ?? 0) < Date.now()
                  ? <span className="text-danger">{t("expired")}</span> : <RelTime at={i.expiresAt} /> },
              { key: "a", header: "", align: "right", cell: (i) => <button className="text-xs text-danger hover:underline" onClick={() => revoke.mutate(i.id)}>{t("Revoke")}</button> },
            ]} />
        </div>)}
      {inviting && <InviteDialog ws={ws} onClose={() => { setInviting(false); refresh(); }} />}
      {removing && <ConfirmDialog title={t("Remove {name} from the workspace?", { name: removing.name })} label={t("Remove member")} busy={remove.isPending}
        consequence={t("This person immediately loses access to every resource in the workspace. Data they created is kept.")}
        onClose={() => setRemoving(null)} onConfirm={() => remove.mutate(removing.id)} />}
    </>
  );
}

function InviteDialog({ ws, onClose }: { ws: string; onClose: () => void }) {
  const [email, setEmail] = useState("");
  const [role, setRole] = useState("editor");
  const m = useMutation({ mutationFn: () => api.workspace.createInvitation({ workspaceId: ws, email, role }) });
  const link = m.data ? `${window.location.origin}/#/signup?invite=${m.data.token}` : "";
  return (
    <Dialog title={t("Invite member")} onClose={onClose}>
      {!m.data ? (
        <div className="space-y-3">
          <Field label="Email"><input className="input" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoFocus /></Field>
          <Field label={t("Role")}><select className="input" value={role} onChange={(e) => setRole(e.target.value)}>{ROLES.map((r) => <option key={r} value={r}>{ROLE_LABEL[r]}</option>)}</select></Field>
          <ErrorBox error={m.error ? errorText(m.error) : null} />
          <div className="flex justify-end gap-2"><button className="btn-ghost" onClick={onClose}>{t("Cancel")}</button>
            <button className="btn-primary" disabled={!email.includes("@") || m.isPending} onClick={() => m.mutate()}>{t("Send invitation")}</button></div>
        </div>
      ) : (
        <div className="space-y-3 text-sm">
          <p className="text-fg-muted">{t("Invited")} <b className="text-fg">{email}</b></p>
          <CommandBlock text={link} />
          <div className="flex justify-end"><button className="btn-primary" onClick={onClose}>Xong</button></div>
        </div>)}
    </Dialog>
  );
}

function ApiKeys() {
  const qc = useQueryClient();
  const keys = useQuery({ queryKey: ["api-keys"], queryFn: () => api.iam.listApiKeys({}) });
  const [name, setName] = useState("");
  const [created, setCreated] = useState<string | null>(null);
  const [revoking, setRevoking] = useState<{ id: string; name: string } | null>(null);
  const create = useMutation({ mutationFn: () => api.iam.createApiKey({ name }), onSuccess: (r) => { setCreated(r.token); setName(""); void qc.invalidateQueries({ queryKey: ["api-keys"] }); } });
  const revoke = useMutation({ mutationFn: (id: string) => api.iam.revokeApiKey({ id }), onSuccess: () => { setRevoking(null); void qc.invalidateQueries({ queryKey: ["api-keys"] }); } });
  return (
    <>
      <Section title={t("Create API key")}>
        <div className="flex max-w-lg gap-2">
          <input className="input" placeholder={t("Key name, e.g. laptop-cli")} value={name} onChange={(e) => setName(e.target.value)} />
          <button className="btn-primary shrink-0" disabled={!name.trim() || create.isPending} onClick={() => create.mutate()}>{t("Create key")}</button>
        </div>
        <ErrorBox error={create.error ? errorText(create.error) : null} />
        {created && (
          <div className="mt-3 space-y-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
            <div>{t("Copy the key now — it will not be shown again.")}</div>
            <CommandBlock text={created} />
            <button className="text-xs underline" onClick={() => setCreated(null)}>{t("Saved, hide it")}</button>
          </div>)}
      </Section>
      <DataTable rows={keys.data?.keys ?? []} loading={keys.isLoading} rowKey={(k) => k.id} search={false} empty={t("No API keys yet.")}
        columns={[
          { key: "name", header: t("Name"), value: (k) => k.name, cell: (k) => <span className="font-medium">{k.name}</span> },
          { key: "id", header: "ID", cell: (k) => <ShortId id={k.id} /> },
          { key: "created", header: t("Created"), value: (k) => ts(k.createdAt)?.getTime(), cell: (k) => <RelTime at={k.createdAt} /> },
          { key: "used", header: t("Last used"), value: (k) => ts(k.lastUsedAt)?.getTime(), cell: (k) => <RelTime at={k.lastUsedAt} /> },
          { key: "a", header: "", align: "right", cell: (k) => <button className="text-xs text-danger hover:underline" onClick={() => setRevoking({ id: k.id, name: k.name })}>{t("Revoke")}</button> },
        ]} />
      {revoking && <ConfirmDialog title={t("Revoke key \"{name}\"?", { name: revoking.name })} label={t("Revoke")} busy={revoke.isPending}
        consequence={t("Every script using this key will be rejected immediately. This cannot be undone.")} confirmText={revoking.name}
        onClose={() => setRevoking(null)} onConfirm={() => revoke.mutate(revoking.id)} />}
    </>
  );
}

function WorkspaceTab({ ws, me }: { ws: string; me: Me }) {
  const qc = useQueryClient();
  const cur = me.workspaces.find((w) => w.workspaceId === ws);
  const [name, setName] = useState(cur?.name ?? "");
  const m = useMutation({ mutationFn: () => api.workspace.updateWorkspace({ workspaceId: ws, name }), onSuccess: () => qc.invalidateQueries({ queryKey: ["me"] }) });
  return (
    <Section title="Workspace">
      <div className="grid max-w-lg gap-3">
        <Field label={t("Name")}><input className="input" value={name} onChange={(e) => setName(e.target.value)} /></Field>
        <Field label="Slug"><input className="input mono" value={cur?.slug ?? ""} disabled /></Field>
        <Field label="ID"><div><ShortId id={ws} /></div></Field>
        <ErrorBox error={m.error ? errorText(m.error) : null} />
        <div className="flex items-center gap-3">
          <button className="btn-primary" disabled={m.isPending || !name.trim() || name === cur?.name} onClick={() => m.mutate()}>{t("Save")}</button>
          {m.isSuccess && <span className="text-sm text-success">{t("Saved")}</span>}
        </div>
      </div>
    </Section>
  );
}

function Audit({ ws }: { ws: string }) {
  const q = useQuery({ queryKey: ["audit", ws], queryFn: () => api.audit.listEvents({ workspaceId: ws }) });
  const members = useQuery({ queryKey: ["members", ws], queryFn: () => api.workspace.listMembers({ workspaceId: ws }) });
  const who = (id: string) => members.data?.members.find((m) => m.userId === id)?.displayName ?? id.slice(0, 8);
  return (
    <>
      {q.data && (
        <div className={`mb-3 inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm ${q.data.chainValid ? "bg-emerald-50 text-emerald-700" : "bg-rose-50 text-rose-700"}`}>
          {q.data.chainValid ? t("✓ Audit hash chain intact") : t("✕ Audit hash chain broken — a record was modified")}
        </div>)}
      <ErrorBox error={q.error ? errorText(q.error) : null} />
      <DataTable rows={q.data?.events ?? []} loading={q.isLoading} rowKey={(e) => String(e.seq)} pageSize={100}
        columns={[
          { key: "seq", header: "#", value: (e) => e.seq, cell: (e) => <span className="mono text-xs text-fg-subtle">{String(e.seq)}</span>, width: "70px" },
          { key: "at", header: t("Time"), value: (e) => ts(e.at)?.getTime(), cell: (e) => <RelTime at={e.at} /> },
          { key: "actor", header: "Ai", value: (e) => who(e.actorId), cell: (e) => e.actorId ? who(e.actorId) : <span className="text-fg-subtle">{t("system")}</span> },
          { key: "action", header: t("Action"), value: (e) => e.action, cell: (e) => <span className="mono text-xs">{e.action}</span> },
          { key: "outcome", header: t("Outcome"), value: (e) => e.outcome, cell: (e) => <StateBadge state={e.outcome === "success" ? "succeeded" : e.outcome === "denied" ? "failed" : e.outcome} reason={e.reason || undefined} /> },
        ]} />
    </>
  );
}

/** S-01-10: workspace Hugging Face token (reads gated/private repos, downloads weights). The secret is never returned to the client (M01 R10). */
function Integrations({ ws, canManage }: { ws: string; canManage: boolean }) {
  const qc = useQueryClient();
  const st = useQuery({ queryKey: ["hub-token", ws], queryFn: () => api.hub.getHubToken({ workspaceId: ws }) });
  const [token, setToken] = useState("");
  const [del, setDel] = useState(false);
  const save = useMutation({ mutationFn: () => api.hub.setHubToken({ workspaceId: ws, token }), onSuccess: () => { setToken(""); void qc.invalidateQueries({ queryKey: ["hub-token", ws] }); } });
  const remove = useMutation({ mutationFn: () => api.hub.deleteHubToken({ workspaceId: ws }), onSuccess: () => { setDel(false); void qc.invalidateQueries({ queryKey: ["hub-token", ws] }); } });
  const s = st.data;
  return (
    <Section title="🤗 Hugging Face">
      <div className="max-w-lg space-y-3">
        {st.isLoading ? <div className="skeleton h-10" /> : s?.configured ? (
          <div className="flex items-center justify-between gap-3 rounded-lg border border-line bg-surface-2 px-3 py-2 text-sm">
            <div>
              <div className="font-medium">{s.account ? `@${s.account}` : t("Saved token")} <span className="mono text-fg-subtle">hf_••••</span></div>
              <div className="text-xs text-fg-muted">{s.status === "ok" ? t("✔ valid") : t("✕ invalid")} · {t("checked")} <RelTime at={s.checkedAt} /></div>
            </div>
            {canManage && <button className="text-xs text-danger hover:underline" onClick={() => setDel(true)}>{t("Delete")}</button>}
          </div>
        ) : <div className="text-sm text-fg-muted">{t("No token — public repos can still be imported.")}</div>}
        {canManage && (
          <>
            <Field label={s?.configured ? "Thay token" : "Token"}>
              <input className="input mono" type="password" autoComplete="off" placeholder="hf_…" value={token} onChange={(e) => setToken(e.target.value)} />
            </Field>
            <ErrorBox error={save.error ? errorText(save.error) : null} />
            <button className="btn-primary" disabled={!token.startsWith("hf_") || save.isPending} onClick={() => save.mutate()}>{t("Check & save")}</button>
            {save.data && save.data.status !== "ok" && <div className="text-sm text-danger">{t("Hugging Face rejected this token (saved but marked invalid).")}</div>}
          </>)}
      </div>
      {del && <ConfirmDialog title={t("Delete the Hugging Face token?")} label={t("Delete token")} busy={remove.isPending}
        consequence={t("Gated/private repos can no longer be imported; running jobs are not affected.")} onClose={() => setDel(false)} onConfirm={() => remove.mutate()} />}
    </Section>
  );
}
