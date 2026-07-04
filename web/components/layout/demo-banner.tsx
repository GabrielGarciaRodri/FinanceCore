"use client";

import { demoMode } from "@/lib/demo";

/** Aviso persistente del entorno demo. No renderiza nada fuera de demo mode. */
export function DemoBanner(): JSX.Element | null {
  if (!demoMode) return null;

  return (
    <div className="border-b border-amber-300 bg-amber-50 px-4 py-1.5 text-center text-xs font-medium text-amber-900 dark:border-amber-400/30 dark:bg-amber-950/50 dark:text-amber-200">
      Demo pública — usuario de sólo lectura. Datos de ejemplo.
    </div>
  );
}
