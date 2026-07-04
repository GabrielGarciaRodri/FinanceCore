"use client";

import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
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
    date: d.date.slice(5),         // MM-DD
    Débitos: Number(d.debits),
    Créditos: Number(d.credits),
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
          <div className="flex gap-4 text-xs">
            <span className="text-muted-foreground">
              Débitos:{" "}
              <span className="font-medium text-foreground tabular-nums">
                {totalDebits.toLocaleString(undefined, { maximumFractionDigits: 0 })}
              </span>
            </span>
            <span className="text-muted-foreground">
              Créditos:{" "}
              <span className="font-medium text-foreground tabular-nums">
                {totalCredits.toLocaleString(undefined, { maximumFractionDigits: 0 })}
              </span>
            </span>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        <div className="h-[260px] w-full">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={chartData} margin={{ top: 8, right: 8, bottom: 8, left: 8 }}>
              <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
              <XAxis
                dataKey="date"
                tick={{ fontSize: 11 }}
                interval="preserveStartEnd"
                stroke="hsl(var(--muted-foreground))"
              />
              <YAxis
                tick={{ fontSize: 11 }}
                tickFormatter={(v) => abbreviate(Number(v))}
                stroke="hsl(var(--muted-foreground))"
              />
              <Tooltip
                contentStyle={{
                  backgroundColor: "hsl(var(--popover))",
                  border: "1px solid hsl(var(--border))",
                  borderRadius: 6,
                  fontSize: 12,
                  color: "hsl(var(--popover-foreground))",
                }}
                labelStyle={{ color: "hsl(var(--popover-foreground))" }}
                formatter={(value: number) =>
                  value.toLocaleString(undefined, { maximumFractionDigits: 2 })
                }
              />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="Débitos" stackId="a" fill="hsl(0 70% 60%)" radius={[0, 0, 0, 0]} />
              <Bar dataKey="Créditos" stackId="a" fill="hsl(142 70% 45%)" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </CardContent>
    </Card>
  );
}

function abbreviate(value: number): string {
  const abs = Math.abs(value);
  if (abs >= 1_000_000) return `${(value / 1_000_000).toFixed(1)}M`;
  if (abs >= 1_000) return `${(value / 1_000).toFixed(1)}K`;
  return value.toFixed(0);
}
