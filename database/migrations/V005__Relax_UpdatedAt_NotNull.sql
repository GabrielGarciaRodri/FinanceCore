-- ============================================================================
-- FINANCECORE - V005 Relax updated_at NOT NULL
-- ============================================================================
-- Autor: Gabriel - FinanceCore Project
-- Descripción: BaseEntity.UpdatedAt es nullable (DateTimeOffset?) en C#:
--              se setea sólo cuando la entidad se modifica, no en el INSERT.
--              V001 creó updated_at como NOT NULL DEFAULT CURRENT_TIMESTAMP
--              en varias tablas, pero EF Core inserta NULL explícito en lugar
--              de omitir la columna — los defaults no se aplican cuando el
--              valor se manda explícito, así que el INSERT viola la restricción.
--
--              Solución: alinear el schema al modelo de dominio relajando
--              NOT NULL en updated_at de las tablas afectadas. Los triggers
--              `update_*_updated_at` siguen funcionando en UPDATE (que es el
--              caso de uso real de la columna).
--
--              ALTER ... DROP NOT NULL es idempotente: no falla si ya está
--              nullable, así que la migración se puede reaplicar sin riesgo.
-- ============================================================================

ALTER TABLE institutions       ALTER COLUMN updated_at DROP NOT NULL;
ALTER TABLE financial_accounts ALTER COLUMN updated_at DROP NOT NULL;
ALTER TABLE transactions       ALTER COLUMN updated_at DROP NOT NULL;
ALTER TABLE daily_balances     ALTER COLUMN updated_at DROP NOT NULL;

-- Refrescar estadísticas.
ANALYZE institutions;
ANALYZE financial_accounts;
ANALYZE transactions;
ANALYZE daily_balances;
