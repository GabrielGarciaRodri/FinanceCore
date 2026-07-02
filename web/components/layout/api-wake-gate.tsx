"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Loader2 } from "lucide-react";
import { LogoMark } from "@/components/layout/logo-mark";
import { apiBaseUrl } from "@/lib/api/client";

const POLL_INTERVAL_MS = 3_000;
const ATTEMPT_TIMEOUT_MS = 8_000;
const SPLASH_DELAY_MS = 1_500;
const STALLED_AFTER_MS = 90_000;
// Un fallo más rápido que esto no es un cold start (esos mueren por timeout):
// es el cliente bloqueando el request (adblock → ERR_BLOCKED_BY_CLIENT, CORS,
// DNS). Tras varios seguidos el gate falla-abierto: es azúcar de UX, nunca
// puede ser un muro que deje la app colgada con la API sana.
const FAST_FAIL_MS = 1_500;
const MAX_FAST_FAILS = 3;
const MAX_WAIT_MS = 150_000;

// El free tier de Render duerme la API tras inactividad (~60s de arranque).
// Una vez despierta, no vuelve a dormirse durante la sesión SPA: recordarlo
// a nivel módulo evita re-mostrar el splash en remounts del árbol.
let apiAwake = false;

async function pingHealth(): Promise<boolean> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), ATTEMPT_TIMEOUT_MS);
  try {
    const res = await fetch(`${apiBaseUrl}/health`, {
      signal: controller.signal,
      cache: "no-store",
    });
    return res.ok;
  } catch {
    return false;
  } finally {
    clearTimeout(timeout);
  }
}

/**
 * Bloquea el árbol hasta que /health responda. El splash recién aparece tras
 * SPLASH_DELAY_MS para no parpadear cuando la API ya está caliente (dev/local).
 */
export function ApiWakeGate({ children }: { children: ReactNode }): JSX.Element {
  const [awake, setAwake] = useState<boolean>(apiAwake);
  const [showSplash, setShowSplash] = useState<boolean>(false);
  const [stalled, setStalled] = useState<boolean>(false);

  useEffect(() => {
    if (awake) return;
    let cancelled = false;

    const splashTimer = setTimeout(() => setShowSplash(true), SPLASH_DELAY_MS);
    const stalledTimer = setTimeout(() => setStalled(true), STALLED_AFTER_MS);

    async function poll(): Promise<void> {
      const startedAt = Date.now();
      let fastFails = 0;

      function open(reason?: string): void {
        if (cancelled) return;
        if (reason) {
          console.warn(`[ApiWakeGate] fail-open: ${reason}`);
        }
        apiAwake = true;
        setAwake(true);
      }

      while (!cancelled) {
        const attemptStart = Date.now();
        if (await pingHealth()) {
          open();
          return;
        }

        // Fallo rápido = el request nunca llegó a la API (adblock/CORS/DNS);
        // un cold start real muere por timeout (~ATTEMPT_TIMEOUT_MS).
        if (Date.now() - attemptStart < FAST_FAIL_MS) {
          fastFails += 1;
          if (fastFails >= MAX_FAST_FAILS) {
            open(
              "el ping a /health falla instantáneamente (¿adblock bloqueando " +
                "el dominio o CORS?); la app sigue y los errores reales los " +
                "muestran las llamadas normales"
            );
            return;
          }
        } else {
          fastFails = 0;
        }

        if (Date.now() - startedAt > MAX_WAIT_MS) {
          open("se agotó la espera máxima del cold start");
          return;
        }

        await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
      }
    }
    void poll();

    return () => {
      cancelled = true;
      clearTimeout(splashTimer);
      clearTimeout(stalledTimer);
    };
  }, [awake]);

  if (awake) return <>{children}</>;

  // Ventana de gracia: pantalla neutra mientras el primer ping está en vuelo.
  if (!showSplash) return <div className="min-h-screen" />;

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-4 bg-muted/30 p-4 text-center">
      <div className="flex items-center gap-2">
        <LogoMark className="h-8 w-8 text-primary" />
        <span className="text-lg font-semibold">FinanceCore</span>
      </div>
      <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      <div className="space-y-1">
        <p className="text-sm font-medium">La demo está despertando…</p>
        <p className="max-w-xs text-xs text-muted-foreground">
          El servidor gratuito entra en reposo tras un rato sin uso; arrancar
          toma ~1 minuto. Gracias por la paciencia.
        </p>
        {stalled && (
          <p className="max-w-xs pt-2 text-xs text-destructive">
            Esto está tardando más de lo normal. Si usás un bloqueador de
            anuncios, permití este dominio y el de la API; si no, el servicio
            puede estar caído — podés seguir esperando o recargar.
          </p>
        )}
      </div>
    </div>
  );
}
