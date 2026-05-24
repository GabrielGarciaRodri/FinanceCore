import { apiClient } from "./client";
import type {
  PagedTransactionsDto,
  SearchTransactionsRequest,
  TransactionDetailDto,
} from "./types";

/**
 * Limpia el request: quita undefined/null/"" para no ensuciar la URL.
 */
function cleanParams(params: SearchTransactionsRequest): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === "") continue;
    out[k] = v;
  }
  return out;
}

export const transactionsApi = {
  async search(params: SearchTransactionsRequest): Promise<PagedTransactionsDto> {
    const { data } = await apiClient.get<PagedTransactionsDto>(
      "/api/transactions/search",
      { params: cleanParams(params) },
    );
    return data;
  },

  async getById(id: string): Promise<TransactionDetailDto> {
    const { data } = await apiClient.get<TransactionDetailDto>(
      `/api/transactions/${id}`,
    );
    return data;
  },

  /**
   * Descarga export como Blob (necesario para incluir el Authorization header;
   * un <a download> sólo no manda headers).
   */
  async downloadExport(
    format: "csv" | "xlsx",
    params: SearchTransactionsRequest,
  ): Promise<{ blob: Blob; filename: string }> {
    const response = await apiClient.get<Blob>(
      `/api/transactions/export.${format}`,
      {
        params: cleanParams(params),
        responseType: "blob",
      },
    );

    const cd = response.headers["content-disposition"] as string | undefined;
    const filename =
      cd?.match(/filename=([^;]+)/i)?.[1]?.replace(/["']/g, "").trim() ??
      `transactions.${format}`;

    return { blob: response.data, filename };
  },
};
