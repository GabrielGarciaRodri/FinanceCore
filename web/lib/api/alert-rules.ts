import { apiClient } from "./client";

export type AlertRuleType =
  | "MissingPayout"
  | "DiscrepancyThreshold"
  | "LowBalance";

export interface AlertRuleDto {
  id: string;
  name: string;
  type: AlertRuleType;
  accountId: string | null;
  sourceProfileId: string | null;
  thresholdAmount: number | null;
  thresholdPercent: number | null;
  lookbackDays: number | null;
  /** Flags como texto: "Email" | "Webhook" | "Email, Webhook". */
  channels: string;
  emailTo: string | null;
  cooldownHours: number;
  isEnabled: boolean;
  lastTriggeredAt: string | null;
}

export interface SaveAlertRuleRequest {
  name: string;
  /** Sólo se usa al crear; el tipo de una regla existente no cambia. */
  type?: AlertRuleType;
  accountId?: string | null;
  sourceProfileId?: string | null;
  thresholdAmount?: number | null;
  thresholdPercent?: number | null;
  lookbackDays?: number | null;
  channels: string;
  emailTo?: string | null;
  cooldownHours: number;
  isEnabled?: boolean;
}

export interface SourceProfileDto {
  id: string;
  sourceKey: string;
  displayName: string;
  isActive: boolean;
}

export const alertRulesApi = {
  async list(): Promise<AlertRuleDto[]> {
    const { data } = await apiClient.get<AlertRuleDto[]>("/api/alert-rules");
    return data;
  },

  async create(body: SaveAlertRuleRequest): Promise<AlertRuleDto> {
    const { data } = await apiClient.post<AlertRuleDto>("/api/alert-rules", body);
    return data;
  },

  async update(id: string, body: SaveAlertRuleRequest): Promise<AlertRuleDto> {
    const { data } = await apiClient.put<AlertRuleDto>(`/api/alert-rules/${id}`, body);
    return data;
  },

  async remove(id: string): Promise<void> {
    await apiClient.delete(`/api/alert-rules/${id}`);
  },
};

export const sourceProfilesApi = {
  async list(): Promise<SourceProfileDto[]> {
    const { data } = await apiClient.get<SourceProfileDto[]>(
      "/api/reconciliation-source-profiles",
    );
    return data;
  },
};
