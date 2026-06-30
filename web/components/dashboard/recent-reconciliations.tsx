import Link from "next/link";
import { CheckCircle2, AlertCircle, Clock, XCircle, Loader2 } from "lucide-react";
import { Badge, type BadgeProps } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { cn, formatDate } from "@/lib/utils";
import { reconciliationStatusLabel } from "@/lib/i18n/labels";
import type {
  RecentReconciliationDto,
  ReconciliationStatus,
} from "@/lib/api/types";

interface RecentReconciliationsProps {
  items: RecentReconciliationDto[];
}

// La etiqueta en español sale del map central (lib/i18n/labels); acá solo
// vive lo visual específico de este componente: variante de badge + ícono.
const STATUS_META: Record<
  ReconciliationStatus,
  { variant: BadgeProps["variant"]; Icon: React.ComponentType<{ className?: string }> }
> = {
  Pending: { variant: "outline", Icon: Clock },
  InProgress: { variant: "secondary", Icon: Loader2 },
  Completed: { variant: "success", Icon: CheckCircle2 },
  CompletedWithDiscrepancies: { variant: "warning", Icon: AlertCircle },
  Failed: { variant: "destructive", Icon: XCircle },
};

export function RecentReconciliations({ items }: RecentReconciliationsProps): JSX.Element {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Reconciliaciones recientes</CardTitle>
        <CardDescription>
          {items.length === 0
            ? "Todavía no hay reconciliaciones."
            : `Últimas ${items.length}`}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-2">
        {items.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            Las reconciliaciones aparecen acá cuando el job diario corre o se
            disparan manualmente.
          </p>
        ) : (
          items.map((r) => {
            // El backend devuelve `status` como string crudo en RecentReconciliationDto
            // (ToString del enum), por eso casteamos contra el Record tipado.
            const status = r.status as ReconciliationStatus;
            const meta = STATUS_META[status] ?? STATUS_META.Pending;
            const Icon = meta.Icon;
            return (
              <Link
                key={r.id}
                href={`/reconciliations/${r.id}`}
                className="flex items-center justify-between rounded-md border p-3 transition-colors hover:bg-accent"
              >
                <div className="flex items-center gap-3">
                  <Icon
                    className={cn(
                      "h-4 w-4",
                      meta.variant === "success" && "text-emerald-600",
                      meta.variant === "warning" && "text-amber-600",
                      meta.variant === "destructive" && "text-destructive"
                    )}
                  />
                  <div className="flex flex-col">
                    <span className="text-sm font-medium">{formatDate(r.date)}</span>
                    <span className="font-mono text-[11px] text-muted-foreground">
                      {r.accountId.slice(0, 8)}…
                    </span>
                  </div>
                </div>

                <div className="flex items-center gap-3">
                  {r.discrepancyCount > 0 && (
                    <span className="text-xs text-muted-foreground">
                      {r.discrepancyCount} disc.
                    </span>
                  )}
                  <Badge variant={meta.variant}>{reconciliationStatusLabel(status)}</Badge>
                  {r.approved && (
                    <Badge variant="outline" className="text-[10px]">
                      Aprobada
                    </Badge>
                  )}
                </div>
              </Link>
            );
          })
        )}
      </CardContent>
    </Card>
  );
}
