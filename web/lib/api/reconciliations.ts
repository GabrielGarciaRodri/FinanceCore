import { apiClient } from "./client";
import type {
  ApproveReconciliationRequest,
  ReconciliationDto,
  ResolveDiscrepancyRequest,
  SearchReconciliationsRequest,
} from "./types";

function cleanParams(params: SearchReconciliationsRequest): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === "") continue;
    out[k] = v;
  }
  return out;
}

export const reconciliationsApi = {
  async search(params: SearchReconciliationsRequest): Promise<ReconciliationDto[]> {
    // El backend devuelve una lista plana (sin totalCount).
    const { data } = await apiClient.get<ReconciliationDto[]>("/api/reconciliations", {
      params: cleanParams(params),
    });
    return data;
  },

  async getById(id: string): Promise<ReconciliationDto> {
    const { data } = await apiClient.get<ReconciliationDto>(`/api/reconciliations/${id}`);
    return data;
  },

  async resolveDiscrepancy(
    reconciliationId: string,
    discrepancyId: string,
    body: ResolveDiscrepancyRequest,
  ): Promise<void> {
    await apiClient.post(
      `/api/reconciliations/${reconciliationId}/discrepancies/${discrepancyId}/resolve`,
      body,
    );
  },

  async approve(
    reconciliationId: string,
    body: ApproveReconciliationRequest,
  ): Promise<void> {
    await apiClient.post(`/api/reconciliations/${reconciliationId}/approve`, body);
  },

  async downloadDiscrepanciesCsv(
    reconciliationId: string,
  ): Promise<{ blob: Blob; filename: string }> {
    const response = await apiClient.get<Blob>(
      `/api/reconciliations/${reconciliationId}/discrepancies.csv`,
      { responseType: "blob" },
    );

    const cd = response.headers["content-disposition"] as string | undefined;
    const filename =
      cd?.match(/filename=([^;]+)/i)?.[1]?.replace(/["']/g, "").trim() ??
      `discrepancies-${reconciliationId}.csv`;

    return { blob: response.data, filename };
  },
};
