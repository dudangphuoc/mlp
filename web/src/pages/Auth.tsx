import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api, errorText } from "../lib/api";
import { ErrorBox, Field } from "../components/ui";
import { ArrowRight, Boxes, CircleAlert, Cpu, Eye, EyeOff, Languages, Layers, Loader2, Lock, Mail, Moon, ShieldCheck, Sun, UserPlus } from "lucide-react";
import { setLang, translate as t, useT } from "../i18n";

/** S-01-8 First-run setup (D-27) — the token comes from the api log. */
export function SetupPage() {
  const qc = useQueryClient();
  const params = new URLSearchParams(window.location.search);
  const [f, setF] = useState({ token: params.get("token") ?? "", email: "", username: "", displayName: "", password: "", instanceName: "MLPlatform", allowSignup: false });
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setF({ ...f, [k]: e.target.type === "checkbox" ? e.target.checked : e.target.value });
  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true); setErr(null);
    try {
      await api.instance.completeSetup({ bootstrapToken: f.token, email: f.email, password: f.password, username: f.username,
        displayName: f.displayName, instanceName: f.instanceName, allowSignup: f.allowSignup });
      window.history.replaceState(null, "", "/");
      await qc.invalidateQueries();
    } catch (x) { setErr(errorText(x)); } finally { setBusy(false); }
  }
  return (
    <AuthShell title={t("Set up MLPlatform")}>
      <form className="space-y-3" onSubmit={submit}>
        <Field label={t("Setup token")}><input className="input" value={f.token} onChange={set("token")} required /></Field>
        <Field label={t("Instance name")}><input className="input" value={f.instanceName} onChange={set("instanceName")} /></Field>
        <Field label="Email"><input className="input" type="email" value={f.email} onChange={set("email")} required /></Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Username"><input className="input" value={f.username} onChange={set("username")} required pattern="[a-z0-9-]+" /></Field>
          <Field label={t("Display name")}><input className="input" value={f.displayName} onChange={set("displayName")} /></Field>
        </div>
        <Field label={t("Password (≥ 10 characters)")}><input className="input" type="password" value={f.password} onChange={set("password")} required minLength={10} /></Field>
        <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={f.allowSignup} onChange={set("allowSignup")} /> {t("Allow self sign-up")}</label>
        <ErrorBox error={err} />
        <button className="btn-primary w-full justify-center" disabled={busy}>{t("Finish setup")}</button>
      </form>
    </AuthShell>
  );
}

/** S-01-1 Sign in. */
export function LoginPage() {
  const qc = useQueryClient();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [show, setShow] = useState(false);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setErr(null); setBusy(true);
    try { await api.iam.login({ email: email.trim(), password }); await qc.invalidateQueries(); }
    catch (x) { setErr(errorText(x)); }
    finally { setBusy(false); }
  }
  return (
    <AuthShell title={t("Sign in")}>
      <form className="space-y-4" onSubmit={submit}>
        <label className="block">
          <span className="label">Email</span>
          <span className="relative block">
            <Mail className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fg-subtle" />
            <input className="input h-11 pl-10" type="email" autoComplete="username" placeholder="you@company.com" value={email}
              onChange={(e) => setEmail(e.target.value)} required autoFocus />
          </span>
        </label>
        <label className="block">
          <span className="label">{t("Password")}</span>
          <span className="relative block">
            <Lock className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fg-subtle" />
            <input className="input h-11 pl-10 pr-11" type={show ? "text" : "password"} autoComplete="current-password" value={password}
              onChange={(e) => setPassword(e.target.value)} required />
            <button type="button" className="absolute right-1.5 top-1/2 inline-flex h-8 w-8 -translate-y-1/2 items-center justify-center rounded-md text-fg-subtle hover:bg-surface-2 hover:text-fg"
              onClick={() => setShow(!show)} aria-label={show ? t("Hide password") : t("Show password")}>
              {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
          </span>
        </label>
        {err && <div className="flex items-start gap-2 rounded-lg border border-danger/30 bg-danger/10 px-3 py-2.5 text-sm text-danger"><CircleAlert className="mt-0.5 h-4 w-4 shrink-0" />{err}</div>}
        <button className="btn-primary h-11 w-full justify-center text-sm" disabled={busy}>
          {busy ? <><Loader2 className="h-4 w-4 animate-spin" />{t("Signing in…")}</> : <>{t("Sign in")}<ArrowRight className="h-4 w-4" /></>}
        </button>
      </form>
    </AuthShell>
  );
}

/** Invitation token from `/#/signup?invite=…` (the link in invitation emails) or `/signup?invite=…`. */
export function signupInvite(): string | null {
  const hash = window.location.hash.replace(/^#/, "");
  if (hash.startsWith("/signup")) return new URLSearchParams(hash.split("?")[1] ?? "").get("invite") ?? "";
  if (window.location.pathname === "/signup") return new URLSearchParams(window.location.search).get("invite") ?? "";
  return null;
}

/** S-01-1 Sign up with a workspace invitation. Registers, then signs in with the same credentials. */
export function SignupPage({ signedInAs }: { signedInAs?: string }) {
  const qc = useQueryClient();
  const [f, setF] = useState({ token: signupInvite() ?? "", email: "", username: "", displayName: "", password: "" });
  const [show, setShow] = useState(false);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: e.target.value });
  const leave = () => window.history.replaceState(null, "", "/");
  async function signOut() {
    setBusy(true);
    try { await api.iam.logout({}); await qc.invalidateQueries(); } catch (x) { setErr(errorText(x)); } finally { setBusy(false); }
  }
  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setErr(null); setBusy(true);
    try {
      const email = f.email.trim();
      await api.iam.register({ email, password: f.password, username: f.username.trim(), displayName: f.displayName.trim(), invitationToken: f.token.trim() });
      await api.iam.login({ email, password: f.password });
      window.location.replace("/");   // full reload: drops the invite from the URL and loads the new session
    } catch (x) { setErr(errorText(x)); setBusy(false); }
  }
  if (signedInAs) return (
    <AuthShell title={t("Accept invitation")} subtitle={t("You are signed in as {name}. Sign out to create the invited account.", { name: signedInAs })}>
      <div className="space-y-3">
        <ErrorBox error={err} />
        <button className="btn-primary h-11 w-full justify-center text-sm" disabled={busy} onClick={signOut}>{t("Sign out and continue")}</button>
        <button className="btn-ghost h-11 w-full justify-center text-sm" onClick={() => { leave(); window.location.reload(); }}>{t("Stay signed in")}</button>
      </div>
    </AuthShell>
  );
  return (
    <AuthShell title={t("Create your account")}>
      <form className="space-y-3" onSubmit={submit}>
        <Field label="Email"><input className="input" type="email" autoComplete="username" value={f.email} onChange={set("email")} required autoFocus /></Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Username"><input className="input" value={f.username} onChange={set("username")} required pattern="[a-z0-9-]+" /></Field>
          <Field label={t("Display name")}><input className="input" value={f.displayName} onChange={set("displayName")} /></Field>
        </div>
        <Field label={t("Password (≥ 10 characters)")}>
          <span className="relative block">
            <input className="input pr-11" type={show ? "text" : "password"} autoComplete="new-password" value={f.password} onChange={set("password")} required minLength={10} />
            <button type="button" className="absolute right-1.5 top-1/2 inline-flex h-7 w-7 -translate-y-1/2 items-center justify-center rounded-md text-fg-subtle hover:bg-surface-2 hover:text-fg"
              onClick={() => setShow(!show)} aria-label={show ? t("Hide password") : t("Show password")}>
              {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
          </span>
        </Field>
        <Field label={t("Invitation code")}><input className="input font-mono text-xs" value={f.token} onChange={set("token")} required /></Field>
        <ErrorBox error={err} />
        <button className="btn-primary h-11 w-full justify-center text-sm" disabled={busy}>
          {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <UserPlus className="h-4 w-4" />}{t("Create account")}
        </button>
      </form>
      <p className="mt-6 text-center text-xs text-fg-subtle">
        {t("Already have an account?")} <button className="text-primary hover:underline" onClick={() => { leave(); window.location.reload(); }}>{t("Sign in")}</button>
      </p>
    </AuthShell>
  );
}

const FEATURES: { icon: typeof Cpu; title: string }[] = [
  { icon: Cpu, title: "Your own GPUs" },
  { icon: ShieldCheck, title: "Automatic private network" },
  { icon: Layers, title: "The whole model lifecycle" },
];

/** Auth page frame: brand panel on the left (hidden on narrow screens), form on the right. */
function AuthShell({ title, subtitle, children }: { title: string; subtitle?: string; children: React.ReactNode }) {
  const tt = useT();
  const [theme, setTheme] = useState(() => document.documentElement.dataset.theme ?? "dark");
  const toggle = () => { const n = theme === "dark" ? "light" : "dark"; document.documentElement.dataset.theme = n; try { localStorage.setItem("mlp.theme", n); } catch { /* ignore */ } setTheme(n); };
  return (
    <div className="grid min-h-screen bg-bg lg:grid-cols-[1.05fr_1fr]">
      <aside className="relative hidden overflow-hidden border-r border-line lg:flex lg:flex-col lg:justify-between lg:p-12"
        style={{ background: "radial-gradient(1200px 600px at -10% -10%, color-mix(in srgb, var(--t-primary) 28%, transparent), transparent 60%), radial-gradient(900px 500px at 110% 110%, color-mix(in srgb, var(--t-accent) 22%, transparent), transparent 60%), var(--t-surface)" }}>
        <div className="pointer-events-none absolute inset-0 opacity-[0.07]" style={{ backgroundImage: "linear-gradient(var(--t-fg) 1px, transparent 1px), linear-gradient(90deg, var(--t-fg) 1px, transparent 1px)", backgroundSize: "44px 44px" }} />
        <div className="relative flex items-center gap-2.5">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-primary text-primary-fg"><Boxes className="h-5 w-5" /></span>
          <span className="text-lg font-semibold tracking-tight">ML<span className="text-primary">Platform</span></span>
        </div>
        <div className="relative max-w-md">
          <h2 className="text-3xl font-semibold leading-tight tracking-tight">{t("The ML platform that runs on your infrastructure")}</h2>
          <ul className="mt-8 space-y-5">
            {FEATURES.map((f) => (
              <li key={f.title} className="flex gap-3.5">
                <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-line bg-surface-2/80 text-primary"><f.icon className="h-[18px] w-[18px]" /></span>
                <span><span className="block text-sm font-medium">{t(f.title)}</span></span>
              </li>))}
          </ul>
        </div>
        <p className="relative text-xs text-fg-subtle">© {new Date().getFullYear()} MLPlatform</p>
      </aside>
      <main className="relative flex items-center justify-center px-6 py-12">
        <div className="absolute right-4 top-4 flex gap-1">
          <button className="inline-flex h-8 items-center gap-1.5 rounded-md px-2 text-xs font-medium text-fg-muted hover:bg-surface-2 hover:text-fg"
            onClick={() => setLang(tt.lang === "vi" ? "en" : "vi")}><Languages className="h-4 w-4" />{tt.lang === "vi" ? "VI" : "EN"}</button>
          <button className="inline-flex h-8 w-8 items-center justify-center rounded-md text-fg-muted hover:bg-surface-2 hover:text-fg" onClick={toggle} aria-label={t("Toggle theme")}>
            {theme === "dark" ? <Moon className="h-4 w-4" /> : <Sun className="h-4 w-4" />}</button>
        </div>
        <div className="w-full max-w-sm">
          <div className="mb-8 flex items-center gap-2.5 lg:hidden">
            <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-primary text-primary-fg"><Boxes className="h-5 w-5" /></span>
            <span className="text-lg font-semibold tracking-tight">ML<span className="text-primary">Platform</span></span>
          </div>
          <h1 className={`text-2xl font-semibold tracking-tight ${subtitle ? "" : "mb-7"}`}>{title}</h1>
          {subtitle && <p className="mb-7 mt-1.5 text-sm text-fg-muted">{subtitle}</p>}
          {children}
        </div>
      </main>
    </div>
  );
}
