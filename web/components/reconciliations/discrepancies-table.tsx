"use client";

import { useState } from "react";
import { CheckCircle2, Clock } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { ReconciliationDiscrepancyDto } from "@/lib/api/types";
import { formatDate, formatMoney } from "@/lib/format";
import { ResolveDiscrepancyDialog } from "./resolve-discrepancy-dialog";

interface Props {
  reconciliationId: string;
  discrepancies: ReconciliationDiscrepancyDto[];
  /** Si la reconciliación ya fue aprobada, deshabilita acciones. */
  locked: boolean;
}

export function DiscrepanciesTable({
  reconciliationId,
  discrepancies,
  locked,
}: Props): JSX.Element {
  const [resolving, setResolving] = useState<ReconciliationDiscrepancyDto | null>(null);

  if (discrepancies.length === 0) {
    return (
      <div className="rounded-md border bg-card p-8 text-center text-sm text-muted-foreground">
        Esta reconciliación no registra discrepancias.
      </div>
    );
  }

  return (
    <>
      <div className="rounded-md border bg-card">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-[170px]">Tipo</TableHead>
              <TableHead className="w-[110px]">Fecha int.</TableHead>
              <TableHead className="text-right w-[140px]">Monto int.</TableHead>
              <TableHead className="w-[110px]">Fecha ext.</TableHead>
              <TableHead className="text-right w-[140px]">Monto ext.</TableHead>
              <TableHead className="text-right w-[140px]">Diferencia</TableHead>
              <TableHead className="w-[140px]">Estado</TableHead>
              <TableHead className="w-[100px] text-right">Acción</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {discrepancies.map((d) => (
              <TableRow key={d.id}>
                <TableCell>
                  <Badge variant="outline" className="font-normal">
                    {d.discrepancyType}
                  </Badge>
                </TableCell>
                <TableCell className="text-xs tabular-nums text-muted-foreground">
                  {formatDate(d.internalDate)}
                </TableCell>
                <TableCell className="text-right tabular-nums text-sm">
                  {d.internalAmount !== null ? formatMoney(d.internalAmount, "") : "—"}
                </TableCell>
                <TableCell className="text-xs tabular-nums text-muted-foreground">
                  {formatDate(d.externalDate)}
                </TableCell>
                <TableCell className="text-right tabular-nums text-sm">
                  {d.externalAmount !== null ? formatMoney(d.externalAmount, "") : "—"}
                </TableCell>
                <TableCell
                  className={
                    d.differenceAmount && d.differenceAmount !== 0
                      ? "text-right tabular-nums font-medium text-rose-600 dark:text-rose-400"
                      : "text-right tabular-nums text-muted-foreground"
                  }
                >
                  {d.differenceAmount !== null
                    ? formatMoney(Math.abs(d.differenceAmount), "")
                    : "—"}
                </TableCell>
                <TableCell>
                  {d.isResolved ? (
                    <div className="flex items-center gap-1.5">
                      <CheckCircle2 className="h-3.5 w-3.5 text-emerald-600 dark:text-emerald-400" />
                      <span className="text-xs">{d.resolutionType ?? "Resuelta"}</span>
                    </div>
                  ) : d.resolutionType ? (
                    // Resolución no terminal (UnderInvestigation / Escalated):
                    // ya se actuó pero el caso queda abierto. Mostrar el tipo
                    // explícitamente para no confundir con "Pendiente" puro.
                    <div className="flex items-center gap-1.5">
                      <Clock className="h-3.5 w-3.5 text-amber-600 dark:text-amber-400" />
                      <span className="text-xs">{d.resolutionType}</span>
                    </div>
                  ) : (
                    <Badge variant="warning" className="font-normal">Pendiente</Badge>
                  )}
                </TableCell>
                <TableCell className="text-right">
                  {!d.isResolved && (
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={locked}
                      onClick={() => setResolving(d)}
                    >
                      Resolver
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      <ResolveDiscrepancyDialog
        reconciliationId={reconciliationId}
        discrepancy={resolving}
        onClose={() => setResolving(null)}
      />
    </>
  );
}
