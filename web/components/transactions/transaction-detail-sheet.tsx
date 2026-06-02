"use client";

import { useQuery } from "@tanstack/react-query";
import { AlertCircle, Copy } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { Skeleton } from "@/components/ui/skeleton";
import { transactionsApi } from "@/lib/api/transactions";
import type { TransactionDetailDto } from "@/lib/api/types";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";

interface Props {
  transactionId: string | null;
  onClose: () => void;
}

export function TransactionDetailSheet({ transactionId, onClose }: Props): JSX.Element {
  const open = transactionId !== null;

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ["transaction", transactionId],
    queryFn: () => transactionsApi.getById(transactionId as string),
    enabled: open,
    staleTime: 30_000,
  });

  return (
    <Sheet open={open} onOpenChange={(o) => !o && onClose()}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>Detalle de transacción</SheetTitle>
          <SheetDescription>
            Toda la información asociada a esta transacción.
          </SheetDescription>
        </SheetHeader>

        <div className="mt-6">
          {isLoading ? (
            <DetailSkeleton />
          ) : isError ? (
            <ErrorBlock message={(error as Error)?.message ?? "Error desconocido"} />
          ) : data ? (
            <DetailBody tx={data} />
          ) : null}
        </div>
      </SheetContent>
    </Sheet>
  );
}

function DetailBody({ tx }: { tx: TransactionDetailDto }): JSX.Element {
  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <div className="flex items-center gap-2">
          <Badge variant="outline">{tx.type}</Badge>
          <Badge variant="outline">{tx.status}</Badge>
        </div>
        <div className="text-2xl font-semibold tabular-nums">
          {formatMoney(tx.amount, tx.currencyCode)}
        </div>
        {tx.description && (
          <p className="text-sm text-muted-foreground">{tx.description}</p>
        )}
      </div>

      <Separator />

      <Section title="Identificación">
        <KV label="ID" value={tx.id} copyable />
        <KV label="External ID" value={tx.externalId} copyable />
        <KV label="Cuenta" value={tx.accountId} copyable />
        {tx.category && <KV label="Categoría" value={tx.category} />}
      </Section>

      <Section title="Fechas">
        <KV label="Fecha de valor" value={formatDate(tx.valueDate)} />
        <KV label="Fecha contable" value={formatDate(tx.bookingDate)} />
        <KV label="Creada" value={formatDateTime(tx.createdAt)} />
        {tx.processedAt && (
          <KV label="Procesada" value={formatDateTime(tx.processedAt)} />
        )}
      </Section>

      {(tx.counterpartyName || tx.counterpartyAccount || tx.counterpartyBank) && (
        <Section title="Contraparte">
          {tx.counterpartyName && <KV label="Nombre" value={tx.counterpartyName} />}
          {tx.counterpartyAccount && (
            <KV label="Cuenta" value={tx.counterpartyAccount} />
          )}
          {tx.counterpartyBank && <KV label="Banco" value={tx.counterpartyBank} />}
        </Section>
      )}

      {tx.originalAmount != null && tx.originalCurrency != null && (
        <Section title="Conversión de moneda">
          <KV
            label="Monto original"
            value={formatMoney(tx.originalAmount, tx.originalCurrency)}
          />
          {tx.exchangeRateUsed != null && (
            <KV label="Tipo de cambio" value={tx.exchangeRateUsed.toString()} />
          )}
        </Section>
      )}

      {tx.reconciliationId && (
        <Section title="Reconciliación">
          <KV label="ID" value={tx.reconciliationId} copyable />
          {tx.reconciledAt && (
            <KV label="Reconciliada" value={formatDateTime(tx.reconciledAt)} />
          )}
        </Section>
      )}
    </div>
  );
}

function Section({
  title,
  children,
}: {
  title: string;
  children: React.ReactNode;
}): JSX.Element {
  return (
    <div className="space-y-3">
      <h3 className="text-sm font-medium text-muted-foreground">{title}</h3>
      <dl className="space-y-1.5">{children}</dl>
    </div>
  );
}

function KV({
  label,
  value,
  copyable,
}: {
  label: string;
  value: string;
  copyable?: boolean;
}): JSX.Element {
  const handleCopy = async () => {
    await navigator.clipboard.writeText(value);
    toast.success(`${label} copiado`);
  };

  return (
    <div className="grid grid-cols-[120px_1fr] items-center gap-2 text-sm">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="flex items-center gap-1 break-all font-mono text-xs">
        <span className="flex-1">{value}</span>
        {copyable && (
          <Button
            variant="ghost"
            size="icon"
            className="h-6 w-6 shrink-0"
            onClick={handleCopy}
            aria-label={`Copiar ${label}`}
          >
            <Copy className="h-3 w-3" />
          </Button>
        )}
      </dd>
    </div>
  );
}

function DetailSkeleton(): JSX.Element {
  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <div className="flex gap-2">
          <Skeleton className="h-5 w-16" />
          <Skeleton className="h-5 w-16" />
        </div>
        <Skeleton className="h-8 w-40" />
        <Skeleton className="h-4 w-full" />
      </div>
      {Array.from({ length: 3 }).map((_, i) => (
        <div key={i} className="space-y-2">
          <Skeleton className="h-4 w-24" />
          {Array.from({ length: 3 }).map((_, j) => (
            <Skeleton key={j} className="h-4 w-full" />
          ))}
        </div>
      ))}
    </div>
  );
}

function ErrorBlock({ message }: { message: string }): JSX.Element {
  return (
    <div className="flex flex-col items-center gap-2 rounded-md border border-destructive/30 bg-destructive/5 p-6 text-center text-sm">
      <AlertCircle className="h-5 w-5 text-destructive" />
      <p className="font-medium">No se pudo cargar la transacción</p>
      <p className="text-xs text-muted-foreground">{message}</p>
    </div>
  );
}
