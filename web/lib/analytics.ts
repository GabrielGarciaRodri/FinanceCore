/**
 * Eventos custom de Vercel Analytics. Los pageviews miden llegadas; esto mide
 * acciones (funnel del informe post-launch: visitantes → logins → exports).
 * Catálogo centralizado para que los nombres queden consistentes en el panel.
 * track() sólo emite en producción sobre Vercel; en dev loguea en consola.
 */
import { track } from "@vercel/analytics";

/** Login exitoso con el usuario demo — la conversión clave del funnel. */
export function trackDemoLogin(): void {
  track("demo_login");
}

/** Descarga de un export (transacciones o discrepancias de una conciliación). */
export function trackExportDownloaded(
  format: "csv" | "xlsx",
  page: "transactions" | "reconciliation_detail"
): void {
  track("export_downloaded", { format, page });
}

/** Llegada al detalle de una conciliación — profundidad de exploración. */
export function trackDiscrepancyViewed(discrepancies: number): void {
  track("discrepancy_viewed", { discrepancies });
}
