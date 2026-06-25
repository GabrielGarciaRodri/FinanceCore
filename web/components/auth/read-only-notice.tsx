import { Lock } from "lucide-react";

/**
 * Aviso de "modo solo lectura". Se muestra a usuarios sin permiso de escritura
 * (rol Reader — p.ej. la cuenta de demo pública) donde antes había acciones
 * mutantes, para explicar por qué no están disponibles.
 */
export function ReadOnlyNotice({ className }: { className?: string }): JSX.Element {
  return (
    <div
      className={`flex items-center gap-2 rounded-md border border-amber-300/50 bg-amber-50 px-3 py-2 text-xs text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-400 ${className ?? ""}`}
    >
      <Lock className="h-3.5 w-3.5 shrink-0" />
      <span>Modo solo lectura — las acciones de escritura están deshabilitadas para esta cuenta.</span>
    </div>
  );
}
