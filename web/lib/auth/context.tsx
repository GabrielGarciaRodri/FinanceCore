"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { useRouter } from "next/navigation";
import { authApi } from "@/lib/api/auth";
import { setAuthExpiredHandler } from "@/lib/api/client";
import type { AuthUser, LoginRequest } from "@/lib/api/types";
import { tokenStorage } from "./storage";

interface AuthContextValue {
  user: AuthUser | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (payload: LoginRequest) => Promise<void>;
  logout: () => Promise<void>;
  hasRole: (role: string) => boolean;
  /** true si el usuario puede mutar: rol Admin/FinanceAdmin (espejo de la política "AdminOnly" del backend). */
  canWrite: boolean;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }): JSX.Element {
  const router = useRouter();
  const [user, setUser] = useState<AuthUser | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const initialized = useRef<boolean>(false);

  // Bootstrap inicial: si hay tokens, hidratar user desde localStorage y
  // validar contra /me en background. Si /me falla por 401, el interceptor
  // intentará refresh; si también falla, onAuthExpired limpia y redirige.
  useEffect(() => {
    if (initialized.current) return;
    initialized.current = true;

    const cached = tokenStorage.getUser<AuthUser>();
    if (cached) setUser(cached);

    const access = tokenStorage.getAccessToken();
    if (!access) {
      setIsLoading(false);
      return;
    }

    void authApi
      .me()
      .then((fresh) => {
        tokenStorage.setUser(fresh);
        setUser(fresh);
      })
      .catch(() => {
        // El interceptor ya disparó onAuthExpired si fue 401.
      })
      .finally(() => setIsLoading(false));
  }, []);

  // Hook global del interceptor: si el refresh falla, lo escuchamos acá.
  useEffect(() => {
    setAuthExpiredHandler(() => {
      tokenStorage.clear();
      setUser(null);
      router.replace("/login");
    });
    return () => setAuthExpiredHandler(null);
  }, [router]);

  const login = useCallback(
    async (payload: LoginRequest) => {
      const result = await authApi.login(payload);
      tokenStorage.setTokens(result.accessToken, result.refreshToken);
      if (result.user) {
        tokenStorage.setUser(result.user);
        setUser(result.user);
      }
      router.replace("/dashboard");
    },
    [router]
  );

  const logout = useCallback(async () => {
    const refresh = tokenStorage.getRefreshToken();
    try {
      if (refresh) await authApi.logout(refresh);
    } catch {
      // Logout best-effort: si el server rechaza, igual limpiamos local.
    } finally {
      tokenStorage.clear();
      setUser(null);
      router.replace("/login");
    }
  }, [router]);

  const hasRole = useCallback(
    (role: string): boolean => user?.roles?.includes(role) ?? false,
    [user]
  );

  // Capacidad de escritura = rol Admin/FinanceAdmin, espejo de la política
  // "AdminOnly" del backend. El rol Reader (usuario demo) queda en solo-lectura.
  const canWrite = useMemo<boolean>(
    () => (user?.roles ?? []).some((r) => r === "Admin" || r === "FinanceAdmin"),
    [user]
  );

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isAuthenticated: !!user,
      isLoading,
      login,
      logout,
      hasRole,
      canWrite,
    }),
    [user, isLoading, login, logout, hasRole, canWrite]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within an AuthProvider.");
  return ctx;
}
