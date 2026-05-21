import axios, {
  type AxiosError,
  type AxiosInstance,
  type AxiosRequestConfig,
  type InternalAxiosRequestConfig,
} from "axios";
import { tokenStorage } from "@/lib/auth/storage";
import type { AuthTokenResponse } from "./types";

const baseURL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

/**
 * Axios singleton. Toda llamada al backend pasa por este cliente.
 * - Inyecta el access token en cada request si existe.
 * - Si una respuesta es 401, intenta refresh UNA vez y reintenta el request
 *   original. Si el refresh también falla, dispara onAuthExpired (configurable
 *   desde el AuthProvider).
 */
export const apiClient: AxiosInstance = axios.create({
  baseURL,
  withCredentials: false,
  headers: { "Content-Type": "application/json" },
  timeout: 30_000,
});

// --- Estado de refresh (cola para requests concurrentes mientras se rota) ---

type Pending = {
  resolve: (token: string) => void;
  reject: (err: unknown) => void;
};

let refreshing = false;
let queue: Pending[] = [];

function flushQueue(error: unknown, token: string | null) {
  for (const p of queue) {
    if (error || !token) p.reject(error);
    else p.resolve(token);
  }
  queue = [];
}

// --- Hook para que el AuthProvider observe expiración de sesión ---

type AuthExpiredHandler = () => void;
let onAuthExpired: AuthExpiredHandler | null = null;

export function setAuthExpiredHandler(handler: AuthExpiredHandler | null): void {
  onAuthExpired = handler;
}

// --- Interceptores ---

apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const access = tokenStorage.getAccessToken();
  if (access && config.headers) {
    config.headers.set("Authorization", `Bearer ${access}`);
  }
  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as
      | (InternalAxiosRequestConfig & { _retry?: boolean })
      | undefined;

    if (!original || error.response?.status !== 401 || original._retry) {
      return Promise.reject(error);
    }

    // Saltar el flujo de refresh para el propio endpoint /auth/*
    if (original.url?.startsWith("/api/auth/")) {
      return Promise.reject(error);
    }

    const refresh = tokenStorage.getRefreshToken();
    if (!refresh) {
      onAuthExpired?.();
      return Promise.reject(error);
    }

    original._retry = true;

    if (refreshing) {
      // Encolar y esperar a que termine el refresh en curso.
      return new Promise((resolve, reject) => {
        queue.push({
          resolve: (token) => {
            if (original.headers) {
              original.headers.set("Authorization", `Bearer ${token}`);
            }
            apiClient.request(original).then(resolve).catch(reject);
          },
          reject,
        });
      });
    }

    refreshing = true;
    try {
      const { data } = await apiClient.post<AuthTokenResponse>(
        "/api/auth/refresh",
        { refreshToken: refresh },
        { _retry: true } as AxiosRequestConfig
      );

      tokenStorage.setTokens(data.accessToken, data.refreshToken);
      flushQueue(null, data.accessToken);

      if (original.headers) {
        original.headers.set("Authorization", `Bearer ${data.accessToken}`);
      }
      return apiClient.request(original);
    } catch (refreshError) {
      flushQueue(refreshError, null);
      tokenStorage.clear();
      onAuthExpired?.();
      return Promise.reject(refreshError);
    } finally {
      refreshing = false;
    }
  }
);
