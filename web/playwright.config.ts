import { defineConfig, devices } from "@playwright/test";

/**
 * Suite E2E de FinanceCore (sub-sprint 1).
 *
 * Precondición externa: el stack vivo debe estar arriba ANTES de correr la suite.
 *   1. docker compose up -d          (Postgres + Redis)
 *   2. dotnet run (FinanceCore.API)  → http://localhost:5000
 * Playwright sólo levanta el front (`next dev`); el backend + DB son responsabilidad
 * del dev (ver web/README.md → sección E2E).
 *
 * Estrategia de datos: stack vivo + aislamiento por test vía namespacing
 * (externalIds con prefijo `e2e-{runId}-` y fecha de reconciliación única por run).
 * No hay teardown en v1 — la acumulación es inocua por el namespacing.
 */

const WEB_BASE_URL = process.env.E2E_BASE_URL ?? "http://localhost:3000";

export default defineConfig({
  testDir: "./e2e",
  // Estado de auth compartido entre specs autenticados (lo genera auth.setup.ts).
  // Lo dejamos fuera de testMatch para que no se ejecute como spec.
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  // 1 worker: los specs comparten la cuenta seed; serializar evita carreras de estado.
  workers: 1,
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  timeout: 30_000,
  expect: { timeout: 10_000 },

  use: {
    baseURL: WEB_BASE_URL,
    trace: "on-first-retry",
    screenshot: "only-on-failure",
    locale: "es-AR",
  },

  projects: [
    // 1. Genera el storageState autenticado (login programático).
    {
      name: "setup",
      testMatch: /auth\.setup\.ts/,
    },
    // 2. Specs que ejercen el form de login real → SIN sesión previa.
    {
      name: "unauthenticated",
      testMatch: /auth\.spec\.ts/,
      use: { ...devices["Desktop Chrome"] },
    },
    // 3. Specs autenticados → reutilizan el storageState del setup.
    {
      name: "authenticated",
      testIgnore: /auth\.spec\.ts/,
      testMatch: /.*\.spec\.ts/,
      dependencies: ["setup"],
      use: {
        ...devices["Desktop Chrome"],
        storageState: "e2e/.auth/user.json",
      },
    },
  ],

  webServer: {
    command: "npm run dev",
    url: WEB_BASE_URL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    stdout: "ignore",
    stderr: "pipe",
  },
});
