"use client";

import { useEffect, useState } from "react";

/**
 * Devuelve `value` con un delay de `delayMs` ms desde el último cambio.
 * Útil para no spamear queries al backend mientras el usuario tipea.
 */
export function useDebouncedValue<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState<T>(value);

  useEffect(() => {
    const handle = window.setTimeout(() => setDebounced(value), delayMs);
    return () => window.clearTimeout(handle);
  }, [value, delayMs]);

  return debounced;
}
