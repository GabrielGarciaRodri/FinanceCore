"use client";

import { useCallback, useMemo } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import type {
  SearchTransactionsRequest,
  TransactionStatus,
  TransactionType,
} from "@/lib/api/types";

/**
 * Estado de filtros reflejado en la URL. Todas las propiedades son opcionales
 * salvo page y pageSize, que tienen defaults.
 */
export interface TransactionFilters {
  accountId?: string;
  startDate?: string;        // YYYY-MM-DD
  endDate?: string;
  type?: TransactionType;
  status?: TransactionStatus;
  minAmount?: number;
  maxAmount?: number;
  category?: string;
  searchText?: string;
  page: number;
  pageSize: number;
  sortBy: string;
  sortDescending: boolean;
}

const DEFAULTS = {
  page: 1,
  pageSize: 50,
  sortBy: "valuedate",
  sortDescending: true,
} as const;

/**
 * Claves que, al cambiar, deben resetear page a 1.
 */
const RESET_PAGE_KEYS = new Set<keyof TransactionFilters>([
  "accountId",
  "startDate",
  "endDate",
  "type",
  "status",
  "minAmount",
  "maxAmount",
  "category",
  "searchText",
  "pageSize",
  "sortBy",
  "sortDescending",
]);

function parseNumber(v: string | null): number | undefined {
  if (v === null || v === "") return undefined;
  const n = Number(v);
  return Number.isFinite(n) ? n : undefined;
}

function parseBool(v: string | null, fallback: boolean): boolean {
  if (v === null) return fallback;
  return v === "true" || v === "1";
}

function parseFromSearchParams(sp: URLSearchParams): TransactionFilters {
  return {
    accountId: sp.get("accountId") ?? undefined,
    startDate: sp.get("startDate") ?? undefined,
    endDate: sp.get("endDate") ?? undefined,
    type: (sp.get("type") as TransactionType | null) ?? undefined,
    status: (sp.get("status") as TransactionStatus | null) ?? undefined,
    minAmount: parseNumber(sp.get("minAmount")),
    maxAmount: parseNumber(sp.get("maxAmount")),
    category: sp.get("category") ?? undefined,
    searchText: sp.get("searchText") ?? undefined,
    page: parseNumber(sp.get("page")) ?? DEFAULTS.page,
    pageSize: parseNumber(sp.get("pageSize")) ?? DEFAULTS.pageSize,
    sortBy: sp.get("sortBy") ?? DEFAULTS.sortBy,
    sortDescending: parseBool(sp.get("sortDescending"), DEFAULTS.sortDescending),
  };
}

export interface UseTransactionFiltersResult {
  filters: TransactionFilters;
  /** Convierte a payload del backend (omite defaults innecesarios). */
  toRequest: () => SearchTransactionsRequest;
  /** Actualiza filtros (merge). Resetea page=1 salvo si sólo cambia "page". */
  setFilters: (patch: Partial<TransactionFilters>) => void;
  /** Setter ergonómico por clave individual. */
  setFilter: <K extends keyof TransactionFilters>(
    key: K,
    value: TransactionFilters[K] | undefined,
  ) => void;
  /** Limpia todo salvo paginación/orden. */
  clear: () => void;
}

export function useTransactionFilters(): UseTransactionFiltersResult {
  const router = useRouter();
  const pathname = usePathname();
  const sp = useSearchParams();

  const filters = useMemo(
    () => parseFromSearchParams(new URLSearchParams(sp?.toString() ?? "")),
    [sp],
  );

  const writeToUrl = useCallback(
    (next: TransactionFilters) => {
      const params = new URLSearchParams();

      const setIf = (key: string, value: string | undefined) => {
        if (value !== undefined && value !== "") params.set(key, value);
      };

      setIf("accountId", next.accountId);
      setIf("startDate", next.startDate);
      setIf("endDate", next.endDate);
      setIf("type", next.type);
      setIf("status", next.status);
      setIf("minAmount", next.minAmount?.toString());
      setIf("maxAmount", next.maxAmount?.toString());
      setIf("category", next.category);
      setIf("searchText", next.searchText);

      if (next.page !== DEFAULTS.page) params.set("page", String(next.page));
      if (next.pageSize !== DEFAULTS.pageSize) params.set("pageSize", String(next.pageSize));
      if (next.sortBy !== DEFAULTS.sortBy) params.set("sortBy", next.sortBy);
      if (next.sortDescending !== DEFAULTS.sortDescending) {
        params.set("sortDescending", String(next.sortDescending));
      }

      const qs = params.toString();
      router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
    },
    [router, pathname],
  );

  const setFilters = useCallback(
    (patch: Partial<TransactionFilters>) => {
      const next = { ...filters, ...patch };
      const onlyPageChanged =
        Object.keys(patch).length === 1 && "page" in patch;
      const touchesResetKey = Object.keys(patch).some((k) =>
        RESET_PAGE_KEYS.has(k as keyof TransactionFilters),
      );
      if (!onlyPageChanged && touchesResetKey) {
        next.page = 1;
      }
      writeToUrl(next);
    },
    [filters, writeToUrl],
  );

  const setFilter = useCallback(
    <K extends keyof TransactionFilters>(
      key: K,
      value: TransactionFilters[K] | undefined,
    ) => {
      setFilters({ [key]: value } as Partial<TransactionFilters>);
    },
    [setFilters],
  );

  const clear = useCallback(() => {
    writeToUrl({
      page: DEFAULTS.page,
      pageSize: filters.pageSize,
      sortBy: filters.sortBy,
      sortDescending: filters.sortDescending,
    });
  }, [writeToUrl, filters.pageSize, filters.sortBy, filters.sortDescending]);

  const toRequest = useCallback((): SearchTransactionsRequest => {
    return {
      accountId: filters.accountId,
      startDate: filters.startDate,
      endDate: filters.endDate,
      type: filters.type,
      status: filters.status,
      minAmount: filters.minAmount,
      maxAmount: filters.maxAmount,
      category: filters.category,
      searchText: filters.searchText,
      page: filters.page,
      pageSize: filters.pageSize,
      sortBy: filters.sortBy,
      sortDescending: filters.sortDescending,
    };
  }, [filters]);

  return { filters, toRequest, setFilters, setFilter, clear };
}
