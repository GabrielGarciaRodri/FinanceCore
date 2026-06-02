import { apiClient } from "./client";

// ---------- Transactions upload ----------

export interface UploadRowError {
  row: number;
  error: string;
}

export interface TransactionIngestionResult {
  batchId: string;
  totalReceived: number;
  processed: number;
  succeeded: number;
  failed: number;
  duplicates: number;
  duration: string;            // TimeSpan se serializa como "HH:mm:ss.fffffff"
  results: Array<{
    externalId: string;
    transactionId: string | null;
    success: boolean;
    errorMessage: string | null;
    isDuplicate: boolean;
  }>;
}

export interface UploadTransactionsResponse {
  fileName: string;
  totalRowsParsed: number;
  ingestion: TransactionIngestionResult | null;
  parseErrors: UploadRowError[];
}

// ---------- Statement upload ----------

export interface StatementUploadResponse {
  reconciliationId: string;
  linesParsed: number;
  matched: number;
  unmatchedInternal: number;
  unmatchedExternal: number;
  discrepancyAmount: number;
  discrepancyCount: number;
  status: string;
}

// ---------- API ----------

export type UploadProgressHandler = (loaded: number, total: number) => void;

export const uploadsApi = {
  async uploadTransactions(
    file: File,
    accountId: string | undefined,
    onProgress?: UploadProgressHandler,
  ): Promise<UploadTransactionsResponse> {
    const form = new FormData();
    form.append("file", file);

    const { data } = await apiClient.post<UploadTransactionsResponse>(
      "/api/transactions/upload",
      form,
      {
        headers: { "Content-Type": "multipart/form-data" },
        params: accountId ? { accountId } : undefined,
        onUploadProgress: (evt) => {
          if (onProgress && evt.total) onProgress(evt.loaded, evt.total);
        },
      },
    );
    return data;
  },

  async uploadStatement(
    file: File,
    accountId: string,
    date: string,             // YYYY-MM-DD
    onProgress?: UploadProgressHandler,
  ): Promise<StatementUploadResponse> {
    const form = new FormData();
    form.append("file", file);

    const { data } = await apiClient.post<StatementUploadResponse>(
      `/api/reconciliations/accounts/${accountId}/date/${date}/statement`,
      form,
      {
        headers: { "Content-Type": "multipart/form-data" },
        onUploadProgress: (evt) => {
          if (onProgress && evt.total) onProgress(evt.loaded, evt.total);
        },
      },
    );
    return data;
  },
};
