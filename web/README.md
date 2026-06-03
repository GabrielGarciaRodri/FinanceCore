# FinanceCore Web

UI Next.js para FinanceCore.

## Stack

- **Next.js 14** (App Router) + React 18 + TypeScript
- **Tailwind CSS** + **shadcn/ui** (new-york style, base slate)
- **TanStack Query** para data fetching
- **Axios** con interceptor de auto-refresh JWT
- **react-hook-form** + **zod** para formularios

## Setup

```bash
cp .env.local.example .env.local
npm install
npm run dev
```

La app corre en `http://localhost:3000` y habla con el backend en
`NEXT_PUBLIC_API_URL` (default `http://localhost:5000`). El backend ya
permite ese origen vía CORS Development.

## Auth

- Login: `admin@financecore.local` / `ChangeMe!2026` (seed por defecto del backend).
- Tokens en `localStorage` (`fc:access`, `fc:refresh`).
- Si una request devuelve 401, el cliente intenta `/api/auth/refresh` una
  vez y reintenta la request original.

## Scripts

| Comando | Descripción |
|---|---|
| `npm run dev` | Servidor de desarrollo |
| `npm run build` | Build de producción |
| `npm run start` | Servidor de producción |
| `npm run lint` | Lint con ESLint |
| `npm run typecheck` | Type-check sin emitir |
| `npm run gen:api` | Regenera `lib/api/generated.ts` desde el Swagger del backend |
| `npm run test:e2e` | Corre la suite E2E de Playwright |
| `npm run test:e2e:ui` | Corre la suite E2E en modo UI (debug interactivo) |

## Tests E2E (Playwright)

Suite end-to-end sobre los 3 flows backbone: **auth**, **upload de
transacciones** y **reconciliación** (`e2e/*.spec.ts`).

### Precondición: stack vivo

Playwright **sólo levanta el front** (`next dev`). El backend y la base son
precondición externa y tienen que estar arriba antes de correr la suite:

```bash
# (1) infra
cd D:\FinanceCore
docker compose up -d              # Postgres + Redis

# (2) backend en Development (necesario para el seed admin y CORS :3000)
cd src/FinanceCore.API
dotnet run                        # http://localhost:5000
```

### Primera vez

```bash
cd web
npm install
npx playwright install chromium   # baja el browser (one-time)
```

### Correr

```bash
npm run test:e2e
```

`webServer` en `playwright.config.ts` levanta `next dev` automáticamente (y
reusa uno ya corriendo fuera de CI). Variables opcionales: `E2E_BASE_URL`
(default `http://localhost:3000`) y `E2E_API_URL` (default
`http://localhost:5000`).

### Cómo funciona

- **Auth**: `auth.setup.ts` hace login programático (`POST /api/auth/login`) y
  persiste el `storageState` en `e2e/.auth/user.json` (localStorage
  `fc:access`/`fc:refresh`/`fc:user`). Sólo `auth.spec.ts` ejerce el form real.
- **Aislamiento por corrida**: no hay endpoint para crear cuentas, así que
  todos los tests usan la cuenta seed (`a1b2c3d4-…-001`, COP) con namespacing:
  externalIds con prefijo `e2e-{runId}-` y una fecha de reconciliación única
  por run. La acumulación de datos es inocua → **no hay teardown en v1**.
- **Reconciliación**: la reconciliación-con-discrepancias se siembra vía API
  (statement sobre fecha única); la UI sólo maneja detalle → resolver → aprobar.

### Follow-ups conocidos

- **CI de web**: hoy `ci.yml` sólo corre el backend (.NET). Levantar el stack
  completo en Actions para esta suite es un lift dedicado.
- **Teardown opcional**: `DELETE /api/dev/e2e-data?runId=` para limpiar datos
  acumulados (no bloqueante por el namespacing).

## Tipos compartidos con el backend (codegen)

Los tipos TS de los DTOs viven en dos archivos:

- **`lib/api/generated.ts`** — autogenerado desde el OpenAPI/Swagger del
  backend con [`openapi-typescript`](https://openapi-ts.dev/). **No editar
  a mano**: se sobreescribe cada vez que se corre `npm run gen:api`.
- **`lib/api/types.ts`** — entrada pública. Re-exporta los tipos generados
  con nombres amigables y suma los tipos frontend-only (helpers genéricos,
  string-literal unions de enums que el backend serializa como `string`
  crudo, request shapes de query params).

### Cuándo regenerar

Cada vez que toques un DTO en backend (o agregues/cambies un endpoint):

```bash
# Con el API corriendo en http://localhost:5000
cd web
npm run gen:api
npm run typecheck
```

Si el typecheck rompe, son los call sites que tienen que adaptarse al
nuevo shape — el codegen es la fuente de verdad.

### Por qué requiere reiniciar el API después de tocar Swagger config

El endpoint `/swagger/v1/swagger.json` lo sirve Swashbuckle dentro del
proceso de ASP.NET. Si tocás `Program.cs` (security schemes, schema
filters, etc.) hay que reiniciar `dotnet run` para que el Swagger
emitido refleje los cambios.

### Asimetría conocida con enums

Algunos enums (`TransactionType`, `TransactionStatus`, `AccountType`)
salen del backend como `string` plano en los response DTOs porque el
mapper los convierte con `.ToString()`. Los mantenemos como
string-literal unions en `types.ts` para preservar autocomplete +
runtime narrowing. `DiscrepancyType`, `ReconciliationStatus`,
`ResolutionType`, `SourceType` sí salen como enums tipados del OpenAPI
y se re-exportan automáticamente.
