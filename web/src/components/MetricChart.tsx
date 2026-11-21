import { useEffect, useMemo, useRef } from "react";
import * as echarts from "echarts/core";
import { LineChart } from "echarts/charts";
import { GridComponent, TooltipComponent, LegendComponent } from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";

echarts.use([LineChart, GridComponent, TooltipComponent, LegendComponent, CanvasRenderer]);

export interface Series { name: string; points: [number, number][]; color?: string }

/** Series colors, in order — the run list uses the same order for its compare dots. */
export const CHART_COLORS = ["#4f8cff", "#34d399", "#fbbf24", "#fb7185", "#a78bfa", "#38bdf8", "#f97316", "#94a3b8"];

/** Short, stable number text for metric values (0.3936, 12.5, 1.2e-5). */
export function metricText(v: number): string {
  if (!Number.isFinite(v)) return "—";
  const a = Math.abs(v);
  if (a === 0) return "0";
  if (a >= 1000) return v.toFixed(0);
  if (a >= 100) return v.toFixed(1);
  if (a >= 1) return v.toFixed(3);
  if (a >= 0.001) return v.toFixed(4);
  return v.toExponential(2);
}

/** Axis tick text: at most three significant digits, no trailing zeros. */
function axisText(v: number): string {
  if (!Number.isFinite(v)) return "";
  const a = Math.abs(v);
  if (a !== 0 && (a >= 1e5 || a < 1e-3)) return v.toExponential(1);
  return String(Number(v.toPrecision(3)));
}

/** Exponential moving average with bias correction (the smoothing TensorBoard uses); weight 0 = raw values. */
export function smooth(points: [number, number][], weight: number): [number, number][] {
  if (weight <= 0 || points.length < 3) return points;
  let last = 0;
  return points.map(([x, y], i) => {
    last = last * weight + (1 - weight) * y;
    return [x, last / (1 - Math.pow(weight, i + 1))];
  });
}

function themeColors() {
  const cs = getComputedStyle(document.documentElement);
  const v = (n: string) => cs.getPropertyValue(n).trim();
  return { fg: v("--t-fg"), muted: v("--t-fg-muted"), subtle: v("--t-fg-subtle"), line: v("--t-line"), overlay: v("--t-overlay") };
}

/**
 * Line chart by step (M09). Colors come from the theme tokens and follow dark/light switches; `smoothing` (0–0.99) draws the
 * smoothed curve on top of a faint raw one. `bare` renders without the card frame, for charts placed inside another card.
 */
export function MetricChart({ title, subtitle, series, height = 220, smoothing = 0, bare }:
  { title?: string; subtitle?: string; series: Series[]; height?: number; smoothing?: number; bare?: boolean }) {
  const ref = useRef<HTMLDivElement>(null);
  const chart = useRef<echarts.ECharts | null>(null);
  // Polling hands over a new array with the same content; redraw only when the data really changed.
  const signature = useMemo(() => series.map((s) => `${s.name}:${s.color ?? ""}:${s.points.length}:${s.points[s.points.length - 1]?.join(",") ?? ""}`).join("|"), [series]);

  useEffect(() => {
    if (!ref.current) return;
    const c = echarts.init(ref.current);
    chart.current = c;
    const ro = new ResizeObserver(() => c.resize());
    ro.observe(ref.current);
    return () => { ro.disconnect(); c.dispose(); chart.current = null; };
  }, []);

  useEffect(() => {
    const draw = () => {
      const c = chart.current;
      if (!c) return;
      const k = themeColors();
      const many = series.length > 1;
      c.setOption({
        animation: false,
        color: CHART_COLORS,
        textStyle: { fontFamily: "Inter, ui-sans-serif, system-ui, sans-serif" },
        tooltip: {
          trigger: "axis", backgroundColor: k.overlay, borderColor: k.line, textStyle: { color: k.fg, fontSize: 12 },
          axisPointer: { lineStyle: { color: k.subtle } },
          valueFormatter: (v: unknown) => (typeof v === "number" ? metricText(v) : String(v ?? "—")),
        },
        legend: many ? { bottom: 0, type: "scroll", icon: "roundRect", itemWidth: 12, itemHeight: 3, textStyle: { color: k.muted, fontSize: 11 }, pageTextStyle: { color: k.muted } } : undefined,
        grid: { left: 4, right: 14, top: 10, bottom: many ? 30 : 2, containLabel: true },
        xAxis: {
          type: "value", minInterval: 1, min: "dataMin", max: "dataMax",
          axisLine: { lineStyle: { color: k.line } }, axisTick: { show: false }, splitLine: { show: false },
          axisLabel: { color: k.subtle, fontSize: 10, hideOverlap: true },
        },
        yAxis: {
          type: "value", scale: true, axisLabel: { color: k.subtle, fontSize: 10, formatter: axisText },
          splitLine: { lineStyle: { color: k.line, type: "dashed" } },
        },
        series: series.flatMap((s, i) => {
          const color = s.color ?? CHART_COLORS[i % CHART_COLORS.length];
          const line = { type: "line" as const, showSymbol: s.points.length < 40, symbolSize: 5, sampling: "lttb" as const };
          if (smoothing <= 0 || s.points.length < 3) return [{ ...line, name: s.name, data: s.points, lineStyle: { width: 2, color }, itemStyle: { color } }];
          return [
            { ...line, name: s.name + " (raw)", data: s.points, showSymbol: false, silent: true, tooltip: { show: false }, lineStyle: { width: 1, color, opacity: 0.25 }, itemStyle: { color } },
            { ...line, name: s.name, data: smooth(s.points, smoothing), showSymbol: false, lineStyle: { width: 2, color }, itemStyle: { color } },
          ];
        }),
      }, true);
    };
    draw();
    // Theme switch only swaps CSS variables — redraw to pick up the new axis and text colors.
    const mo = new MutationObserver(draw);
    mo.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    return () => mo.disconnect();
  }, [signature, smoothing]); // eslint-disable-line react-hooks/exhaustive-deps

  const body = (
    <>
      {(title || subtitle) && (
        <div className="mb-1 flex items-baseline justify-between gap-3">
          {title && <div className="truncate text-sm font-medium" title={title}>{title}</div>}
          {subtitle && <div className="shrink-0 text-xs tabular-nums text-fg-muted">{subtitle}</div>}
        </div>)}
      <div ref={ref} style={{ height }} />
    </>
  );
  return bare ? <div>{body}</div> : <div className="card">{body}</div>;
}
