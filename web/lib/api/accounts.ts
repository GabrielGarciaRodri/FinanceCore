import { apiClient } from "./client";
import type { AccountListItemDto } from "./types";

export const accountsApi = {
  async list(includeInactive = false): Promise<AccountListItemDto[]> {
    const { data } = await apiClient.get<AccountListItemDto[]>("/api/accounts", {
      params: { includeInactive },
    });
    return data;
  },
};
