import { Banknote } from "lucide-react";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { formatCurrency } from "@/lib/utils";
import type { BalanceByCurrencyDto } from "@/lib/api/types";

interface BalanceCardsProps {
  balances: BalanceByCurrencyDto[];
}

export function BalanceCards({ balances }: BalanceCardsProps): JSX.Element {
  if (balances.length === 0) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Saldos por moneda</CardTitle>
          <CardDescription>Sin cuentas activas todavía.</CardDescription>
        </CardHeader>
        <CardContent className="flex items-center gap-3 text-sm text-muted-foreground">
          <Banknote className="h-5 w-5" />
          Cargá cuentas para ver totales agregados acá.
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
      {balances.map((b) => (
        <Card
          key={b.currencyCode}
          className="transition-all duration-200 motion-safe:hover:-translate-y-0.5 hover:shadow-md"
        >
          <CardHeader className="pb-2">
            <CardDescription className="flex items-center justify-between">
              <span>{b.currencyCode}</span>
              <span className="text-xs">
                {b.accountCount} cuenta{b.accountCount === 1 ? "" : "s"}
              </span>
            </CardDescription>
            <CardTitle className="text-3xl font-semibold tabular-nums">
              {formatCurrency(b.totalBalance, b.currencyCode)}
            </CardTitle>
          </CardHeader>
        </Card>
      ))}
    </div>
  );
}
