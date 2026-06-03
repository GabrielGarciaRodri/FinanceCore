import { test, expect } from "@playwright/test";
import {
  buildStatementCsv,
  login,
  uniqueReconDate,
  uploadStatementViaApi,
} from "./helpers/api";

/**
 * Flow #3 — Reconciliación E2E.
 *
 * Estrategia (decidida con el dev): la reconciliación-con-discrepancias se
 * siembra vía API (statement sobre la cuenta seed + fecha única por run), y la
 * UI se ejerce para lo que aporta valor real: detalle → resolver discrepancia →
 * aprobar. Esto evita pelear con el date-picker para conseguir una fecha única
 * y mantiene el test determinista. El form de upload en sí queda cubierto por
 * el flow #2.
 *
 * Por qué genera discrepancias sin transacciones internas: el engine sólo
 * matchea internas en estado Posted/Reconciled; un statement sobre una fecha
 * fresca produce discrepancias `MissingInternal` directamente.
 */

test("detalle → resolver discrepancia → aprobar reconciliación", async ({ page, request }) => {
  // --- Precondición vía API ---
  const auth = await login(request);
  const date = uniqueReconDate(7); // distinta a la fecha del flow #2
  const result = await uploadStatementViaApi(request, auth.accessToken, {
    date,
    csv: buildStatementCsv(date),
  });

  expect(
    result.discrepancyCount,
    "el statement sembrado debe generar al menos una discrepancia",
  ).toBeGreaterThan(0);

  // --- Detalle ---
  await page.goto(`/reconciliations/${result.reconciliationId}`);
  await expect(page.getByRole("heading", { name: /Reconciliación/ })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Discrepancias" })).toBeVisible();

  // --- Resolver la primera discrepancia ---
  await page.getByRole("button", { name: "Resolver" }).first().click();

  const resolveDialog = page.getByRole("dialog");
  await expect(resolveDialog.getByText("Resolver discrepancia")).toBeVisible();
  // El tipo de resolución default (MatchedManually) marca la discrepancia como resuelta.
  await resolveDialog.getByRole("button", { name: "Resolver" }).click();

  await expect(page.getByText("Discrepancia resuelta")).toBeVisible();

  // --- Aprobar ---
  await page.getByRole("button", { name: "Aprobar" }).click();

  const approveDialog = page.getByRole("dialog");
  await expect(approveDialog.getByText("Aprobar reconciliación")).toBeVisible();
  await approveDialog.getByRole("button", { name: "Aprobar" }).click();

  await expect(page.getByText("Reconciliación aprobada")).toBeVisible();
  // Prueba terminal inequívoca: tras aprobar, el botón "Aprobar" queda deshabilitado.
  await expect(page.getByRole("button", { name: "Aprobar" })).toBeDisabled();
});
