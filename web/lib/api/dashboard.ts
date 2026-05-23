import { apiClient } from "./client";
import type { DashboardDto } from "./types";

export interface GetDashboardParams {
  activityDays?: number;
  recentReconciliations?: number;
}

export const dashboardApi = {
  async get(params: GetDashboardParams = {}): Promise<DashboardDto> {
    const { data } = await apiClient.get<DashboardDto>("/api/dashboard", {
      params: {
        activityDays: params.activityDays ?? 30,
        recentReconciliations: params.recentReconciliations ?? 5,
      },
    });
    return data;
  },
};
