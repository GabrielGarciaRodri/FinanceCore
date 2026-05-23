"use client";

import { useQuery } from "@tanstack/react-query";
import { AlertCircle, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ActivityChart } from "@/components/dashboard/activity-chart";
import { BalanceCards } from "@/components/dashboard/balance-cards";
import { QuickStats } from "@/components/dashboard/quick-stats";
import { RecentReconciliations } from "@/components/dashboard/recent-reconciliations";
import { dashboardApi } from "@/lib/api/dashboard";

export default function DashboardPage(): JSX.Element {
  const { data, isLoading, isError, error, refetch, isFetching } = useQuery({
    queryKey: ["dashboard"],
    queryFn: () => dashboardApi.get(),
    refetchInterval: 60_000,        // refresca cada minuto en background
  });

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Dashboard</h1>
          <p className="text-sm text-muted-foreground">
            Resumen general del sistema. Auto-refresh cada 60 segundos.
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={() => void refetch()}
          disabled={isFetching}
        >
          <RefreshCw className={isFetching ? "h-4 w-4 animate-spin" : "h-4 w-4"} />
          Actualizar
        </Button>
      </header>

      {isLoading ? (
        <DashboardSkeleton />
      ) : isError ? (
        <ErrorState
          message={(error as Error)?.message ?? "Error desconocido"}
          onRetry={() => void refetch()}
        />
      ) : data ? (
        <div className="space-y-6">
          <QuickStats stats={data.stats} />

          <section>
            <h2 className="mb-3 text-sm font-medium text-muted-foreground">
              Saldos por moneda
            </h2>
            <BalanceCards balances={data.balancesByCurrency} />
          </section>

          <div className="grid gap-6 lg:grid-cols-3">
            <div className="lg:col-span-2">
              <ActivityChart data={data.activity} />
            </div>
            <div>
              <RecentReconciliations items={data.recentReconciliations} />
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}

function DashboardSkeleton(): JSX.Element {
  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-20" />
        ))}
      </div>
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-28" />
        ))}
      </div>
      <div className="grid gap-6 lg:grid-cols-3">
        <Skeleton className="h-[320px] lg:col-span-2" />
        <Skeleton className="h-[320px]" />
      </div>
    </div>
  );
}

function ErrorState({
  message,
  onRetry,
}: {
  message: string;
  onRetry: () => void;
}): JSX.Element {
  return (
    <div className="flex flex-col items-center justify-center gap-3 rounded-md border border-destructive/30 bg-destructive/5 p-8 text-center">
      <AlertCircle className="h-6 w-6 text-destructive" />
      <div>
        <p className="text-sm font-medium">No se pudo cargar el dashboard</p>
        <p className="text-xs text-muted-foreground">{message}</p>
      </div>
      <Button variant="outline" size="sm" onClick={onRetry}>
        <RefreshCw className="h-4 w-4" />
        Reintentar
      </Button>
    </div>
  );
}
