"use client";

import { useCallback, useMemo } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import type {
  ReconciliationStatus,
  SearchReconciliationsRequest,
} from "@/lib/api/types";

export interface ReconciliationFilters {
  accountId?: string;
  startDate?: string;        // YYYY-MM-DD
  endDate?: string;
  status?: ReconciliationStatus;
  page: number;
  pageSize: number;
}

const DEFAULTS = {
  page: 1,
  pageSize: 50,
} as const;

const RESET_PAGE_KEYS = new Set<keyof ReconciliationFilters>([
  "accountId",
  "startDate",
  "endDate",
  "status",
  "pageSize",
]);

function parseNumber(v: string | null): number | undefined {
  if (v === null || v === "") return undefined;
  const n = Number(v);
  return Number.isFinite(n) ? n : undefined;
}

function parseFromSearchParams(sp: URLSearchParams): ReconciliationFilters {
  return {
    accountId: sp.get("accountId") ?? undefined,
    startDate: sp.get("startDate") ?? undefined,
    endDate: sp.get("endDate") ?? undefined,
    status: (sp.get("status") as ReconciliationStatus | null) ?? undefined,
    page: parseNumber(sp.get("page")) ?? DEFAULTS.page,
    pageSize: parseNumber(sp.get("pageSize")) ?? DEFAULTS.pageSize,
  };
}

export interface UseReconciliationFiltersResult {
  filters: ReconciliationFilters;
  toRequest: () => SearchReconciliationsRequest;
  setFilters: (patch: Partial<ReconciliationFilters>) => void;
  setFilter: <K extends keyof ReconciliationFilters>(
    key: K,
    value: ReconciliationFilters[K] | undefined,
  ) => void;
  clear: () => void;
}

export function useReconciliationFilters(): UseReconciliationFiltersResult {
  const router = useRouter();
  const pathname = usePathname();
  const sp = useSearchParams();

  const filters = useMemo(
    () => parseFromSearchParams(new URLSearchParams(sp?.toString() ?? "")),
    [sp],
  );

  const writeToUrl = useCallback(
    (next: ReconciliationFilters) => {
      const params = new URLSearchParams();
      const setIf = (k: string, v: string | undefined) => {
        if (v !== undefined && v !== "") params.set(k, v);
      };

      setIf("accountId", next.accountId);
      setIf("startDate", next.startDate);
      setIf("endDate", next.endDate);
      setIf("status", next.status);

      if (next.page !== DEFAULTS.page) params.set("page", String(next.page));
      if (next.pageSize !== DEFAULTS.pageSize) {
        params.set("pageSize", String(next.pageSize));
      }

      const qs = params.toString();
      router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
    },
    [router, pathname],
  );

  const setFilters = useCallback(
    (patch: Partial<ReconciliationFilters>) => {
      const next = { ...filters, ...patch };
      const onlyPageChanged =
        Object.keys(patch).length === 1 && "page" in patch;
      const touchesResetKey = Object.keys(patch).some((k) =>
        RESET_PAGE_KEYS.has(k as keyof ReconciliationFilters),
      );
      if (!onlyPageChanged && touchesResetKey) {
        next.page = 1;
      }
      writeToUrl(next);
    },
    [filters, writeToUrl],
  );

  const setFilter = useCallback(
    <K extends keyof ReconciliationFilters>(
      key: K,
      value: ReconciliationFilters[K] | undefined,
    ) => {
      setFilters({ [key]: value } as Partial<ReconciliationFilters>);
    },
    [setFilters],
  );

  const clear = useCallback(() => {
    writeToUrl({
      page: DEFAULTS.page,
      pageSize: filters.pageSize,
    });
  }, [writeToUrl, filters.pageSize]);

  const toRequest = useCallback(
    (): SearchReconciliationsRequest => ({
      accountId: filters.accountId,
      startDate: filters.startDate,
      endDate: filters.endDate,
      status: filters.status,
      page: filters.page,
      pageSize: filters.pageSize,
    }),
    [filters],
  );

  return { filters, toRequest, setFilters, setFilter, clear };
}
