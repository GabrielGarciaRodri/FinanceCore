"use client";

import { useQuery } from "@tanstack/react-query";
import { CalendarIcon, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Calendar } from "@/components/ui/calendar";
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
import type { ReconciliationStatus } from "@/lib/api/types";
import { fromIsoDate, toIsoDate } from "@/lib/format";
import {
  useReconciliationFilters,
  type ReconciliationFilters,
} from "@/lib/hooks/use-reconciliation-filters";

const STATUSES: ReconciliationStatus[] = [
  "Pending",
  "InProgress",
  "Completed",
  "CompletedWithDiscrepancies",
  "Failed",
];

const ALL_VALUE = "__all__";

function hasAnyFilter(f: ReconciliationFilters): boolean {
  return Boolean(f.accountId || f.startDate || f.endDate || f.status);
}

export function FiltersBar(): JSX.Element {
  const { filters, setFilter, clear } = useReconciliationFilters();

  const { data: accounts } = useQuery({
    queryKey: ["accounts", { activeOnly: true }],
    queryFn: () => accountsApi.list(false),
    staleTime: 5 * 60_000,
  });

  return (
    <div className="space-y-3 rounded-md border bg-card p-4">
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-4">
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

        <div className="space-y-1">
          <Label className="text-xs text-muted-foreground">Estado</Label>
          <Select
            value={filters.status ?? ALL_VALUE}
            onValueChange={(v) =>
              setFilter(
                "status",
                v === ALL_VALUE ? undefined : (v as ReconciliationStatus),
              )
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_VALUE}>Todos</SelectItem>
              {STATUSES.map((s) => (
                <SelectItem key={s} value={s}>
                  {s}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <DatePickerField
          label="Desde"
          value={filters.startDate}
          onChange={(v) => setFilter("startDate", v)}
        />

        <DatePickerField
          label="Hasta"
          value={filters.endDate}
          onChange={(v) => setFilter("endDate", v)}
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
