"use client";

import { useState } from "react";
import { ChevronDown, Layers } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import type { ReconciliationMatchGroupDto } from "@/lib/api/types";
import { formatDate, formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";

interface Props {
  groups: ReconciliationMatchGroupDto[];
}

/**
 * Payouts de pasarela conciliados por matching N:1: cada fila es una
 * liquidación (línea de extracto) explicada como Σ ventas − comisión.
 */
export function MatchGroupsCard({ groups }: Props): JSX.Element | null {
  if (groups.length === 0) return null;

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex items-center gap-2">
          <Layers className="h-4 w-4 text-primary" aria-hidden />
          <CardTitle className="text-base">Payouts agrupados</CardTitle>
        </div>
        <CardDescription>
          Liquidaciones de pasarela conciliadas contra sus ventas, con la
          comisión explicada como transacción.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {groups.map((group) => (
          <GroupRow key={group.id} group={group} />
        ))}
      </CardContent>
    </Card>
  );
}

function GroupRow({ group }: { group: ReconciliationMatchGroupDto }): JSX.Element {
  const [open, setOpen] = useState(false);
  const items = group.items ?? [];
  const feePct = ((group.feePercent ?? 0) * 100).toFixed(2);

  return (
    <div className="rounded-md border">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        className="flex w-full flex-wrap items-center justify-between gap-x-4 gap-y-2 p-3 text-left transition-colors hover:bg-muted/50"
      >
        <div className="space-y-0.5">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-mono text-sm font-medium">
              {group.externalReference}
            </span>
            <Badge variant="secondary">
              {group.groupedCount} venta{group.groupedCount === 1 ? "" : "s"}
            </Badge>
          </div>
          <p className="text-xs text-muted-foreground">
            Abonado {formatDate(group.payoutDate)} · ventas del{" "}
            {formatDate(group.windowStart)} al {formatDate(group.windowEnd)}
          </p>
        </div>

        <div className="flex items-center gap-3">
          <div className="text-right">
            <div className="text-sm font-semibold tabular-nums">
              {formatMoney(group.payoutAmount, "")}
            </div>
            <div className="text-xs text-muted-foreground tabular-nums">
              Comisión {formatMoney(group.feeAmount, "")} ({feePct}%)
            </div>
          </div>
          <ChevronDown
            className={cn(
              "h-4 w-4 shrink-0 text-muted-foreground transition-transform",
              open && "rotate-180"
            )}
            aria-hidden
          />
        </div>
      </button>

      {open && (
        <div className="space-y-2 border-t p-3">
          <p className="text-xs text-muted-foreground tabular-nums">
            Σ ventas {formatMoney(group.groupedAmount, "")} − comisión{" "}
            {formatMoney(group.feeAmount, "")} = payout{" "}
            {formatMoney(group.payoutAmount, "")}
          </p>
          <ul className="grid gap-1 sm:grid-cols-2">
            {items.map((item) => (
              <li
                key={item.transactionId}
                className="flex items-center justify-between rounded-sm bg-muted/40 px-2 py-1 text-xs"
              >
                <span className="font-mono text-muted-foreground">
                  {(item.transactionId ?? "").slice(0, 8)}
                </span>
                <span className="font-medium tabular-nums">
                  {formatMoney(item.amount, "")}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
