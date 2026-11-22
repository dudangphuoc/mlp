import { useEffect, useRef } from "react";
import * as echarts from "echarts/core";
import { LineChart } from "echarts/charts";
import { GridComponent, TooltipComponent, LegendComponent, MarkAreaComponent } from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";
import type { GpuHistory } from "../../gen/mlplatform/v1/fleet_pb";
import { ts } from "../../lib/format";
import { translate as t } from "../../i18n";

echarts.use([LineChart, GridComponent, TooltipComponent, LegendComponent, MarkAreaComponent, CanvasRenderer]);

const COLORS = { util: "#4f8cff", vram: "#a78bfa", temp: "#fb7185", power: "#fbbf24" };

function themeColors() {
  const cs = getComputedStyle(document.documentElement);
  const v = (n: string) => cs.getPropertyValue(n).trim();
  return { fg: v("--t-fg"), muted: v("--t-fg-muted"), subtle: v("--t-fg-subtle"), line: v("--t-line"), overlay: v("--t-overlay") };
}

/** Utilisation and VRAM share (left axis, %), temperature (°C) and power (W) on the right, job runs as shaded bands. */
export function GpuHistoryChart({ h, height = 240 }: { h: GpuHistory; height?: number }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!ref.current) return;
    const c = echarts.init(ref.current);
    const ro = new ResizeObserver(() => c.resize());
    ro.observe(ref.current);
    const k = themeColors();
    const total = Number(h.memoryTotalBytes) || 1;
    const at = (p: GpuHistory["points"][number]) => ts(p.at)?.getTime() ?? 0;
    const line = (name: string, color: string, data: [number, number][], yAxisIndex = 0) =>
      ({ type: "line" as const, name, data, yAxisIndex, showSymbol: false, sampling: "lttb" as const, lineStyle: { width: 1.5, color }, itemStyle: { color } });
    const from = h.points.length ? at(h.points[0]) : 0;
    const bands = h.jobs.map((j) => [
      { name: j.jobName, xAxis: Math.max(from, ts(j.startedAt)?.getTime() ?? 0) },
      { xAxis: ts(j.finishedAt)?.getTime() ?? Date.now() },
    ]);
    c.setOption({
      animation: false,
      tooltip: { trigger: "axis", backgroundColor: k.overlay, borderColor: k.line, textStyle: { color: k.fg, fontSize: 12 } },
      legend: { bottom: 0, icon: "roundRect", itemWidth: 12, itemHeight: 3, textStyle: { color: k.muted, fontSize: 11 } },
      grid: { left: 4, right: 4, top: 10, bottom: 28, containLabel: true },
      xAxis: { type: "time", axisLine: { lineStyle: { color: k.line } }, axisLabel: { color: k.subtle, fontSize: 10, hideOverlap: true }, splitLine: { show: false } },
      yAxis: [
        { type: "value", min: 0, max: 100, axisLabel: { color: k.subtle, fontSize: 10, formatter: "{value}%" }, splitLine: { lineStyle: { color: k.line, type: "dashed" } } },
        { type: "value", min: 0, axisLabel: { color: k.subtle, fontSize: 10 }, splitLine: { show: false } },
      ],
      series: [
        { ...line(t("Utilisation"), COLORS.util, h.points.map((p) => [at(p), p.utilPercent])), markArea: { silent: true, itemStyle: { color: "rgba(79,140,255,0.06)" }, label: { show: false }, data: bands } },
        line("VRAM", COLORS.vram, h.points.map((p) => [at(p), (Number(p.memoryUsedBytes) / total) * 100])),
        line("°C", COLORS.temp, h.points.map((p) => [at(p), p.temperatureC]), 1),
        line("W", COLORS.power, h.points.map((p) => [at(p), p.powerW]), 1),
      ],
    }, true);
    return () => { ro.disconnect(); c.dispose(); };
  }, [h]);
  return <div ref={ref} style={{ height }} />;
}
