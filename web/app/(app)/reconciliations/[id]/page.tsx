"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { AlertCircle, ArrowLeft, CheckCircle2, Download, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { ApproveReconciliationDialog } from "@/components/reconciliations/approve-reconciliation-dialog";
import { DiscrepanciesTable } from "@/components/reconciliations/discrepancies-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";
import { accountsApi } from "@/lib/api/accounts";
import { reconciliationsApi } from "@/lib/api/reconciliations";
import type {
  ReconciliationDto,
  ReconciliationStatus,
} from "@/lib/api/types";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";

export default function ReconciliationDetailPage(): JSX.Element {
  const params = useParams<{ id: string }>();
  const id = params.id;

  const [approveOpen, setApproveOpen] = useState(false);
  const [exporting, setExporting] = useState(false);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ["reconciliation", id],
    queryFn: () => reconciliationsApi.getById(id),
    enabled: Boolean(id),
    staleTime: 15_000,
  });

  const { data: accounts } = useQuery({
    queryKey: ["accounts", { activeOnly: true }],
    queryFn: () => accountsApi.list(false),
    staleTime: 5 * 60_000,
  });

  const accountLabel = data
    ? accounts?.find((a) => a.id === data.accountId)?.accountName ?? data.accountId
    : "";

  const handleExportCsv = async () => {
    setExporting(true);
    try {
      const { blob, filename } = await reconciliationsApi.downloadDiscrepanciesCsv(id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 0);
      toast.success("Export listo");
    } catch (err) {
      toast.error(`Error en export: ${(err as Error).message}`);
    } finally {
      setExporting(false);
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <Button variant="ghost" size="sm" asChild>
          <Link href="/reconciliations">
            <ArrowLeft className="h-4 w-4" />
            Volver
          </Link>
        </Button>
      </div>

      {isLoading ? (
        <DetailSkeleton />
      ) : isError ? (
        <ErrorState message={(error as Error)?.message ?? "Error desconocido"} />
      ) : data ? (
        <DetailBody
          rec={data}
          accountLabel={accountLabel}
          onApproveClick={() => setApproveOpen(true)}
          onExportClick={() => void handleExportCsv()}
          exporting={exporting}
        />
      ) : null}

      {data && (
        <ApproveReconciliationDialog
          reconciliationId={id}
          open={approveOpen}
          onClose={() => setApproveOpen(false)}
          unresolvedCount={data.discrepancies.filter((d) => !d.isResolved).length}
        />
      )}
    </div>
  );
}

function DetailBody({
  rec,
  accountLabel,
  onApproveClick,
  onExportClick,
  exporting,
}: {
  rec: ReconciliationDto;
  accountLabel: string;
  onApproveClick: () => void;
  onExportClick: () => void;
  exporting: boolean;
}): JSX.Element {
  const approved = Boolean(rec.approvedBy);
  const unresolved = rec.discrepancies.filter((d) => !d.isResolved).length;

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-semibold tracking-tight">
              Reconciliación · {formatDate(rec.reconciliationDate)}
            </h1>
            <Badge variant={statusBadgeVariant(rec.status as ReconciliationStatus)}>
              {rec.status}
            </Badge>
            {approved && (
              <Badge variant="success" className="gap-1">
                <CheckCircle2 className="h-3 w-3" />
                Aprobada
              </Badge>
            )}
          </div>
          <p className="text-sm text-muted-foreground">{accountLabel}</p>
        </div>

        <div className="flex gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={onExportClick}
            disabled={exporting || rec.discrepancies.length === 0}
          >
            {exporting ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <Download className="h-4 w-4" />
            )}
            Export discrepancias CSV
          </Button>
          <Button
            size="sm"
            onClick={onApproveClick}
            disabled={approved}
            title={approved ? "Ya aprobada" : "Aprobar reconciliación"}
          >
            <CheckCircle2 className="h-4 w-4" />
            Aprobar
          </Button>
        </div>
      </header>

      <section className="grid grid-cols-2 gap-4 md:grid-cols-4">
        <Stat label="Registros internos" value={rec.totalInternalRecords.toString()} />
        <Stat label="Registros externos" value={rec.totalExternalRecords.toString()} />
        <Stat
          label="Matched"
          value={`${rec.matchedCount} / ${rec.totalInternalRecords}`}
        />
        <Stat
          label="Unmatched"
          value={(rec.unmatchedInternal + rec.unmatchedExternal).toString()}
          highlight={rec.unmatchedInternal + rec.unmatchedExternal > 0 ? "warn" : undefined}
        />
        <Stat label="Total interno" value={formatMoney(rec.totalInternalAmount, "")} />
        <Stat label="Total externo" value={formatMoney(rec.totalExternalAmount, "")} />
        <Stat
          label="Discrepancia"
          value={formatMoney(Math.abs(rec.discrepancyAmount), "")}
          highlight={rec.discrepancyAmount !== 0 ? "error" : undefined}
        />
        <Stat
          label="Sin resolver"
          value={unresolved.toString()}
          highlight={unresolved > 0 ? "warn" : undefined}
        />
      </section>

      <Separator />

      <section>
        <h2 className="mb-3 text-sm font-medium text-muted-foreground">
          Discrepancias
        </h2>
        <DiscrepanciesTable
          reconciliationId={rec.id}
          discrepancies={rec.discrepancies}
          locked={approved}
        />
      </section>

      <Separator />

      <section className="grid gap-4 md:grid-cols-2">
        <Stat label="Procesada por" value={rec.processedBy} />
        {rec.completedAt && (
          <Stat label="Completada" value={formatDateTime(rec.completedAt)} />
        )}
        {rec.startedAt && (
          <Stat label="Iniciada" value={formatDateTime(rec.startedAt)} />
        )}
        {rec.durationMs != null && (
          <Stat label="Duración" value={`${(rec.durationMs / 1000).toFixed(2)}s`} />
        )}
        {rec.approvedBy && <Stat label="Aprobada por" value={rec.approvedBy} />}
        {rec.notes && <Stat label="Notas" value={rec.notes} />}
      </section>
    </div>
  );
}

function Stat({
  label,
  value,
  highlight,
}: {
  label: string;
  value: string;
  highlight?: "warn" | "error";
}): JSX.Element {
  const color =
    highlight === "error"
      ? "text-rose-600 dark:text-rose-400"
      : highlight === "warn"
      ? "text-amber-600 dark:text-amber-400"
      : "";
  return (
    <div className="rounded-md border bg-card p-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className={`mt-1 text-lg font-semibold tabular-nums ${color}`}>
        {value}
      </div>
    </div>
  );
}

function DetailSkeleton(): JSX.Element {
  return (
    <div className="space-y-6">
      <Skeleton className="h-8 w-1/2" />
      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        {Array.from({ length: 8 }).map((_, i) => (
          <Skeleton key={i} className="h-20" />
        ))}
      </div>
      <Skeleton className="h-64 w-full" />
    </div>
  );
}

function ErrorState({ message }: { message: string }): JSX.Element {
  return (
    <div className="flex flex-col items-center gap-2 rounded-md border border-destructive/30 bg-destructive/5 p-8 text-center">
      <AlertCircle className="h-6 w-6 text-destructive" />
      <p className="text-sm font-medium">No se pudo cargar la reconciliación</p>
      <p className="text-xs text-muted-foreground">{message}</p>
    </div>
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
