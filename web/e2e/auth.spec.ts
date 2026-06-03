import { test, expect } from "@playwright/test";
import { ADMIN_CREDENTIALS } from "./helpers/api";

/**
 * Flow #1 — Auth (form real).
 *
 * Este spec corre en el project `unauthenticated` (sin storageState): es el
 * único que ejerce el formulario de login de verdad. El resto de la suite usa
 * el storageState que produce auth.setup.ts.
 */

test.describe("Autenticación", () => {
  test("login exitoso redirige al dashboard", async ({ page }) => {
    await page.goto("/login");

    await page.getByLabel("Email").fill(ADMIN_CREDENTIALS.email);
    await page.getByLabel("Contraseña").fill(ADMIN_CREDENTIALS.password);
    await page.getByRole("button", { name: "Ingresar" }).click();

    await expect(page).toHaveURL(/\/dashboard/);
  });

  test("credenciales inválidas muestran error", async ({ page }) => {
    await page.goto("/login");

    await page.getByLabel("Email").fill(ADMIN_CREDENTIALS.email);
    await page.getByLabel("Contraseña").fill("contraseña-incorrecta");
    await page.getByRole("button", { name: "Ingresar" }).click();

    // El backend responde 401 con title "Credenciales inválidas" → toast (sonner).
    await expect(page.getByText(/credenciales inválidas/i)).toBeVisible();
    // No debe haber navegado.
    await expect(page).toHaveURL(/\/login/);
  });

  test("ruta protegida sin sesión redirige a /login", async ({ page }) => {
    // Sin storageState (project unauthenticated) → el AppLayout debe expulsar.
    await page.goto("/reconciliations");
    await expect(page).toHaveURL(/\/login/);
  });
});
