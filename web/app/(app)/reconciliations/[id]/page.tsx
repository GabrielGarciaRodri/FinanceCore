"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { AlertCircle, ArrowLeft, CheckCircle2, Download, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { ApproveReconciliationDialog } from "@/components/reconciliations/approve-reconciliation-dialog";
import { DiscrepanciesTable } from "@/components/reconciliations/discrepancies-table";
import { ReadOnlyNotice } from "@/components/auth/read-only-notice";
import { useAuth } from "@/lib/auth/context";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";
import { accountsApi } from "@/lib/api/accounts";
import { reconciliationsApi } from "@/lib/api/reconciliations";
import type {
  ReconciliationDto,
  ReconciliationStatus,
} from "@/lib/api/types";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";
import { reconciliationStatusLabel } from "@/lib/i18n/labels";

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
      toast.success("Exportación lista");
    } catch (err) {
      toast.error(`Error al exportar: ${(err as Error).message}`);
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
  const { canWrite } = useAuth();
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
              {reconciliationStatusLabel(rec.status as ReconciliationStatus)}
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
            Exportar discrepancias CSV
          </Button>
          {canWrite && (
            <Button
              size="sm"
              onClick={onApproveClick}
              disabled={approved}
              title={approved ? "Ya aprobada" : "Aprobar reconciliación"}
            >
              <CheckCircle2 className="h-4 w-4" />
              Aprobar
            </Button>
          )}
        </div>
      </header>

      {!canWrite && <ReadOnlyNotice />}

      <section className="grid gap-4 motion-safe:animate-fade-in-up lg:grid-cols-2">
        <CoverageCard rec={rec} />
        <BalancesCard rec={rec} />
      </section>

      <section
        className="motion-safe:animate-fade-in-up"
        style={{ animationDelay: "80ms" }}
      >
        <div className="mb-3 flex items-center gap-2">
          <h2 className="text-sm font-medium text-muted-foreground">
            Discrepancias
          </h2>
          {rec.discrepancies.length > 0 && (
            <Badge variant={unresolved > 0 ? "warning" : "success"}>
              {unresolved > 0
                ? `${unresolved} sin resolver de ${rec.discrepancies.length}`
                : `${rec.discrepancies.length} resueltas`}
            </Badge>
          )}
        </div>
        <DiscrepanciesTable
          reconciliationId={rec.id}
          discrepancies={rec.discrepancies}
          locked={approved || !canWrite}
        />
      </section>

      <Separator />

      <section
        className="motion-safe:animate-fade-in-up"
        style={{ animationDelay: "160ms" }}
      >
        <dl className="grid gap-x-8 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-3">
          <MetaItem label="Procesada por" value={rec.processedBy} />
          {rec.startedAt && (
            <MetaItem label="Iniciada" value={formatDateTime(rec.startedAt)} />
          )}
          {rec.completedAt && (
            <MetaItem label="Completada" value={formatDateTime(rec.completedAt)} />
          )}
          {rec.durationMs != null && (
            <MetaItem label="Duración" value={`${(rec.durationMs / 1000).toFixed(2)}s`} />
          )}
          {rec.approvedBy && <MetaItem label="Aprobada por" value={rec.approvedBy} />}
          {rec.notes && <MetaItem label="Notas" value={rec.notes} />}
        </dl>
      </section>
    </div>
  );
}

const MATCHED_COLOR = "hsl(var(--chart-credits))";
const UNMATCHED_INTERNAL_COLOR = "hsl(var(--stat-discrepancies))";
const UNMATCHED_EXTERNAL_COLOR = "hsl(var(--chart-debits))";

function CoverageCard({ rec }: { rec: ReconciliationDto }): JSX.Element {
  const total = rec.totalInternalRecords;
  const pct = total > 0 ? (rec.matchedCount / total) * 100 : 0;

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex items-start justify-between gap-2">
          <div>
            <CardTitle className="text-base">Cobertura de matcheo</CardTitle>
            <CardDescription>
              {rec.totalInternalRecords.toLocaleString()} registro
              {rec.totalInternalRecords === 1 ? "" : "s"} interno
              {rec.totalInternalRecords === 1 ? "" : "s"} ·{" "}
              {rec.totalExternalRecords.toLocaleString()} del extracto
            </CardDescription>
          </div>
          <span className="text-2xl font-semibold tabular-nums">
            {pct.toLocaleString(undefined, { maximumFractionDigits: 0 })}%
          </span>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        <div
          className="flex h-2 w-full overflow-hidden rounded-full bg-muted"
          role="progressbar"
          aria-valuenow={Math.round(pct)}
          aria-valuemin={0}
          aria-valuemax={100}
        >
          <div
            className="h-full transition-all"
            style={{ width: `${pct}%`, backgroundColor: MATCHED_COLOR }}
          />
        </div>
        <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs">
          <CoverageLegend
            color={MATCHED_COLOR}
            label="Conciliados"
            value={rec.matchedCount}
          />
          <CoverageLegend
            color={UNMATCHED_INTERNAL_COLOR}
            label="Sin conciliar (interno)"
            value={rec.unmatchedInternal}
          />
          <CoverageLegend
            color={UNMATCHED_EXTERNAL_COLOR}
            label="Sin conciliar (extracto)"
            value={rec.unmatchedExternal}
          />
        </div>
      </CardContent>
    </Card>
  );
}

function CoverageLegend({
  color,
  label,
  value,
}: {
  color: string;
  label: string;
  value: number;
}): JSX.Element {
  return (
    <span className="flex items-center gap-1.5 text-muted-foreground">
      <span
        className="h-2 w-2 shrink-0 rounded-full"
        style={{ backgroundColor: color }}
        aria-hidden
      />
      {label}:{" "}
      <span className="font-medium text-foreground tabular-nums">
        {value.toLocaleString()}
      </span>
    </span>
  );
}

function BalancesCard({ rec }: { rec: ReconciliationDto }): JSX.Element {
  const balanced = rec.discrepancyAmount === 0;

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-base">Balances</CardTitle>
        <CardDescription>Total interno vs. extracto bancario</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="grid grid-cols-2 gap-4">
          <div>
            <div className="text-xs text-muted-foreground">Interno</div>
            <div className="text-xl font-semibold tabular-nums">
              {formatMoney(rec.totalInternalAmount, "")}
            </div>
          </div>
          <div>
            <div className="text-xs text-muted-foreground">Extracto</div>
            <div className="text-xl font-semibold tabular-nums">
              {formatMoney(rec.totalExternalAmount, "")}
            </div>
          </div>
        </div>
        <Separator />
        <div className="flex items-center justify-between text-sm">
          <span className="text-muted-foreground">Diferencia</span>
          {balanced ? (
            <span
              className="flex items-center gap-1.5 font-medium"
              style={{ color: MATCHED_COLOR }}
            >
              <CheckCircle2 className="h-4 w-4" />
              Sin diferencia
            </span>
          ) : (
            <span
              className="font-semibold tabular-nums"
              style={{ color: UNMATCHED_EXTERNAL_COLOR }}
            >
              {formatMoney(Math.abs(rec.discrepancyAmount), "")}
            </span>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

function MetaItem({ label, value }: { label: string; value: string }): JSX.Element {
  return (
    <div className="flex flex-col">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-foreground">{value}</dd>
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
