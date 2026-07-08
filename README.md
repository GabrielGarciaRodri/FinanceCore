# FinanceCore

[![CI](https://github.com/GabrielGarciaRodri/FinanceCore/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/GabrielGarciaRodri/FinanceCore/actions/workflows/ci.yml)

**Multi-account, multi-currency financial reconciliation system.** FinanceCore ingests transactions from heterogeneous sources (API, CSV/Excel uploads, SFTP), matches them against bank statement lines with configurable tolerances, and tracks balances with daily close and full audit trail — a .NET 10 Clean Architecture backend with a typed Next.js 14 frontend.

## 🚀 Live demo

> **Try it now:** [financecore-demo.vercel.app](https://financecore-demo.vercel.app/)
>
> | | |
> |---|---|
> | **User** | `demo@financecore.local` |
> | **Password** | `Demo!2026` |
>
> Read-only user with sample data. The API runs on Render's free tier and sleeps after inactivity — the first visit shows a splash while it wakes up (~1 min).

![Reconciliation flow](docs/media/reconciliation-flow.gif)

## What it does

- **Transaction ingestion** from multiple sources with idempotency guarantees (duplicate detection via `external_id` + hash).
- **Reconciliation engine**: matches internal transactions against imported bank statements with configurable tolerances (`AmountTolerance`, `DateToleranceDays`, `BalanceTolerance`), discrepancy detection and approval workflow.
- **Balance management**: daily close job, real-time balances with optimistic concurrency, immutable posted transactions.
- **Multi-currency**: `Money`/`Currency` value objects with banker's rounding, scheduled FX rate updates.
- **Auth & roles**: ASP.NET Identity + JWT with refresh-token rotation; `Admin` / `FinanceAdmin` / `Reader` roles enforced per endpoint and mirrored in the UI (read-only demo user).
- **Observability**: structured logging (Serilog), OpenTelemetry + Prometheus `/metrics`, health checks at `/health`.

## Stack

| Layer | Tech |
|---|---|
| Backend | **.NET 10** · ASP.NET Core · Clean Architecture · CQRS (MediatR) · FluentValidation |
| Frontend | **Next.js 14** (App Router) · TypeScript · TanStack Query · shadcn/ui · types generated from OpenAPI |
| Data | **PostgreSQL 16** (EF Core writes, Dapper reads) · Redis (optional, falls back to in-memory cache) |
| Jobs | **Hangfire** — daily close, FX rates, file ingestion, cleanup |
| Testing | xUnit (domain) · **Testcontainers** (integration, real Postgres) · **Playwright** E2E in CI |
| Deploy | Docker multi-stage · Render (API) · Vercel (web) · Neon (Postgres) |

Architecture details, layer rules and design decisions: **[ARCHITECTURE.md](ARCHITECTURE.md)**.

## Quick start

Prerequisites: .NET 10 SDK, Node 20+, Docker.

```bash
# 1. Infrastructure (Postgres + Redis + pgAdmin)
docker compose up -d

# 2. Database schema
for f in database/migrations/V00*.sql; do
  docker compose exec -T postgres psql -U postgres -d financecore < "$f"
done

# 3. API → http://localhost:5000 (Swagger at /swagger)
cd src/FinanceCore.API && dotnet run

# 4. Web → http://localhost:3000
cd web && npm install && npm run dev
```

Default seed user: `admin@financecore.local` / `ChangeMe!2026` (see `Identity:Seed` options).

| Service | URL |
|---|---|
| Web | http://localhost:3000 |
| API / Swagger | http://localhost:5000 · /swagger |
| Hangfire dashboard | http://localhost:5000/hangfire |
| Health / Metrics | http://localhost:5000/health · /metrics |

## Tests

```bash
dotnet test                      # domain + integration (needs Docker for Testcontainers)
cd web && npx playwright test    # E2E against a running stack
```

CI runs build, unit, integration and full-stack E2E (Playwright against a real API + Postgres) on every push — see [ci.yml](.github/workflows/ci.yml).

---

Developed by **Gabriel García Rodríguez** — Full Stack Developer · .NET · Financial Systems
