"use client";

import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { TooltipProps } from "recharts";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import type { ActivityPointDto } from "@/lib/api/types";

interface ActivityChartProps {
  data: ActivityPointDto[];
}

const DEBITS_COLOR = "hsl(var(--chart-debits))";
const CREDITS_COLOR = "hsl(var(--chart-credits))";

export function ActivityChart({ data }: ActivityChartProps): JSX.Element {
  if (data.length === 0) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Actividad reciente</CardTitle>
          <CardDescription>Últimos 30 días</CardDescription>
        </CardHeader>
        <CardContent className="flex h-[260px] items-center justify-center text-sm text-muted-foreground">
          No hay transacciones en el período.
        </CardContent>
      </Card>
    );
  }

  // Recharts trabaja mejor con números — formateamos la fecha como label corta.
  const chartData = data.map((d) => ({
    date: d.date.slice(5), // MM-DD
    fullDate: d.date,
    debits: Number(d.debits),
    credits: Number(d.credits),
    count: d.count,
  }));

  const totalDebits = data.reduce((acc, d) => acc + d.debits, 0);
  const totalCredits = data.reduce((acc, d) => acc + d.credits, 0);
  const totalCount = data.reduce((acc, d) => acc + d.count, 0);

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <CardTitle className="text-base">Actividad reciente</CardTitle>
            <CardDescription>
              Últimos {data.length} día{data.length === 1 ? "" : "s"} —{" "}
              {totalCount.toLocaleString()} transacciones
            </CardDescription>
          </div>
          {/* Los totales hacen de leyenda: dot de color + label + monto */}
          <div className="flex gap-4 text-xs">
            <LegendTotal label="Débitos" color={DEBITS_COLOR} value={totalDebits} />
            <LegendTotal label="Créditos" color={CREDITS_COLOR} value={totalCredits} />
          </div>
        </div>
      </CardHeader>
      <CardContent>
        <div className="h-[260px] w-full">
          <ResponsiveContainer width="100%" height="100%">
            <AreaChart data={chartData} margin={{ top: 8, right: 8, bottom: 0, left: 8 }}>
              <defs>
                <linearGradient id="fillDebits" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor={DEBITS_COLOR} stopOpacity={0.25} />
                  <stop offset="95%" stopColor={DEBITS_COLOR} stopOpacity={0.02} />
                </linearGradient>
                <linearGradient id="fillCredits" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor={CREDITS_COLOR} stopOpacity={0.25} />
                  <stop offset="95%" stopColor={CREDITS_COLOR} stopOpacity={0.02} />
                </linearGradient>
              </defs>
              <CartesianGrid
                vertical={false}
                stroke="hsl(var(--border))"
                strokeOpacity={0.6}
              />
              <XAxis
                dataKey="date"
                tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }}
                interval="preserveStartEnd"
                axisLine={false}
                tickLine={false}
                tickMargin={8}
              />
              <YAxis
                tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }}
                tickFormatter={(v) => abbreviate(Number(v))}
                axisLine={false}
                tickLine={false}
                width={44}
              />
              <Tooltip
                content={<ActivityTooltip />}
                cursor={{ stroke: "hsl(var(--border))", strokeWidth: 1 }}
              />
              <Area
                type="monotone"
                dataKey="credits"
                name="Créditos"
                stroke={CREDITS_COLOR}
                strokeWidth={2}
                fill="url(#fillCredits)"
                activeDot={{ r: 3, strokeWidth: 0 }}
              />
              <Area
                type="monotone"
                dataKey="debits"
                name="Débitos"
                stroke={DEBITS_COLOR}
                strokeWidth={2}
                fill="url(#fillDebits)"
                activeDot={{ r: 3, strokeWidth: 0 }}
              />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      </CardContent>
    </Card>
  );
}

function LegendTotal({
  label,
  color,
  value,
}: {
  label: string;
  color: string;
  value: number;
}): JSX.Element {
  return (
    <span className="flex items-center gap-1.5 text-muted-foreground">
      <span
        className="h-2 w-2 shrink-0 rounded-full"
        style={{ backgroundColor: color }}
        aria-hidden
      />
      {label}:{" "}
      <span className="font-medium text-foreground tabular-nums">
        {value.toLocaleString(undefined, { maximumFractionDigits: 0 })}
      </span>
    </span>
  );
}

function ActivityTooltip({
  active,
  payload,
}: TooltipProps<number, string>): JSX.Element | null {
  if (!active || !payload || payload.length === 0) return null;

  const point = payload[0]?.payload as {
    fullDate: string;
    count: number;
  };

  return (
    <div className="rounded-lg border bg-popover px-3 py-2 text-xs shadow-md">
      <p className="mb-1.5 font-medium text-popover-foreground">
        {formatFullDate(point.fullDate)}
      </p>
      <div className="space-y-1">
        {payload.map((entry) => (
          <div key={entry.dataKey} className="flex items-center justify-between gap-6">
            <span className="flex items-center gap-1.5 text-muted-foreground">
              <span
                className="h-2 w-2 shrink-0 rounded-full"
                style={{ backgroundColor: entry.stroke }}
                aria-hidden
              />
              {entry.name}
            </span>
            <span className="font-medium text-popover-foreground tabular-nums">
              {Number(entry.value).toLocaleString(undefined, {
                maximumFractionDigits: 2,
              })}
            </span>
          </div>
        ))}
        <div className="flex items-center justify-between gap-6 border-t pt-1 text-muted-foreground">
          <span>Transacciones</span>
          <span className="tabular-nums">{point.count.toLocaleString()}</span>
        </div>
      </div>
    </div>
  );
}

function formatFullDate(isoDate: string): string {
  const parsed = new Date(`${isoDate}T00:00:00`);
  if (Number.isNaN(parsed.getTime())) return isoDate;
  return parsed.toLocaleDateString("es", {
    weekday: "short",
    day: "numeric",
    month: "short",
  });
}

function abbreviate(value: number): string {
  const abs = Math.abs(value);
  if (abs >= 1_000_000) return `${(value / 1_000_000).toFixed(1)}M`;
  if (abs >= 1_000) return `${(value / 1_000).toFixed(1)}K`;
  return value.toFixed(0);
}
