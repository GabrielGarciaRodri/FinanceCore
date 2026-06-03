import { request, type APIRequestContext } from "@playwright/test";

/**
 * Helpers de API para la suite E2E.
 *
 * Todo lo que es "precondición de datos" (login programático, seeding de un
 * statement para el flow de reconciliación) pasa por acá vía HTTP directo al
 * backend, evitando manejar ese estado a través de la UI. La UI se ejerce sólo
 * para lo que el test realmente quiere validar.
 */

// ---------------------------------------------------------------------------
// Constantes del entorno (ver memory: project_financecore_setup)
// ---------------------------------------------------------------------------

export const API_BASE_URL = process.env.E2E_API_URL ?? "http://localhost:5000";

/** Cuenta seed (COP). Único origen de datos: no hay endpoint para crear cuentas. */
export const SEED_ACCOUNT_ID = "a1b2c3d4-0000-0000-0000-000000000001";

export const ADMIN_CREDENTIALS = {
  email: "admin@financecore.local",
  password: "ChangeMe!2026",
} as const;

// Claves de localStorage que usa el front (lib/auth/storage.ts).
export const STORAGE_KEYS = {
  access: "fc:access",
  refresh: "fc:refresh",
  user: "fc:user",
} as const;

// ---------------------------------------------------------------------------
// Tipos (subset de los DTOs del backend que nos interesan)
// ---------------------------------------------------------------------------

interface AuthUser {
  id: string;
  email: string;
  displayName: string | null;
  roles: string[];
}

interface AuthTokenResponse {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthUser | null;
}

export interface StatementUploadResult {
  reconciliationId: string;
  linesParsed: number;
  matched: number;
  unmatchedInternal: number;
  unmatchedExternal: number;
  discrepancyAmount: number;
  discrepancyCount: number;
  status: string;
}

// ---------------------------------------------------------------------------
// Namespacing / aislamiento por corrida
// ---------------------------------------------------------------------------

/**
 * Identificador único por corrida de la suite. Se usa como prefijo de
 * externalIds y como semilla de las fechas únicas, para que cada run sea
 * inocuo frente a corridas previas sobre la misma cuenta seed.
 */
export const RUN_ID = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;

/**
 * Devuelve una fecha YYYY-MM-DD única por (run, offset), retrocediendo desde hoy.
 * Retroceder evita la validación "ValueDate <= hoy + 5d" del backend y la
 * colisión con reconciliaciones ya creadas por el seeder demo (hoy/-7/-14/-21).
 */
export function uniqueReconDate(offsetDays = 0): string {
  // Un offset base grande (>30) para no pisar las fechas del seeder demo.
  const base = 40 + (hashRun() % 200);
  const d = new Date();
  d.setUTCDate(d.getUTCDate() - base - offsetDays);
  return d.toISOString().slice(0, 10);
}

function hashRun(): number {
  let h = 0;
  for (const ch of RUN_ID) h = (h * 31 + ch.charCodeAt(0)) | 0;
  return Math.abs(h);
}

/** ExternalId namespaced para que las filas de este run no choquen con otras. */
export function e2eExternalId(suffix: string): string {
  return `e2e-${RUN_ID}-${suffix}`;
}

// ---------------------------------------------------------------------------
// Login programático + storageState
// ---------------------------------------------------------------------------

/** Hace login contra el backend y devuelve los tokens + user. */
export async function login(
  ctx: APIRequestContext,
  credentials: { email: string; password: string } = ADMIN_CREDENTIALS,
): Promise<AuthTokenResponse> {
  const res = await ctx.post(`${API_BASE_URL}/api/auth/login`, {
    data: credentials,
  });
  if (!res.ok()) {
    throw new Error(
      `Login programático falló (${res.status()}). ¿Está el backend arriba en ${API_BASE_URL} y seedeado el admin?`,
    );
  }
  return (await res.json()) as AuthTokenResponse;
}

/**
 * Forma del storageState (localStorage) que espera el front para considerarse
 * autenticado. `origin` debe ser el origin del web app (NO el del backend).
 */
export function buildStorageState(origin: string, auth: AuthTokenResponse) {
  return {
    cookies: [],
    origins: [
      {
        origin,
        localStorage: [
          { name: STORAGE_KEYS.access, value: auth.accessToken },
          { name: STORAGE_KEYS.refresh, value: auth.refreshToken },
          { name: STORAGE_KEYS.user, value: JSON.stringify(auth.user) },
        ],
      },
    ],
  };
}

// ---------------------------------------------------------------------------
// Seeding de reconciliación (precondición del flow #3)
// ---------------------------------------------------------------------------

/**
 * Sube un statement bancario CSV vía API y devuelve la reconciliación creada.
 *
 * No requiere transacciones internas previas: el engine sólo matchea internas
 * en estado Posted/Reconciled (el upload UI las deja en Pending), así que un
 * statement sobre una fecha fresca produce discrepancias `MissingInternal`
 * directamente — justo lo que el flow #3 necesita para resolver + aprobar.
 */
export async function uploadStatementViaApi(
  ctx: APIRequestContext,
  accessToken: string,
  opts: { accountId?: string; date: string; csv: string },
): Promise<StatementUploadResult> {
  const accountId = opts.accountId ?? SEED_ACCOUNT_ID;
  const res = await ctx.post(
    `${API_BASE_URL}/api/reconciliations/accounts/${accountId}/date/${opts.date}/statement`,
    {
      headers: { Authorization: `Bearer ${accessToken}` },
      multipart: {
        file: {
          name: `e2e-statement-${RUN_ID}.csv`,
          mimeType: "text/csv",
          buffer: Buffer.from(opts.csv, "utf-8"),
        },
      },
    },
  );
  if (!res.ok()) {
    throw new Error(
      `Upload de statement vía API falló (${res.status()}): ${await res.text()}`,
    );
  }
  return (await res.json()) as StatementUploadResult;
}

/**
 * Construye un CSV de statement con N líneas "bank-only" (sin contraparte
 * interna) → cada una se convierte en una discrepancia.
 */
export function buildStatementCsv(
  date: string,
  currencyCode = "COP",
  lines = 3,
): string {
  const rows = ["ExternalReference,Amount,CurrencyCode,ValueDate,Description"];
  for (let i = 0; i < lines; i++) {
    rows.push(
      `${e2eExternalId(`stmt-${i}`)},${(i + 1) * 1000},${currencyCode},${date},Cargo bancario E2E ${i}`,
    );
  }
  return rows.join("\n");
}

/** Crea un APIRequestContext suelto (para usar fuera de un test, p.ej. en setup). */
export function newApiContext(): Promise<APIRequestContext> {
  return request.newContext();
}
