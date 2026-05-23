-- ============================================================================
-- FINANCECORE - V004 BaseEntity.UpdatedAt columns
-- ============================================================================
-- Autor: Gabriel - FinanceCore Project
-- Descripción: Las entidades Reconciliation, ReconciliationDiscrepancy y
--              ExchangeRate heredan de BaseEntity (que define CreatedAt y
--              UpdatedAt). V001 sólo creó created_at en estas tablas, así
--              que cualquier SELECT por EF falla con
--              `column ... .updated_at does not exist`.
--              Esta migración agrega las columnas faltantes y las hace
--              nullable (matchea la firma `DateTimeOffset?` de BaseEntity).
-- ============================================================================

ALTER TABLE reconciliations
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ;

ALTER TABLE reconciliation_discrepancies
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ;

ALTER TABLE exchange_rates
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ;

-- Refrescar estadísticas para el planner.
ANALYZE reconciliations;
ANALYZE reconciliation_discrepancies;
ANALYZE exchange_rates;
