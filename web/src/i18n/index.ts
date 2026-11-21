import { useSyncExternalStore } from "react";
import vi from "./vi.json";

/*
 * Bilingual UI (30/01 §1). English is the source language: every string in code is English and is the lookup key.
 * Vietnamese lives in vi.json ({ "English": "Tiếng Việt" }). A missing translation falls back to English.
 *
 * Keys may contain {placeholders}: t("{n} machines", { n: 3 }). The same templates also translate dynamic text that
 * arrives from the server (state reasons, events, errors): "Machine gpu-01 is offline" matches the key
 * "Machine {name} is {state}" and is rendered with the Vietnamese template, captured values translated recursively.
 */
export type Lang = "vi" | "en";
const VI = vi as Record<string, string>;

let lang: Lang = (() => { try { return localStorage.getItem("mlp.lang") === "en" ? "en" : "vi"; } catch { return "vi"; } })();
const subs = new Set<() => void>();
document.documentElement.lang = lang;

export function setLang(l: Lang) {
  lang = l;
  document.documentElement.lang = l;
  try { localStorage.setItem("mlp.lang", l); } catch { /* storage blocked — language still switches for this tab */ }
  subs.forEach((f) => f());
}
export const getLang = () => lang;

type Pattern = { re: RegExp; names: string[]; target: string; literal: number };
let patterns: Pattern[] | null = null;
const esc = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

/** Templates with {placeholders}, most specific (longest literal text) first. */
function compile(): Pattern[] {
  const out: Pattern[] = [];
  for (const [key, target] of Object.entries(VI)) {
    if (!key.includes("{")) continue;
    const names: string[] = [];
    const parts = key.split(/\{(\w+)\}/g);
    let src = "^";
    parts.forEach((p, i) => { if (i % 2 === 0) src += esc(p); else { names.push(p); src += "([\\s\\S]+?)"; } });
    if (names.length === 0) continue;
    const literal = parts.filter((_, i) => i % 2 === 0).join("").length;
    if (literal < 3) continue;   // "{a} {b}" would match everything
    out.push({ re: new RegExp(src + "$"), names, target, literal });
  }
  return out.sort((a, b) => b.literal - a.literal);
}

const cache = new Map<string, string>();

/* English mode: data stored before the code switched to English (old job reasons, events) is still Vietnamese — map it back. */
const VIET = /[\u00C0-\u1EF9]/;
let reverse: { exact: Map<string, string>; patterns: Pattern[] } | null = null;
function toEnglish(text: string): string {
  if (!reverse) {
    const exact = new Map<string, string>();
    const patterns: Pattern[] = [];
    for (const [en, viText] of Object.entries(VI)) {
      if (!viText.includes("{")) { if (!exact.has(viText)) exact.set(viText, en); continue; }
      const names: string[] = [];
      const parts = viText.split(/\{(\w+)\}/g);
      let src = "^";
      parts.forEach((p, i) => { if (i % 2 === 0) src += esc(p); else { names.push(p); src += "([\\s\\S]+?)"; } });
      const literal = parts.filter((_, i) => i % 2 === 0).join("").length;
      if (names.length && literal >= 3) patterns.push({ re: new RegExp(src + "$"), names, target: en, literal });
    }
    reverse = { exact, patterns: patterns.sort((a, b) => b.literal - a.literal) };
  }
  const hit = reverse.exact.get(text);
  if (hit !== undefined) return hit;
  for (const p of reverse.patterns) {
    const m = p.re.exec(text);
    if (!m) continue;
    let r = p.target;
    p.names.forEach((n, i) => { r = r.replaceAll(`{${n}}`, reverse!.exact.get(m[i + 1]) ?? m[i + 1]); });
    return r;
  }
  return text;
}

function toVietnamese(text: string, depth = 0): string {
  const exact = VI[text];
  if (exact !== undefined) return exact;
  if (depth > 2 || text.length > 2000) return text;
  const hit = cache.get(text);
  if (hit !== undefined) return hit;
  patterns ??= compile();
  let result = text;
  for (const p of patterns) {
    const m = p.re.exec(text);
    if (!m) continue;
    result = p.target;
    p.names.forEach((n, i) => { result = result.replaceAll(`{${n}}`, toVietnamese(m[i + 1], depth + 1)); });
    break;
  }
  if (cache.size > 5000) cache.clear();
  cache.set(text, result);
  return result;
}

export function translate(text: string | undefined | null, vars?: Record<string, string | number>): string {
  if (!text) return text ?? "";
  let s = lang === "vi" ? toVietnamese(text) : VIET.test(text) ? toEnglish(text) : text;
  if (vars) for (const [k, v] of Object.entries(vars)) s = s.replaceAll(`{${k}}`, String(v));
  return s;
}

/** Hook: the component re-renders when the language changes. */
export function useT() {
  const l = useSyncExternalStore((cb) => { subs.add(cb); return () => subs.delete(cb); }, () => lang);
  return Object.assign(translate, { lang: l, locale: l === "en" ? "en-US" : "vi-VN" });
}
