import { apiClient } from "./client";
import type {
  AuthTokenResponse,
  AuthUser,
  LoginRequest,
  RefreshTokenRequest,
} from "./types";

export const authApi = {
  async login(payload: LoginRequest): Promise<AuthTokenResponse> {
    const { data } = await apiClient.post<AuthTokenResponse>(
      "/api/auth/login",
      payload
    );
    return data;
  },

  async refresh(payload: RefreshTokenRequest): Promise<AuthTokenResponse> {
    const { data } = await apiClient.post<AuthTokenResponse>(
      "/api/auth/refresh",
      payload
    );
    return data;
  },

  async logout(refreshToken: string): Promise<void> {
    await apiClient.post("/api/auth/logout", { refreshToken });
  },

  async me(): Promise<AuthUser> {
    const { data } = await apiClient.get<AuthUser>("/api/auth/me");
    return data;
  },
};
