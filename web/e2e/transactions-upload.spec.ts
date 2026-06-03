import { test, expect } from "@playwright/test";
import { RUN_ID, SEED_ACCOUNT_ID, e2eExternalId, uniqueReconDate } from "./helpers/api";

/**
 * Flow #2 — Upload de transacciones (UI).
 *
 * Ejerce el FileDropzone + el form de ingesta de transacciones. El CSV se
 * construye en memoria con externalIds namespaced (`e2e-{runId}-…`) y trae la
 * columna AccountId apuntando a la cuenta seed, de modo que no dependemos del
 * selector de cuenta (que no expone el id en su label).
 */

const ROW_COUNT = 3;

function buildTransactionsCsv(): string {
  const date = uniqueReconDate(); // <= hoy, válido para ValueDate
  const header = "ExternalId,AccountId,TransactionType,Amount,CurrencyCode,ValueDate,Description";
  const rows = [header];
  for (let i = 0; i < ROW_COUNT; i++) {
    rows.push(
      `${e2eExternalId(`tx-${i}`)},${SEED_ACCOUNT_ID},Credit,${(i + 1) * 1000},COP,${date},Ingreso E2E ${i}`,
    );
  }
  return rows.join("\n");
}

test("subir CSV de transacciones inserta las filas y muestra el resumen", async ({ page }) => {
  await page.goto("/upload");

  // Tab "Transacciones" es la activa por defecto.
  await expect(page.getByRole("heading", { name: "Cargar archivos" })).toBeVisible();

  // El input[type=file] del FileDropzone está oculto; setInputFiles igual funciona.
  await page.locator('input[type="file"]').first().setInputFiles({
    name: `e2e-transactions-${RUN_ID}.csv`,
    mimeType: "text/csv",
    buffer: Buffer.from(buildTransactionsCsv(), "utf-8"),
  });

  await page.getByRole("button", { name: "Subir", exact: true }).click();

  // Toast de éxito + bloque de resumen.
  await expect(page.getByText(/transacciones insertadas/i)).toBeVisible();
  await expect(page.getByText("Resultado del upload")).toBeVisible();

  // El stat "Insertadas" debe reflejar las filas subidas.
  const insertadas = page
    .locator("div", { has: page.getByText("Insertadas", { exact: true }) })
    .filter({ hasText: String(ROW_COUNT) });
  await expect(insertadas.first()).toBeVisible();
});
