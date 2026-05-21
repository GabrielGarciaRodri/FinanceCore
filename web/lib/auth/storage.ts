/**
 * Abstracción de almacenamiento de tokens.
 *
 * Implementación actual: localStorage. Vulnerable a XSS pero pragmática para
 * arrancar. Migración futura recomendada: refresh token en httpOnly cookie
 * + access token en memoria. La interface no cambia si migramos.
 */

const ACCESS_KEY = "fc:access";
const REFRESH_KEY = "fc:refresh";
const USER_KEY = "fc:user";

const isBrowser = (): boolean => typeof window !== "undefined";

export const tokenStorage = {
  getAccessToken(): string | null {
    if (!isBrowser()) return null;
    return window.localStorage.getItem(ACCESS_KEY);
  },

  getRefreshToken(): string | null {
    if (!isBrowser()) return null;
    return window.localStorage.getItem(REFRESH_KEY);
  },

  setTokens(access: string, refresh: string): void {
    if (!isBrowser()) return;
    window.localStorage.setItem(ACCESS_KEY, access);
    window.localStorage.setItem(REFRESH_KEY, refresh);
  },

  getUser<T = unknown>(): T | null {
    if (!isBrowser()) return null;
    const raw = window.localStorage.getItem(USER_KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as T;
    } catch {
      return null;
    }
  },

  setUser(user: unknown): void {
    if (!isBrowser()) return;
    window.localStorage.setItem(USER_KEY, JSON.stringify(user));
  },

  clear(): void {
    if (!isBrowser()) return;
    window.localStorage.removeItem(ACCESS_KEY);
    window.localStorage.removeItem(REFRESH_KEY);
    window.localStorage.removeItem(USER_KEY);
  },
};
