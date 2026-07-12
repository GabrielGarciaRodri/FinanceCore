# Operations

Operational reference for running FinanceCore outside local development: environment variables, database migration strategy, and the deploy runbook for the public demo (Render + Vercel + Neon).

Deployment topology:

| Piece | Where | Notes |
|---|---|---|
| API (ASP.NET Core) | [Render](https://render.com) — Docker web service, free tier | Blueprint in [`render.yaml`](../render.yaml). Sleeps after inactivity (~1 min cold start). |
| Web (Next.js 14) | [Vercel](https://vercel.com) — Hobby | Root directory `web/`. |
| PostgreSQL 16 | [Neon](https://neon.tech) — free tier | Schema applied manually via versioned SQL (see below). |
| Redis | **none in the demo** | Empty connection string → the API falls back to in-memory cache. |

Demo URLs: front `https://financecore-demo.vercel.app` · API `https://financecore.onrender.com`.

## Environment variables — API

Config binds from `appsettings.json` and can be overridden per-key with env vars using `__` as the section separator (standard ASP.NET Core). `.env.example` at the repo root shows the same list in file form.

| Variable | Required in prod | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | yes | The demo runs as `Development` **on purpose**: it enables the identity seeder and the demo re-seed endpoint (`POST /api/dev/seed-reconciliations-demo`). A real production deploy should use `Production`. |
| `ConnectionStrings__DefaultConnection` | yes | Npgsql **keyword format**, not a `postgresql://` URL. For Neon: `Host=ep-xxx.neon.tech;Database=db;Username=u;Password=p;SSL Mode=Require;Trust Server Certificate=true`. |
| `ConnectionStrings__HangfireConnection` | yes | Same value as above in the demo (single database). |
| `ConnectionStrings__Redis` | yes — set it **empty** if there is no Redis | The `appsettings.json` default is `localhost:6379`, which does not exist on Render and causes timeouts/500s on cached endpoints. Empty string → `AddDistributedMemoryCache` fallback (see `Program.cs`). |
| `Jwt__SigningKey` | yes | ≥ 32 ASCII chars, random. The repo default is empty — the API needs this to issue tokens. |
| `Identity__Seed__AdminPassword` | yes | **The repo is public and ships a default admin password in `appsettings.json`.** Override it with a private value *before the first deploy*: the seeder creates the admin only once, on the first run against a fresh DB. |
| `Identity__Seed__DemoUser__Enabled` | demo only | `true` creates the read-only demo user (`Reader` role) that visitors log in with. |
| `Cors__AllowedOrigins__0` | yes | The front's public URL. Only known after the front is deployed — set it and redeploy the API. |
| `RateLimiting__Enabled` | demo only | Rate limiting is disabled in `appsettings.Development.json` (local dev + E2E make legitimate bursts). Since the demo runs as `Development`, force it back on with `true` — env vars override the JSON. |
| `FinanceCore__ExchangeRates__ApiKey` | optional | exchangerate-api.com key for the scheduled FX rate job. Without it the job fails gracefully; balances still work. |
| `Authentication__ApiKeys__0`, `__1`, … | optional | API keys for machine-to-machine ingestion clients. Not used by the demo. |

## Environment variables — Web

Defined in Vercel (Project → Settings → Environment Variables). Local template: [`web/.env.local.example`](../web/.env.local.example).

| Variable | Required in prod | Notes |
|---|---|---|
| `NEXT_PUBLIC_API_URL` | yes | API base URL, no trailing slash. |
| `NEXT_PUBLIC_SITE_URL` | yes | Public URL of the front itself — base for absolute OG image URLs. Without it previews fall back to `VERCEL_URL`/localhost. |
| `NEXT_PUBLIC_DEMO_MODE` | demo only | `true` shows the persistent demo banner and preloads the demo credentials on the login form. The banner is environment-wide by design (admins see it too). |
| `NEXT_PUBLIC_DEMO_EMAIL` / `NEXT_PUBLIC_DEMO_PASSWORD` | demo only | Must mirror `Identity:Seed:DemoUser` on the backend. |
| `NEXT_PUBLIC_FEEDBACK_EMAIL` | optional | Feedback mailto. Unset, the front only shows the GitHub Issues link (keeps personal inboxes out of the public repo). |

`NEXT_PUBLIC_*` values are inlined at **build time** — changing one requires a new deployment, not just a restart.

## Database migrations

The schema is managed with **versioned SQL files** in [`database/migrations/`](../database/migrations/) (`V001__…` through `V006__…`), applied in order. There is **no EF Core auto-migrate** anywhere — EF maps to the schema but never creates it. This is deliberate: financial schemas change under review, not on app startup.

- Local: `docker compose exec -T postgres psql -U postgres -d financecore < database/migrations/V00X__*.sql` (loop in the README quick start), or `database/apply-migrations.ps1`.
- Neon (prod): run the same files in order against the Neon connection with `psql`. New migrations are additive — never edit an already-applied `V00X` file; add the next number.
- `V006__Seed_Demo_Account.sql` seeds the demo account. Reconciliation demo data is seeded at runtime by an admin via `POST /api/dev/seed-reconciliations-demo` (Development only).

## Deploy runbook

### API on Render

1. Render → New → **Blueprint** → connect the repo. Render reads [`render.yaml`](../render.yaml) and creates the `financecore-api` Docker service (health check on `/health`, auto-deploy from `main`).
2. Fill the `sync: false` env vars by hand in the dashboard: `Identity__Seed__AdminPassword`, `Jwt__SigningKey`, both connection strings, `Cors__AllowedOrigins__0` (after the front exists).
3. **Gotcha:** if the service was created manually instead of via Blueprint, the env vars declared in `render.yaml` are *not* applied — set all of them (including `ConnectionStrings__Redis=""` and `RateLimiting__Enabled=true`) by hand.
4. Apply the SQL migrations to Neon before first boot (the identity seeder needs the schema).
5. Verify: `GET /health` returns healthy; log in with the demo user on the front.

### Web on Vercel

1. Import the repo, set **Root Directory** to `web/`.
2. Set the `NEXT_PUBLIC_*` variables above, then deploy.
3. Set the resulting URL as `Cors__AllowedOrigins__0` on Render and redeploy the API.
4. **Gotcha:** Vercel's "Redeploy" button rebuilds the *same commit* of that deployment — it does not pick up the latest push. To deploy new code, push (or "Create Deployment" from the latest commit).

### Known gotchas

- **Adblockers block `*.onrender.com`** (`ERR_BLOCKED_BY_CLIENT`). The front's API wake gate is fail-open for this reason — don't make reachability checks blocking.
- **Cold start:** the free Render instance sleeps after inactivity; first request takes ~1 min. The front shows a wake-up splash; UptimeRobot monitors (every 5 min on `/health` and the front) double as keep-warm.
- **Link previews (WhatsApp etc.) cache per URL** — after changing OG tags, bust the cache with a dummy query string.

## Monitoring

- **Health:** `GET /health` (also Render's health check). Prometheus metrics at `/metrics`.
- **Uptime:** UptimeRobot, 2 monitors every 5 min (API `/health` + front).
- **Traffic:** Vercel Analytics (pageviews) on the front.
- **Logs:** structured Serilog to console (Render dashboard → Logs) and rolling files under `logs/` locally.
