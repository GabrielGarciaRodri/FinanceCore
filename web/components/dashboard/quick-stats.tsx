import { Activity, AlertTriangle, ClipboardCheck, Wallet } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import type { DashboardQuickStatsDto } from "@/lib/api/types";

interface QuickStatsProps {
  stats: DashboardQuickStatsDto;
}

export function QuickStats({ stats }: QuickStatsProps): JSX.Element {
  const items = [
    {
      label: "Cuentas activas",
      value: stats.activeAccounts,
      icon: Wallet,
      tone: "text-blue-600",
    },
    {
      label: "Transacciones hoy",
      value: stats.transactionsToday,
      icon: Activity,
      tone: "text-emerald-600",
    },
    {
      label: "Discrepancias pendientes",
      value: stats.pendingDiscrepancies,
      icon: AlertTriangle,
      tone: stats.pendingDiscrepancies > 0 ? "text-amber-600" : "text-muted-foreground",
    },
    {
      label: "Reconciliaciones (7 días)",
      value: stats.reconciliationsLast7Days,
      icon: ClipboardCheck,
      tone: "text-violet-600",
    },
  ];

  return (
    <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
      {items.map((item) => {
        const Icon = item.icon;
        return (
          <Card key={item.label}>
            <CardContent className="flex items-center gap-3 p-4">
              <Icon className={cn("h-5 w-5", item.tone)} />
              <div className="flex flex-col">
                <span className="text-xs text-muted-foreground">{item.label}</span>
                <span className="text-2xl font-semibold tabular-nums">
                  {item.value.toLocaleString()}
                </span>
              </div>
            </CardContent>
          </Card>
        );
      })}
    </div>
  );
}
