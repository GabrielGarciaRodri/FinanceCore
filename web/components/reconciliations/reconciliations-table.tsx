"use client";

import { useRouter } from "next/navigation";
import { CheckCircle2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { PagedReconciliationsDto } from "@/lib/api/reconciliations";
import type {
  ReconciliationStatus,
} from "@/lib/api/types";
import { formatDate, formatMoney } from "@/lib/format";

interface Props {
  data: PagedReconciliationsDto | undefined;
  isLoading: boolean;
  isError: boolean;
  errorMessage?: string;
  pageSize: number;
  /** Mapa accountId → label legible, opcional. */
  accountLabels?: Record<string, string>;
}

export function ReconciliationsTable({
  data,
  isLoading,
  isError,
  errorMessage,
  pageSize,
  accountLabels,
}: Props): JSX.Element {
  const router = useRouter();

  return (
    <div className="rounded-md border bg-card">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="w-[110px]">Fecha</TableHead>
            <TableHead>Cuenta</TableHead>
            <TableHead className="w-[180px]">Estado</TableHead>
            <TableHead className="w-[120px] text-right">Matched</TableHead>
            <TableHead className="w-[120px] text-right">Unmatched</TableHead>
            <TableHead className="w-[160px] text-right">Discrepancia</TableHead>
            <TableHead className="w-[60px] text-center">Aprob.</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading ? (
            Array.from({ length: Math.min(pageSize, 10) }).map((_, i) => (
              <SkeletonRow key={i} />
            ))
          ) : isError ? (
            <TableRow>
              <TableCell colSpan={7} className="h-24 text-center text-sm text-destructive">
                Error cargando reconciliaciones{errorMessage ? `: ${errorMessage}` : ""}
              </TableCell>
            </TableRow>
          ) : !data || data.items.length === 0 ? (
            <TableRow>
              <TableCell colSpan={7} className="h-24 text-center text-sm text-muted-foreground">
                No hay reconciliaciones para los filtros actuales.
              </TableCell>
            </TableRow>
          ) : (
            data.items.map((r) => {
              const totalUnmatched = r.unmatchedInternal + r.unmatchedExternal;
              const approved = Boolean(r.approvedBy);
              return (
                <TableRow
                  key={r.id}
                  className="cursor-pointer"
                  onClick={() => router.push(`/reconciliations/${r.id}`)}
                >
                  <TableCell className="text-sm tabular-nums text-muted-foreground">
                    {formatDate(r.reconciliationDate)}
                  </TableCell>
                  <TableCell className="text-sm">
                    {accountLabels?.[r.accountId] ?? (
                      <span className="font-mono text-xs text-muted-foreground">
                        {r.accountId.slice(0, 8)}…
                      </span>
                    )}
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant={statusBadgeVariant(r.status as ReconciliationStatus)}
                      className="font-normal"
                    >
                      {r.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right tabular-nums text-sm">
                    {r.matchedCount} / {r.totalInternalRecords}
                  </TableCell>
                  <TableCell className="text-right tabular-nums text-sm">
                    {totalUnmatched === 0 ? (
                      <span className="text-muted-foreground">0</span>
                    ) : (
                      <span className="text-amber-600 dark:text-amber-400">
                        {totalUnmatched}
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="text-right tabular-nums text-sm">
                    {r.discrepancyAmount === 0 ? (
                      <span className="text-muted-foreground">0,00</span>
                    ) : (
                      <span className="font-medium text-rose-600 dark:text-rose-400">
                        {formatMoney(Math.abs(r.discrepancyAmount), "")}
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="text-center">
                    {approved ? (
                      <CheckCircle2 className="mx-auto h-4 w-4 text-emerald-600 dark:text-emerald-400" />
                    ) : (
                      <span className="text-muted-foreground">—</span>
                    )}
                  </TableCell>
                </TableRow>
              );
            })
          )}
        </TableBody>
      </Table>
    </div>
  );
}

function SkeletonRow(): JSX.Element {
  return (
    <TableRow>
      <TableCell><Skeleton className="h-4 w-20" /></TableCell>
      <TableCell><Skeleton className="h-4 w-32" /></TableCell>
      <TableCell><Skeleton className="h-5 w-24" /></TableCell>
      <TableCell><Skeleton className="ml-auto h-4 w-16" /></TableCell>
      <TableCell><Skeleton className="ml-auto h-4 w-12" /></TableCell>
      <TableCell><Skeleton className="ml-auto h-4 w-20" /></TableCell>
      <TableCell><Skeleton className="mx-auto h-4 w-4" /></TableCell>
    </TableRow>
  );
}

function statusBadgeVariant(
  s: ReconciliationStatus,
): "default" | "secondary" | "outline" | "destructive" | "success" | "warning" {
  switch (s) {
    case "Completed":
      return "success";
    case "CompletedWithDiscrepancies":
      return "warning";
    case "InProgress":
      return "secondary";
    case "Pending":
      return "outline";
    case "Failed":
      return "destructive";
    default:
      return "outline";
  }
}
