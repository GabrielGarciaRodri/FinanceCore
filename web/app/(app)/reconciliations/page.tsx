"use client";

import { useMemo } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { FiltersBar } from "@/components/reconciliations/filters-bar";
import { PaginationBar } from "@/components/reconciliations/pagination-bar";
import { ReconciliationsTable } from "@/components/reconciliations/reconciliations-table";
import { accountsApi } from "@/lib/api/accounts";
import { reconciliationsApi } from "@/lib/api/reconciliations";
import { useReconciliationFilters } from "@/lib/hooks/use-reconciliation-filters";

export default function ReconciliationsPage(): JSX.Element {
  const { filters, toRequest, setFilter } = useReconciliationFilters();
  const request = toRequest();

  const { data, isLoading, isError, error, isFetching } = useQuery({
    queryKey: ["reconciliations", request],
    queryFn: () => reconciliationsApi.search(request),
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  });

  // Cuentas para mapear accountId → label legible en la tabla.
  const { data: accounts } = useQuery({
    queryKey: ["accounts", { activeOnly: true }],
    queryFn: () => accountsApi.list(false),
    staleTime: 5 * 60_000,
  });

  const accountLabels = useMemo(() => {
    const map: Record<string, string> = {};
    for (const a of accounts ?? []) {
      map[a.id] = `${a.accountName} · ${a.accountNumber}`;
    }
    return map;
  }, [accounts]);

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Reconciliaciones</h1>
          <p className="text-sm text-muted-foreground">
            Lista de reconciliaciones con filtros y acceso al detalle.
            {isFetching && !isLoading && (
              <span className="ml-2 text-xs italic">actualizando…</span>
            )}
          </p>
        </div>
      </header>

      <FiltersBar />

      <ReconciliationsTable
        data={data}
        isLoading={isLoading}
        isError={isError}
        errorMessage={(error as Error)?.message}
        pageSize={filters.pageSize}
        accountLabels={accountLabels}
      />

      <PaginationBar
        data={data}
        page={filters.page}
        pageSize={filters.pageSize}
        onPageChange={(p) => setFilter("page", p)}
        onPageSizeChange={(s) => setFilter("pageSize", s)}
      />
    </div>
  );
}
