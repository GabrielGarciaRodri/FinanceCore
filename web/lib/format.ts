/**
 * Convierte un Date a "YYYY-MM-DD" en zona local (no UTC).
 * Útil para mandar al backend DateOnly sin desfasar por timezone.
 */
export function toIsoDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

/**
 * Parsea "YYYY-MM-DD" a Date local (al inicio del día).
 */
export function fromIsoDate(iso: string | undefined | null): Date | undefined {
  if (!iso) return undefined;
  const [y, m, d] = iso.split("-").map(Number);
  if (!y || !m || !d) return undefined;
  return new Date(y, m - 1, d);
}

const dateFormatter = new Intl.DateTimeFormat("es-AR", {
  day: "2-digit",
  month: "short",
  year: "numeric",
});

export function formatDate(iso: string | undefined | null): string {
  const d = fromIsoDate(iso);
  return d ? dateFormatter.format(d) : "—";
}

export function formatDateTime(iso: string | undefined | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  return new Intl.DateTimeFormat("es-AR", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(d);
}

/**
 * Formatea un monto con su código de moneda. Usa el código ISO (e.g. "COP",
 * "USD") como suffix; muchas monedas latinoamericanas no tienen símbolo bonito
 * en Intl.NumberFormat, así que mostramos siempre el código.
 */
export function formatMoney(amount: number, currencyCode: string): string {
  const formatted = new Intl.NumberFormat("es-AR", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount);
  return `${formatted} ${currencyCode}`;
}
