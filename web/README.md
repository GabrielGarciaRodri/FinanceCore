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
