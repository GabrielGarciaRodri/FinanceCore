"use client";

import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { CalendarIcon, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Calendar } from "@/components/ui/calendar";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { accountsApi } from "@/lib/api/accounts";
import type {
  TransactionStatus,
  TransactionType,
} from "@/lib/api/types";
import { fromIsoDate, toIsoDate } from "@/lib/format";
import { useDebouncedValue } from "@/lib/hooks/use-debounced-value";
import {
  useTransactionFilters,
  type TransactionFilters,
} from "@/lib/hooks/use-transaction-filters";

const TRANSACTION_TYPES: TransactionType[] = [
  "Debit",
  "Credit",
  "TransferOut",
  "TransferIn",
  "Fee",
  "Interest",
  "Adjustment",
];

const TRANSACTION_STATUSES: TransactionStatus[] = [
  "Pending",
  "Processing",
  "Validated",
  "Posted",
  "Reconciled",
  "Rejected",
  "Reversed",
];

const ALL_VALUE = "__all__";

function hasAnyFilter(f: TransactionFilters): boolean {
  return Boolean(
    f.accountId ||
      f.startDate ||
      f.endDate ||
      f.type ||
      f.status ||
      f.minAmount !== undefined ||
      f.maxAmount !== undefined ||
      f.searchText,
  );
}

export function FiltersBar(): JSX.Element {
  const { filters, setFilter, clear } = useTransactionFilters();

  const { data: accounts } = useQuery({
    queryKey: ["accounts", { activeOnly: true }],
    queryFn: () => accountsApi.list(false),
    staleTime: 5 * 60_000,
  });

  // searchText: input controlado local + debounce de 300ms al URL.
  const [searchInput, setSearchInput] = useState(filters.searchText ?? "");
  const debouncedSearch = useDebouncedValue(searchInput, 300);

  useEffect(() => {
    // Evita escribir el mismo valor; sólo cambia URL si difiere.
    const current = filters.searchText ?? "";
    const next = debouncedSearch || undefined;
    if ((next ?? "") !== current) {
      setFilter("searchText", next);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debouncedSearch]);

  // Si el filtro de searchText cambia desde otra parte (e.g. clear), re-sync.
  useEffect(() => {
    setSearchInput(filters.searchText ?? "");
  }, [filters.searchText]);

  return (
    <div className="space-y-3 rounded-md border bg-card p-4">
      <div className="grid grid-cols-1 gap-3 md:grid-cols-3 xl:grid-cols-6">
        {/* Cuenta */}
        <div className="space-y-1">
          <Label className="text-xs text-muted-foreground">Cuenta</Label>
          <Select
            value={filters.accountId ?? ALL_VALUE}
            onValueChange={(v) =>
              setFilter("accountId", v === ALL_VALUE ? undefined : v)
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="Todas" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_VALUE}>Todas</SelectItem>
              {accounts?.map((a) => (
                <SelectItem key={a.id} value={a.id}>
                  {a.accountName} · {a.accountNumber} ({a.currencyCode})
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {/* Tipo */}
        <div className="space-y-1">
          <Label className="text-xs text-muted-foreground">Tipo</Label>
          <Select
            value={filters.type ?? ALL_VALUE}
            onValueChange={(v) =>
              setFilter(
                "type",
                v === ALL_VALUE ? undefined : (v as TransactionType),
              )
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_VALUE}>Todos</SelectItem>
              {TRANSACTION_TYPES.map((t) => (
                <SelectItem key={t} value={t}>
                  {t}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {/* Estado */}
        <div className="space-y-1">
          <Label className="text-xs text-muted-foreground">Estado</Label>
          <Select
            value={filters.status ?? ALL_VALUE}
            onValueChange={(v) =>
              setFilter(
                "status",
                v === ALL_VALUE ? undefined : (v as TransactionStatus),
              )
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_VALUE}>Todos</SelectItem>
              {TRANSACTION_STATUSES.map((s) => (
                <SelectItem key={s} value={s}>
                  {s}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {/* Desde */}
        <DatePickerField
          label="Desde"
          value={filters.startDate}
          onChange={(v) => setFilter("startDate", v)}
        />

        {/* Hasta */}
        <DatePickerField
          label="Hasta"
          value={filters.endDate}
          onChange={(v) => setFilter("endDate", v)}
        />

        {/* Search text */}
        <div className="space-y-1">
          <Label className="text-xs text-muted-foreground">Búsqueda</Label>
          <Input
            placeholder="Descripción, externalId…"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
          />
        </div>

        {/* Min amount */}
        <AmountField
          label="Monto mín."
          value={filters.minAmount}
          onCommit={(v) => setFilter("minAmount", v)}
        />

        {/* Max amount */}
        <AmountField
          label="Monto máx."
          value={filters.maxAmount}
          onCommit={(v) => setFilter("maxAmount", v)}
        />
      </div>

      {hasAnyFilter(filters) && (
        <div className="flex justify-end">
          <Button variant="ghost" size="sm" onClick={clear}>
            <X className="h-3.5 w-3.5" />
            Limpiar filtros
          </Button>
        </div>
      )}
    </div>
  );
}

function DatePickerField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string | undefined;
  onChange: (v: string | undefined) => void;
}): JSX.Element {
  const date = fromIsoDate(value);
  return (
    <div className="space-y-1">
      <Label className="text-xs text-muted-foreground">{label}</Label>
      <Popover>
        <PopoverTrigger asChild>
          <Button
            variant="outline"
            className="w-full justify-start text-left font-normal"
          >
            <CalendarIcon className="h-4 w-4" />
            {date
              ? new Intl.DateTimeFormat("es-AR", {
                  day: "2-digit",
                  month: "short",
                  year: "numeric",
                }).format(date)
              : <span className="text-muted-foreground">Seleccionar</span>}
            {value && (
              <button
                type="button"
                onClick={(e) => {
                  e.preventDefault();
                  e.stopPropagation();
                  onChange(undefined);
                }}
                className="ml-auto rounded-sm opacity-60 hover:opacity-100"
              >
                <X className="h-3 w-3" />
              </button>
            )}
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-auto p-0" align="start">
          <Calendar
            mode="single"
            selected={date}
            onSelect={(d) => onChange(d ? toIsoDate(d) : undefined)}
            autoFocus
          />
        </PopoverContent>
      </Popover>
    </div>
  );
}

function AmountField({
  label,
  value,
  onCommit,
}: {
  label: string;
  value: number | undefined;
  onCommit: (v: number | undefined) => void;
}): JSX.Element {
  const [local, setLocal] = useState<string>(value?.toString() ?? "");

  useEffect(() => {
    setLocal(value?.toString() ?? "");
  }, [value]);

  return (
    <div className="space-y-1">
      <Label className="text-xs text-muted-foreground">{label}</Label>
      <Input
        type="number"
        inputMode="decimal"
        placeholder="0.00"
        value={local}
        onChange={(e) => setLocal(e.target.value)}
        onBlur={() => {
          const trimmed = local.trim();
          if (trimmed === "") {
            onCommit(undefined);
            return;
          }
          const n = Number(trimmed);
          if (Number.isFinite(n)) onCommit(n);
        }}
      />
    </div>
  );
}
