"use client";

import { Activity, AlertTriangle, ClipboardCheck, Wallet } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { useCountUp } from "@/lib/hooks/use-count-up";
import type { DashboardQuickStatsDto } from "@/lib/api/types";

interface QuickStatsProps {
  stats: DashboardQuickStatsDto;
}

interface StatItem {
  label: string;
  value: number;
  icon: React.ComponentType<{ className?: string; style?: React.CSSProperties }>;
  /** Token HSL del theme (sin hsl()) que tinta chip e ícono. */
  token: string;
}

export function QuickStats({ stats }: QuickStatsProps): JSX.Element {
  const items: StatItem[] = [
    {
      label: "Cuentas activas",
      value: stats.activeAccounts,
      icon: Wallet,
      token: "--stat-accounts",
    },
    {
      label: "Transacciones hoy",
      value: stats.transactionsToday,
      icon: Activity,
      token: "--stat-transactions",
    },
    {
      label: "Discrepancias pendientes",
      value: stats.pendingDiscrepancies,
      icon: AlertTriangle,
      token: "--stat-discrepancies",
    },
    {
      label: "Reconciliaciones (7 días)",
      value: stats.reconciliationsLast7Days,
      icon: ClipboardCheck,
      token: "--stat-reconciliations",
    },
  ];

  return (
    <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
      {items.map((item) => (
        <StatCard key={item.label} item={item} />
      ))}
    </div>
  );
}

function StatCard({ item }: { item: StatItem }): JSX.Element {
  const Icon = item.icon;
  const animatedValue = useCountUp(item.value);

  return (
    <Card className="transition-all duration-200 motion-safe:hover:-translate-y-0.5 hover:shadow-md">
      <CardContent className="flex items-center gap-3 p-4">
        <div
          className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg"
          style={{ backgroundColor: `hsl(var(${item.token}) / 0.12)` }}
        >
          <Icon
            className="h-5 w-5"
            style={{ color: `hsl(var(${item.token}))` }}
          />
        </div>
        <div className="flex flex-col">
          <span className="text-xs text-muted-foreground">{item.label}</span>
          <span className="text-2xl font-semibold tabular-nums">
            {animatedValue.toLocaleString()}
          </span>
        </div>
      </CardContent>
    </Card>
  );
}
