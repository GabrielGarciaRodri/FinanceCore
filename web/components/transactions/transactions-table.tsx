"use client";

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
import type {
  PagedTransactionsDto,
  TransactionListItemDto,
  TransactionStatus,
  TransactionType,
} from "@/lib/api/types";
import { formatDate, formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";

interface Props {
  data: PagedTransactionsDto | undefined;
  isLoading: boolean;
  isError: boolean;
  errorMessage?: string;
  pageSize: number;
  selectedId: string | null;
  onSelect: (id: string) => void;
}

export function TransactionsTable({
  data,
  isLoading,
  isError,
  errorMessage,
  pageSize,
  selectedId,
  onSelect,
}: Props): JSX.Element {
  return (
    <div className="rounded-md border bg-card">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="w-[110px]">Fecha</TableHead>
            <TableHead>Descripción</TableHead>
            <TableHead className="w-[120px]">Tipo</TableHead>
            <TableHead className="w-[120px]">Estado</TableHead>
            <TableHead className="w-[170px] text-right">Monto</TableHead>
            <TableHead className="w-[60px] text-center">Recon.</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading ? (
            Array.from({ length: Math.min(pageSize, 10) }).map((_, i) => (
              <SkeletonRow key={i} />
            ))
          ) : isError ? (
            <TableRow>
              <TableCell colSpan={6} className="h-24 text-center text-sm text-destructive">
                Error cargando transacciones{errorMessage ? `: ${errorMessage}` : ""}
              </TableCell>
            </TableRow>
          ) : !data || data.items.length === 0 ? (
            <TableRow>
              <TableCell colSpan={6} className="h-24 text-center text-sm text-muted-foreground">
                No hay transacciones para los filtros actuales.
              </TableCell>
            </TableRow>
          ) : (
            data.items.map((tx) => (
              <Row
                key={tx.id}
                tx={tx}
                selected={tx.id === selectedId}
                onSelect={onSelect}
              />
            ))
          )}
        </TableBody>
      </Table>
    </div>
  );
}

function Row({
  tx,
  selected,
  onSelect,
}: {
  tx: TransactionListItemDto;
  selected: boolean;
  onSelect: (id: string) => void;
}): JSX.Element {
  const positive = tx.amount > 0;
  const negative = tx.amount < 0;

  return (
    <TableRow
      data-state={selected ? "selected" : undefined}
      className="cursor-pointer"
      onClick={() => onSelect(tx.id)}
    >
      <TableCell className="text-sm tabular-nums text-muted-foreground">
        {formatDate(tx.valueDate)}
      </TableCell>
      <TableCell>
        <div className="text-sm">{tx.description ?? <span className="text-muted-foreground italic">sin descripción</span>}</div>
        <div className="text-xs text-muted-foreground">
          {tx.category ? `${tx.category} · ` : ""}
          {tx.externalId}
        </div>
      </TableCell>
      <TableCell>
        <Badge variant={typeBadgeVariant(tx.type as TransactionType)} className="font-normal">
          {tx.type}
        </Badge>
      </TableCell>
      <TableCell>
        <Badge variant={statusBadgeVariant(tx.status as TransactionStatus)} className="font-normal">
          {tx.status}
        </Badge>
      </TableCell>
      <TableCell
        className={cn(
          "text-right tabular-nums font-medium",
          positive && "text-emerald-600 dark:text-emerald-400",
          negative && "text-rose-600 dark:text-rose-400",
        )}
      >
        {formatMoney(tx.amount, tx.currencyCode)}
      </TableCell>
      <TableCell className="text-center">
        {tx.isReconciled ? (
          <CheckCircle2 className="mx-auto h-4 w-4 text-emerald-600 dark:text-emerald-400" />
        ) : (
          <span className="text-muted-foreground">—</span>
        )}
      </TableCell>
    </TableRow>
  );
}

function SkeletonRow(): JSX.Element {
  return (
    <TableRow>
      <TableCell><Skeleton className="h-4 w-20" /></TableCell>
      <TableCell>
        <Skeleton className="h-4 w-3/4" />
        <Skeleton className="mt-1 h-3 w-1/2" />
      </TableCell>
      <TableCell><Skeleton className="h-5 w-16" /></TableCell>
      <TableCell><Skeleton className="h-5 w-16" /></TableCell>
      <TableCell><Skeleton className="ml-auto h-4 w-24" /></TableCell>
      <TableCell><Skeleton className="mx-auto h-4 w-4" /></TableCell>
    </TableRow>
  );
}

function typeBadgeVariant(t: TransactionType): "default" | "secondary" | "outline" | "destructive" {
  switch (t) {
    case "Credit":
    case "TransferIn":
    case "Interest":
      return "secondary";
    case "Debit":
    case "TransferOut":
    case "Fee":
      return "outline";
    case "Adjustment":
      return "default";
    default:
      return "outline";
  }
}

function statusBadgeVariant(
  s: TransactionStatus,
): "default" | "secondary" | "outline" | "destructive" | "success" | "warning" {
  switch (s) {
    case "Posted":
    case "Reconciled":
      return "success";
    case "Validated":
      return "secondary";
    case "Pending":
    case "Processing":
      return "warning";
    case "Rejected":
    case "Reversed":
      return "destructive";
    default:
      return "outline";
  }
}
