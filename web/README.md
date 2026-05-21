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
