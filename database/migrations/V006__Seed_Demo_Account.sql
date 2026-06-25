-- ============================================================================
-- FINANCECORE - V006: SEED DE INSTITUCIÓN Y CUENTA DEMO
-- ============================================================================
-- Autor: Gabriel - FinanceCore Project
-- Descripción:
--   Provisiona de forma idempotente la institución y la cuenta seed (COP) que
--   el resto del sistema asume PREEXISTENTES pero que hasta ahora nadie creaba
--   de forma automática:
--     - Suite E2E      → web/e2e/helpers/api.ts  (SEED_ACCOUNT_ID = a1b2c3d4-…-001)
--     - Demo seeder    → DevController.SeedAccountId (mismo GUID)
--     - Demo pública   → entorno Production, donde el DevController está
--                        deshabilitado (devuelve 404), así que la cuenta NO puede
--                        depender de ese endpoint.
--
--   Antes de esta migración la cuenta debía crearse a mano en cada entorno: eso
--   rompía CI sobre una DB fresca (la suite E2E fallaba en el primer upload a la
--   cuenta seed) y dejaba la demo en Production sin datos.
--
--   Se usan UUIDs FIJOS (no uuid_generate_v4) para que los IDs sean estables
--   entre entornos y referenciables desde código y tests. Los INSERT son
--   idempotentes (ON CONFLICT DO NOTHING) para que aplicar la migración sobre
--   una DB que ya tenía la cuenta creada a mano sea un no-op seguro.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- Institución demo (FK requerida por financial_accounts.institution_id)
-- ----------------------------------------------------------------------------
INSERT INTO institutions (id, code, name, country_code, swift_code, is_active)
VALUES (
    'd1e2f3a4-0000-0000-0000-000000000001',
    'DEMO-BANK',
    'Banco Demo FinanceCore',
    'CO',
    'DEMOCOBB',
    true
)
ON CONFLICT (id) DO NOTHING;

-- ----------------------------------------------------------------------------
-- Cuenta seed COP — la que usan la suite E2E y el demo seeder.
-- Replica Account.CreateCheckingAccount(initialBalance: 0): tipo 'checking',
-- saldos en 0 (satisface chk_balance_consistency: available <= current), version 1.
-- created_at / updated_at quedan en el DEFAULT CURRENT_TIMESTAMP de la tabla.
-- ----------------------------------------------------------------------------
INSERT INTO financial_accounts (
    id, account_number, account_type, account_name, currency_code,
    institution_id, current_balance, available_balance, is_active,
    opened_at, version
)
VALUES (
    'a1b2c3d4-0000-0000-0000-000000000001',
    'FC-DEMO-COP-0001',
    'checking',
    'Cuenta Demo FinanceCore (COP)',
    'COP',
    'd1e2f3a4-0000-0000-0000-000000000001',
    0,
    0,
    true,
    DATE '2026-01-01',
    1
)
ON CONFLICT (id) DO NOTHING;
