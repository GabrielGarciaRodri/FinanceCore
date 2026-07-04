"use client";

import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { Wallet } from "lucide-react";
import { AccountsTable } from "@/components/accounts/accounts-table";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { accountsApi } from "@/lib/api/accounts";
import { formatMoney } from "@/lib/format";

/** Total agregado por moneda: la foto "multi-cuenta / multi-moneda". */
interface CurrencyTotal {
  currencyCode: string;
  total: number;
  accountCount: number;
}

export default function AccountsPage(): JSX.Element {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ["accounts", { includeInactive: true }],
    queryFn: () => accountsApi.list(true),
    staleTime: 60_000,
  });

  const totals = useMemo<CurrencyTotal[]>(() => {
    const map = new Map<string, CurrencyTotal>();
    for (const account of data ?? []) {
      if (!account.isActive) continue;
      const entry = map.get(account.currencyCode) ?? {
        currencyCode: account.currencyCode,
        total: 0,
        accountCount: 0,
      };
      entry.total += account.currentBalance;
      entry.accountCount += 1;
      map.set(account.currencyCode, entry);
    }
    return [...map.values()].sort((a, b) =>
      a.currencyCode.localeCompare(b.currencyCode)
    );
  }, [data]);

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">Cuentas</h1>
        <p className="text-sm text-muted-foreground">
          Balances por cuenta y totales agregados por moneda.
        </p>
      </header>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {isLoading &&
          Array.from({ length: 2 }).map((_, i) => (
            <Skeleton key={i} className="h-28 w-full" />
          ))}
        {totals.map((t) => (
          <Card key={t.currencyCode}>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium text-muted-foreground">
                Total en {t.currencyCode}
              </CardTitle>
              <Wallet className="h-4 w-4 text-muted-foreground" />
            </CardHeader>
            <CardContent>
              <div className="font-mono text-2xl font-semibold tabular-nums">
                {formatMoney(t.total, t.currencyCode)}
              </div>
              <p className="text-xs text-muted-foreground">
                {t.accountCount === 1
                  ? "1 cuenta activa"
                  : `${t.accountCount} cuentas activas`}
              </p>
            </CardContent>
          </Card>
        ))}
      </div>

      <AccountsTable
        accounts={data}
        isLoading={isLoading}
        isError={isError}
        errorMessage={(error as Error)?.message}
      />
    </div>
  );
}
