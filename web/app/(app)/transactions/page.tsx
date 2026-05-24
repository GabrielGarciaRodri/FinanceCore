"use client";

import { useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ExportMenu } from "@/components/transactions/export-menu";
import { FiltersBar } from "@/components/transactions/filters-bar";
import { PaginationBar } from "@/components/transactions/pagination-bar";
import { TransactionDetailSheet } from "@/components/transactions/transaction-detail-sheet";
import { TransactionsTable } from "@/components/transactions/transactions-table";
import { transactionsApi } from "@/lib/api/transactions";
import { useTransactionFilters } from "@/lib/hooks/use-transaction-filters";

export default function TransactionsPage(): JSX.Element {
  const { filters, toRequest, setFilter } = useTransactionFilters();
  const request = toRequest();

  const { data, isLoading, isError, error, isFetching } = useQuery({
    queryKey: ["transactions", request],
    queryFn: () => transactionsApi.search(request),
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  });

  const [selectedId, setSelectedId] = useState<string | null>(null);

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Transacciones</h1>
          <p className="text-sm text-muted-foreground">
            Búsqueda, filtros, detalle y export de transacciones.
            {isFetching && !isLoading && (
              <span className="ml-2 text-xs italic">actualizando…</span>
            )}
          </p>
        </div>
        <ExportMenu params={request} disabled={isLoading || isError} />
      </header>

      <FiltersBar />

      <TransactionsTable
        data={data}
        isLoading={isLoading}
        isError={isError}
        errorMessage={(error as Error)?.message}
        pageSize={filters.pageSize}
        selectedId={selectedId}
        onSelect={setSelectedId}
      />

      <PaginationBar
        data={data}
        page={filters.page}
        pageSize={filters.pageSize}
        onPageChange={(p) => setFilter("page", p)}
        onPageSizeChange={(s) => setFilter("pageSize", s)}
      />

      <TransactionDetailSheet
        transactionId={selectedId}
        onClose={() => setSelectedId(null)}
      />
    </div>
  );
}
