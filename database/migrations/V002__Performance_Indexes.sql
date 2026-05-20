-- ============================================================================
-- FINANCECORE - V002 PERFORMANCE INDEXES
-- ============================================================================
-- Autor: Gabriel - FinanceCore Project
-- Fase 6: Performance y observabilidad
-- Descripción: Índices de soporte para los filtros reales que ejecuta
--              TransactionRepository.SearchAsync y los reportes de
--              reconciliación. Todos los índices se crean con
--              IF NOT EXISTS para que la migración sea idempotente.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- TRANSACTIONS - cobertura para SearchAsync y filtros frecuentes
-- ----------------------------------------------------------------------------

-- SearchAsync ordena por (value_date DESC, created_at DESC) por cuenta.
-- Un índice DESC compuesto permite que PostgreSQL evite un sort explícito.
CREATE INDEX IF NOT EXISTS idx_transactions_account_value_created_desc
    ON transactions (account_id, value_date DESC, created_at DESC);

-- Búsqueda por tipo + fecha (filtros de UI/reporting).
CREATE INDEX IF NOT EXISTS idx_transactions_type_date
    ON transactions (transaction_type, value_date);

-- Filtros por estado + fecha (ej. "todas las Posted del último mes").
CREATE INDEX IF NOT EXISTS idx_transactions_status_date
    ON transactions (status, value_date);

-- Búsquedas por rango de monto (criterio MinAmount/MaxAmount de SearchAsync).
CREATE INDEX IF NOT EXISTS idx_transactions_amount
    ON transactions (amount);

-- Lookup por ExternalId (ya hay UNIQUE) y por subcategory (para reportes).
CREATE INDEX IF NOT EXISTS idx_transactions_subcategory
    ON transactions (subcategory)
    WHERE subcategory IS NOT NULL;

-- Para GetUnreconciledByAccountAsync: WHERE account_id = ? AND reconciliation_id IS NULL AND status = 'posted'
CREATE INDEX IF NOT EXISTS idx_transactions_unreconciled_posted
    ON transactions (account_id, value_date)
    WHERE reconciliation_id IS NULL AND status = 'posted';

-- ----------------------------------------------------------------------------
-- RECONCILIATIONS - índices para SearchAsync / GetByAccountAsync
-- ----------------------------------------------------------------------------

-- Búsqueda por estado + fecha (auditoría: "todas las que tienen discrepancias").
CREATE INDEX IF NOT EXISTS idx_reconciliations_status_date
    ON reconciliations (status, reconciliation_date DESC);

-- Búsqueda por cuenta + estado (panel de control por cuenta).
CREATE INDEX IF NOT EXISTS idx_reconciliations_account_status
    ON reconciliations (account_id, status, reconciliation_date DESC);

-- ----------------------------------------------------------------------------
-- RECONCILIATION_DISCREPANCIES - lookups frecuentes
-- ----------------------------------------------------------------------------

-- Por transacción interna (de qué reconciliaciones forma parte).
CREATE INDEX IF NOT EXISTS idx_discrepancies_internal_tx
    ON reconciliation_discrepancies (internal_transaction_id)
    WHERE internal_transaction_id IS NOT NULL;

-- Pendientes de resolución por tipo (workflow de equipo de finanzas).
CREATE INDEX IF NOT EXISTS idx_discrepancies_unresolved_type
    ON reconciliation_discrepancies (discrepancy_type, created_at DESC)
    WHERE is_resolved = false;

-- ----------------------------------------------------------------------------
-- DAILY_BALANCES - acceso por fecha global (job de cierre)
-- ----------------------------------------------------------------------------

-- Cierre diario consulta TODOS los balances de una fecha específica.
CREATE INDEX IF NOT EXISTS idx_daily_balances_date
    ON daily_balances (balance_date);

-- ----------------------------------------------------------------------------
-- EXCHANGE_RATES - hot path de conversión
-- ----------------------------------------------------------------------------

-- ExchangeRateRepository.GetRateAsync hace
--   WHERE from = ? AND to = ? AND effective_date <= ?
--   ORDER BY effective_date DESC LIMIT 1
-- El índice DESC composite cubre exactamente este patrón.
CREATE INDEX IF NOT EXISTS idx_exchange_rates_lookup_desc
    ON exchange_rates (from_currency, to_currency, effective_date DESC);

-- ============================================================================
-- ANALYZE para refrescar estadísticas del planner
-- ============================================================================
ANALYZE transactions;
ANALYZE reconciliations;
ANALYZE reconciliation_discrepancies;
ANALYZE daily_balances;
ANALYZE exchange_rates;
