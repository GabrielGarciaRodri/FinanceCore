-- ============================================================================
-- FINANCECORE - INITIAL DATABASE SCHEMA
-- Sistema de Conciliación y Análisis Financiero
-- ============================================================================
-- Autor: Gabriel - FinanceCore Project
-- Fecha: 2026
-- Descripción: Esquema inicial para sistema ETL financiero
-- ============================================================================

-- Extensiones necesarias
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- ============================================================================
-- ENUMERACIONES (simuladas con dominios para mejor control)
-- ============================================================================

-- Tipos de cuenta
CREATE TYPE account_type AS ENUM (
    'checking',      -- Cuenta corriente
    'savings',       -- Cuenta de ahorro
    'investment',    -- Cuenta de inversión
    'credit',        -- Línea de crédito
    'loan',          -- Préstamo
    'treasury'       -- Tesorería
);

-- Tipos de transacción
CREATE TYPE transaction_type AS ENUM (
    'debit',         -- Débito (salida)
    'credit',        -- Crédito (entrada)
    'transfer_out',  -- Transferencia saliente
    'transfer_in',   -- Transferencia entrante
    'fee',           -- Comisión
    'interest',      -- Interés
    'adjustment'     -- Ajuste
);

-- Estados de transacción
CREATE TYPE transaction_status AS ENUM (
    'pending',       -- Pendiente de procesar
    'processing',    -- En proceso
    'validated',     -- Validada
    'posted',        -- Contabilizada
    'reconciled',    -- Conciliada
    'rejected',      -- Rechazada
    'reversed'       -- Reversada
);

-- Estados de conciliación
CREATE TYPE reconciliation_status AS ENUM (
    'pending',       -- Pendiente
    'in_progress',   -- En progreso
    'completed',     -- Completada
    'completed_with_discrepancies', -- Con descuadres
    'failed'         -- Fallida
);

-- Tipos de fuente de datos
CREATE TYPE source_type AS ENUM (
    'api',           -- API externa
    'csv_file',      -- Archivo CSV
    'excel_file',    -- Archivo Excel
    'sftp',          -- SFTP
    'manual',        -- Entrada manual
    'system'         -- Generado por sistema
);

-- ============================================================================
-- TABLAS PRINCIPALES
-- ============================================================================

-- Instituciones financieras
CREATE TABLE institutions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code VARCHAR(20) NOT NULL UNIQUE,
    name VARCHAR(100) NOT NULL,
    country_code CHAR(2) NOT NULL,
    swift_code VARCHAR(11),
    is_active BOOLEAN NOT NULL DEFAULT true,
    metadata JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

COMMENT ON TABLE institutions IS 'Instituciones financieras (bancos, pasarelas de pago)';

-- Cuentas financieras
CREATE TABLE financial_accounts (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    account_number VARCHAR(34) NOT NULL, -- IBAN compatible
    account_type account_type NOT NULL,
    account_name VARCHAR(100) NOT NULL,
    currency_code CHAR(3) NOT NULL,
    institution_id UUID NOT NULL REFERENCES institutions(id),
    
    -- Saldos (SIEMPRE en la moneda de la cuenta)
    current_balance DECIMAL(18, 4) NOT NULL DEFAULT 0,
    available_balance DECIMAL(18, 4) NOT NULL DEFAULT 0,
    
    -- Control
    is_active BOOLEAN NOT NULL DEFAULT true,
    opened_at DATE,
    closed_at DATE,
    
    -- Auditoría
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    version INT NOT NULL DEFAULT 1, -- Optimistic locking
    
    CONSTRAINT uq_account_institution UNIQUE (account_number, institution_id),
    CONSTRAINT chk_balance_consistency CHECK (
        -- El balance disponible no puede ser mayor al actual en cuentas normales
        CASE 
            WHEN account_type IN ('checking', 'savings', 'treasury') 
            THEN available_balance <= current_balance
            ELSE true
        END
    )
);

COMMENT ON TABLE financial_accounts IS 'Cuentas financieras del sistema';
COMMENT ON COLUMN financial_accounts.version IS 'Versión para control de concurrencia optimista';

-- Tipos de cambio
CREATE TABLE exchange_rates (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    from_currency CHAR(3) NOT NULL,
    to_currency CHAR(3) NOT NULL,
    rate DECIMAL(18, 8) NOT NULL,
    inverse_rate DECIMAL(18, 8) NOT NULL, -- Calculado para consultas inversas
    effective_date DATE NOT NULL,
    effective_time TIME, -- Algunos proveedores dan hora específica
    source VARCHAR(50) NOT NULL, -- Ej: 'ECB', 'BLOOMBERG', 'MANUAL'
    is_official BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_exchange_rate UNIQUE (from_currency, to_currency, effective_date, source),
    CONSTRAINT chk_rate_positive CHECK (rate > 0 AND inverse_rate > 0),
    CONSTRAINT chk_different_currencies CHECK (from_currency != to_currency)
);

COMMENT ON TABLE exchange_rates IS 'Tipos de cambio históricos y actuales';

-- Transacciones financieras
CREATE TABLE transactions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    
    -- Identificación externa (CRÍTICO para idempotencia)
    external_id VARCHAR(100) NOT NULL,
    external_id_source VARCHAR(50) NOT NULL, -- Origen del ID externo
    
    -- Relaciones
    account_id UUID NOT NULL REFERENCES financial_accounts(id),
    
    -- Datos de la transacción
    transaction_type transaction_type NOT NULL,
    status transaction_status NOT NULL DEFAULT 'pending',
    
    -- Importes
    amount DECIMAL(18, 4) NOT NULL, -- Importe en moneda de la cuenta
    currency_code CHAR(3) NOT NULL,
    
    -- Importes originales (si hubo conversión)
    original_amount DECIMAL(18, 4),
    original_currency CHAR(3),
    exchange_rate_used DECIMAL(18, 8),
    exchange_rate_id UUID REFERENCES exchange_rates(id),
    
    -- Fechas financieras
    value_date DATE NOT NULL, -- Fecha valor (afecta saldos)
    booking_date DATE NOT NULL, -- Fecha contable
    execution_date TIMESTAMPTZ, -- Momento exacto de ejecución
    
    -- Descripción y categorización
    description VARCHAR(500),
    category VARCHAR(50),
    subcategory VARCHAR(50),
    
    -- Contraparte
    counterparty_name VARCHAR(200),
    counterparty_account VARCHAR(34),
    counterparty_bank VARCHAR(100),
    counterparty_reference VARCHAR(100),
    
    -- Conciliación
    reconciliation_id UUID, -- Se llena cuando se concilia
    reconciled_at TIMESTAMPTZ,
    
    -- Integridad y trazabilidad
    hash VARCHAR(64) NOT NULL, -- SHA-256 de campos clave para detectar duplicados
    checksum VARCHAR(64), -- Verificación de integridad
    
    -- Metadatos adicionales
    metadata JSONB, -- Datos específicos del proveedor
    tags TEXT[], -- Etiquetas para búsquedas
    
    -- Auditoría
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    processed_at TIMESTAMPTZ,
    
    -- Constraints
    CONSTRAINT uq_external_id UNIQUE (external_id, external_id_source),
    CONSTRAINT chk_amount_sign CHECK (
        -- Débitos negativos, créditos positivos
        CASE 
            WHEN transaction_type IN ('debit', 'transfer_out', 'fee') THEN amount < 0
            WHEN transaction_type IN ('credit', 'transfer_in', 'interest') THEN amount > 0
            ELSE true -- adjustments pueden ser ambos
        END
    ),
    CONSTRAINT chk_currency_conversion CHECK (
        -- Si hay conversión, todos los campos deben estar
        (original_amount IS NULL AND original_currency IS NULL AND exchange_rate_used IS NULL)
        OR
        (original_amount IS NOT NULL AND original_currency IS NOT NULL AND exchange_rate_used IS NOT NULL)
    ),
    CONSTRAINT chk_dates_order CHECK (booking_date >= value_date - INTERVAL '5 days')
);

COMMENT ON TABLE transactions IS 'Transacciones financieras - tabla principal del sistema';
COMMENT ON COLUMN transactions.hash IS 'Hash SHA-256 para detección de duplicados (external_id + amount + value_date)';

-- Fuentes de transacciones (para trazabilidad del ETL)
CREATE TABLE transaction_sources (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    transaction_id UUID NOT NULL REFERENCES transactions(id) ON DELETE CASCADE,
    source_type source_type NOT NULL,
    source_file VARCHAR(500), -- Ruta del archivo si aplica
    source_line INT, -- Línea del archivo si aplica
    source_api VARCHAR(100), -- Endpoint de API si aplica
    raw_data JSONB NOT NULL, -- Datos originales SIN transformar
    checksum VARCHAR(64) NOT NULL, -- Hash del raw_data
    transformation_log JSONB, -- Log de transformaciones aplicadas
    ingested_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ingestion_batch_id UUID -- Para agrupar ingestas del mismo proceso
);

COMMENT ON TABLE transaction_sources IS 'Trazabilidad completa del origen de cada transacción';

-- Entradas contables (partida doble)
CREATE TABLE financial_entries (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    transaction_id UUID NOT NULL REFERENCES transactions(id) ON DELETE CASCADE,
    
    -- Cuenta contable (Plan de cuentas)
    ledger_account VARCHAR(20) NOT NULL,
    ledger_account_name VARCHAR(100),
    
    -- Importe (uno debe ser 0)
    debit_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    credit_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    
    -- Fechas
    entry_date DATE NOT NULL,
    posting_date DATE NOT NULL,
    
    -- Referencia cruzada
    contra_entry_id UUID, -- Referencia a la contrapartida
    
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT chk_single_amount CHECK (
        -- Exactamente uno debe tener valor (partida doble)
        (debit_amount > 0 AND credit_amount = 0) 
        OR 
        (debit_amount = 0 AND credit_amount > 0)
    )
);

COMMENT ON TABLE financial_entries IS 'Asientos contables - implementa partida doble';

-- Balances diarios
CREATE TABLE daily_balances (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    account_id UUID NOT NULL REFERENCES financial_accounts(id),
    balance_date DATE NOT NULL,
    
    -- Saldos
    opening_balance DECIMAL(18, 4) NOT NULL,
    closing_balance DECIMAL(18, 4) NOT NULL,
    
    -- Movimientos del día
    total_debits DECIMAL(18, 4) NOT NULL DEFAULT 0,
    total_credits DECIMAL(18, 4) NOT NULL DEFAULT 0,
    transaction_count INT NOT NULL DEFAULT 0,
    
    -- Reconciliación
    is_reconciled BOOLEAN NOT NULL DEFAULT false,
    reconciled_at TIMESTAMPTZ,
    reconciled_by VARCHAR(100),
    
    -- Verificación
    calculated_closing DECIMAL(18, 4) GENERATED ALWAYS AS (
        opening_balance + total_credits + total_debits -- débitos son negativos
    ) STORED,
    
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_daily_balance UNIQUE (account_id, balance_date),
    CONSTRAINT chk_balance_calculation CHECK (
        -- Verificación de integridad: el cierre calculado debe coincidir
        ABS(closing_balance - (opening_balance + total_credits + total_debits)) < 0.0001
    )
);

COMMENT ON TABLE daily_balances IS 'Saldos diarios por cuenta - esencial para conciliación';

-- Conciliaciones
CREATE TABLE reconciliations (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    
    reconciliation_date DATE NOT NULL,
    account_id UUID NOT NULL REFERENCES financial_accounts(id),
    
    status reconciliation_status NOT NULL DEFAULT 'pending',
    
    -- Métricas
    total_internal_records INT NOT NULL DEFAULT 0,
    total_external_records INT NOT NULL DEFAULT 0,
    matched_count INT NOT NULL DEFAULT 0,
    unmatched_internal INT NOT NULL DEFAULT 0,
    unmatched_external INT NOT NULL DEFAULT 0,
    
    -- Montos
    total_internal_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    total_external_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    discrepancy_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    
    -- Ejecución
    started_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    duration_ms BIGINT,
    
    -- Responsable
    processed_by VARCHAR(100) NOT NULL,
    approved_by VARCHAR(100),
    approved_at TIMESTAMPTZ,
    
    -- Notas y documentación
    notes TEXT,
    resolution_notes TEXT,
    
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_reconciliation_date_account UNIQUE (reconciliation_date, account_id)
);

COMMENT ON TABLE reconciliations IS 'Registro de procesos de conciliación';

-- Detalle de discrepancias de conciliación
CREATE TABLE reconciliation_discrepancies (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    reconciliation_id UUID NOT NULL REFERENCES reconciliations(id) ON DELETE CASCADE,
    
    -- Puede ser interno, externo o ambos
    internal_transaction_id UUID REFERENCES transactions(id),
    external_reference VARCHAR(100),
    
    discrepancy_type VARCHAR(50) NOT NULL, -- 'missing_internal', 'missing_external', 'amount_mismatch', 'date_mismatch'
    
    -- Detalles
    internal_amount DECIMAL(18, 4),
    external_amount DECIMAL(18, 4),
    difference_amount DECIMAL(18, 4),
    
    internal_date DATE,
    external_date DATE,
    
    -- Resolución
    is_resolved BOOLEAN NOT NULL DEFAULT false,
    resolution_type VARCHAR(50), -- 'matched_manually', 'adjustment_created', 'ignored', 'pending_investigation'
    resolution_notes TEXT,
    resolved_at TIMESTAMPTZ,
    resolved_by VARCHAR(100),
    
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

COMMENT ON TABLE reconciliation_discrepancies IS 'Detalle de descuadres encontrados en conciliación';

-- ============================================================================
-- AUDITORÍA Y LOGGING
-- ============================================================================

-- Logs de auditoría (particionada por mes para rendimiento)
CREATE TABLE audit_logs (
    id BIGSERIAL,
    
    -- Entidad afectada
    entity_type VARCHAR(50) NOT NULL,
    entity_id UUID NOT NULL,
    
    -- Acción
    action VARCHAR(20) NOT NULL, -- 'INSERT', 'UPDATE', 'DELETE'
    
    -- Valores
    old_values JSONB,
    new_values JSONB,
    changed_fields TEXT[],
    
    -- Contexto
    user_id VARCHAR(100),
    user_name VARCHAR(200),
    correlation_id UUID, -- Para trazar operaciones relacionadas
    
    -- Request info
    ip_address INET,
    user_agent VARCHAR(500),
    request_path VARCHAR(500),
    
    -- Timestamp
    timestamp TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    PRIMARY KEY (id, timestamp)
) PARTITION BY RANGE (timestamp);

COMMENT ON TABLE audit_logs IS 'Log de auditoría completo - particionado mensualmente';

-- Crear particiones para los próximos 12 meses
DO $$
DECLARE
    start_date DATE := DATE_TRUNC('month', CURRENT_DATE);
    partition_date DATE;
    partition_name TEXT;
BEGIN
    FOR i IN 0..12 LOOP
        partition_date := start_date + (i || ' months')::INTERVAL;
        partition_name := 'audit_logs_' || TO_CHAR(partition_date, 'YYYY_MM');
        
        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS %I PARTITION OF audit_logs
             FOR VALUES FROM (%L) TO (%L)',
            partition_name,
            partition_date,
            partition_date + INTERVAL '1 month'
        );
    END LOOP;
END $$;

-- Logs de procesamiento ETL
CREATE TABLE processing_logs (
    id BIGSERIAL PRIMARY KEY,
    
    -- Identificación del job
    job_id VARCHAR(100) NOT NULL,
    job_type VARCHAR(50) NOT NULL, -- 'ingestion', 'reconciliation', 'report', 'daily_close'
    job_name VARCHAR(200) NOT NULL,
    batch_id UUID NOT NULL DEFAULT uuid_generate_v4(),
    
    -- Estado
    status VARCHAR(20) NOT NULL, -- 'started', 'processing', 'completed', 'failed', 'cancelled'
    
    -- Métricas
    records_total INT,
    records_processed INT DEFAULT 0,
    records_succeeded INT DEFAULT 0,
    records_failed INT DEFAULT 0,
    records_skipped INT DEFAULT 0,
    
    -- Progreso
    progress_percentage DECIMAL(5, 2),
    current_step VARCHAR(100),
    
    -- Errores
    error_count INT DEFAULT 0,
    error_details JSONB,
    last_error TEXT,
    
    -- Tiempo
    started_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    completed_at TIMESTAMPTZ,
    duration_ms BIGINT,
    
    -- Metadatos
    parameters JSONB,
    result_summary JSONB,
    
    -- Contexto
    triggered_by VARCHAR(100),
    correlation_id UUID
);

COMMENT ON TABLE processing_logs IS 'Logs detallados de ejecución de jobs ETL';

-- ============================================================================
-- ÍNDICES
-- ============================================================================

-- Transactions
CREATE INDEX idx_transactions_account_date ON transactions(account_id, value_date);
CREATE INDEX idx_transactions_status ON transactions(status) WHERE status IN ('pending', 'processing');
CREATE INDEX idx_transactions_hash ON transactions(hash);
CREATE INDEX idx_transactions_created_at ON transactions(created_at);
CREATE INDEX idx_transactions_booking_date ON transactions(booking_date);
CREATE INDEX idx_transactions_reconciliation ON transactions(reconciliation_id) WHERE reconciliation_id IS NOT NULL;
CREATE INDEX idx_transactions_category ON transactions(category) WHERE category IS NOT NULL;

-- Para búsquedas de texto en descripción
CREATE INDEX idx_transactions_description_gin ON transactions USING gin(to_tsvector('spanish', description));

-- Financial Entries
CREATE INDEX idx_entries_transaction ON financial_entries(transaction_id);
CREATE INDEX idx_entries_ledger_date ON financial_entries(ledger_account, entry_date);

-- Daily Balances
CREATE INDEX idx_daily_balance_account_date ON daily_balances(account_id, balance_date DESC);
CREATE INDEX idx_daily_balance_unreconciled ON daily_balances(account_id) 
    WHERE is_reconciled = false;

-- Reconciliations
CREATE INDEX idx_reconciliation_status ON reconciliations(status) 
    WHERE status NOT IN ('completed');
CREATE INDEX idx_reconciliation_date ON reconciliations(reconciliation_date DESC);

-- Processing Logs
CREATE INDEX idx_processing_logs_job ON processing_logs(job_type, job_id);
CREATE INDEX idx_processing_logs_status ON processing_logs(status) 
    WHERE status IN ('started', 'processing');
CREATE INDEX idx_processing_logs_batch ON processing_logs(batch_id);

-- Exchange Rates
CREATE INDEX idx_exchange_rates_lookup ON exchange_rates(from_currency, to_currency, effective_date DESC);

-- ============================================================================
-- FUNCIONES Y TRIGGERS
-- ============================================================================

-- Función para actualizar timestamp de modificación
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Aplicar trigger a tablas relevantes
CREATE TRIGGER update_financial_accounts_updated_at
    BEFORE UPDATE ON financial_accounts
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_transactions_updated_at
    BEFORE UPDATE ON transactions
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_daily_balances_updated_at
    BEFORE UPDATE ON daily_balances
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- Función para calcular hash de transacción
CREATE OR REPLACE FUNCTION calculate_transaction_hash()
RETURNS TRIGGER AS $$
BEGIN
    NEW.hash = encode(
        sha256(
            (NEW.external_id || '|' || 
             NEW.amount::TEXT || '|' || 
             NEW.value_date::TEXT || '|' ||
             NEW.account_id::TEXT)::bytea
        ),
        'hex'
    );
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER calculate_transaction_hash_trigger
    BEFORE INSERT OR UPDATE OF external_id, amount, value_date, account_id
    ON transactions
    FOR EACH ROW EXECUTE FUNCTION calculate_transaction_hash();

-- Función para validar partida doble en transacciones
CREATE OR REPLACE FUNCTION validate_double_entry()
RETURNS TRIGGER AS $$
DECLARE
    total_debits DECIMAL(18, 4);
    total_credits DECIMAL(18, 4);
BEGIN
    SELECT 
        COALESCE(SUM(debit_amount), 0),
        COALESCE(SUM(credit_amount), 0)
    INTO total_debits, total_credits
    FROM financial_entries
    WHERE transaction_id = NEW.transaction_id;
    
    -- Incluir el nuevo registro
    IF TG_OP = 'INSERT' THEN
        total_debits := total_debits + NEW.debit_amount;
        total_credits := total_credits + NEW.credit_amount;
    END IF;
    
    -- Validar que la transacción está balanceada
    -- (esto se hace solo cuando la transacción pasa a 'posted')
    -- Por ahora, solo validamos el registro individual
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER validate_double_entry_trigger
    BEFORE INSERT ON financial_entries
    FOR EACH ROW EXECUTE FUNCTION validate_double_entry();

-- ============================================================================
-- DATOS INICIALES
-- ============================================================================

-- Instituciones de ejemplo
INSERT INTO institutions (code, name, country_code, swift_code, metadata) VALUES
('BCOL', 'Banco de Colombia', 'CO', 'BCOLCOBB', '{"type": "bank", "tier": 1}'),
('BBVA', 'BBVA Colombia', 'CO', 'BBVACOBB', '{"type": "bank", "tier": 1}'),
('DAVI', 'Davivienda', 'CO', 'DAVICOBB', '{"type": "bank", "tier": 1}'),
('NUBANK', 'Nu Colombia', 'CO', NULL, '{"type": "neobank", "tier": 2}'),
('PSE', 'PSE - Pagos Seguros en Línea', 'CO', NULL, '{"type": "payment_gateway"}'),
('PAYPAL', 'PayPal', 'US', NULL, '{"type": "payment_gateway", "international": true}'),
('STRIPE', 'Stripe', 'US', NULL, '{"type": "payment_gateway", "international": true}');

-- Monedas principales con tipos de cambio iniciales
INSERT INTO exchange_rates (from_currency, to_currency, rate, inverse_rate, effective_date, source, is_official) VALUES
('USD', 'COP', 4150.00, 0.00024096, CURRENT_DATE, 'MANUAL', false),
('EUR', 'COP', 4520.00, 0.00022124, CURRENT_DATE, 'MANUAL', false),
('USD', 'EUR', 0.92, 1.08695652, CURRENT_DATE, 'MANUAL', false),
('COP', 'USD', 0.00024096, 4150.00, CURRENT_DATE, 'MANUAL', false);

-- ============================================================================
-- VIEWS ÚTILES
-- ============================================================================

-- Vista de saldos actuales por cuenta
CREATE OR REPLACE VIEW v_account_balances AS
SELECT 
    a.id AS account_id,
    a.account_number,
    a.account_name,
    a.account_type,
    a.currency_code,
    i.name AS institution_name,
    a.current_balance,
    a.available_balance,
    COALESCE(
        (SELECT closing_balance 
         FROM daily_balances db 
         WHERE db.account_id = a.id 
         ORDER BY balance_date DESC 
         LIMIT 1),
        0
    ) AS last_reconciled_balance,
    COALESCE(
        (SELECT balance_date 
         FROM daily_balances db 
         WHERE db.account_id = a.id AND db.is_reconciled = true
         ORDER BY balance_date DESC 
         LIMIT 1),
        NULL
    ) AS last_reconciled_date,
    a.is_active,
    a.updated_at
FROM financial_accounts a
JOIN institutions i ON a.institution_id = i.id;

-- Vista de transacciones pendientes de conciliación
CREATE OR REPLACE VIEW v_pending_reconciliation AS
SELECT 
    t.id,
    t.external_id,
    a.account_number,
    a.account_name,
    t.transaction_type,
    t.amount,
    t.currency_code,
    t.value_date,
    t.description,
    t.status,
    t.created_at,
    DATE_PART('day', CURRENT_TIMESTAMP - t.created_at) AS days_pending
FROM transactions t
JOIN financial_accounts a ON t.account_id = a.id
WHERE t.reconciliation_id IS NULL
    AND t.status NOT IN ('rejected', 'reversed')
ORDER BY t.value_date, t.created_at;

-- Vista de resumen diario
CREATE OR REPLACE VIEW v_daily_summary AS
SELECT 
    a.id AS account_id,
    a.account_number,
    a.account_name,
    COALESCE(t.transaction_date, CURRENT_DATE) AS transaction_date,
    COALESCE(t.total_transactions, 0) AS total_transactions,
    COALESCE(t.total_debits, 0) AS total_debits,
    COALESCE(t.total_credits, 0) AS total_credits,
    COALESCE(t.net_change, 0) AS net_change,
    db.is_reconciled,
    db.closing_balance
FROM financial_accounts a
LEFT JOIN LATERAL (
    SELECT 
        DATE(value_date) AS transaction_date,
        COUNT(*) AS total_transactions,
        SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END) AS total_debits,
        SUM(CASE WHEN amount > 0 THEN amount ELSE 0 END) AS total_credits,
        SUM(amount) AS net_change
    FROM transactions
    WHERE account_id = a.id
        AND value_date >= CURRENT_DATE - INTERVAL '30 days'
    GROUP BY DATE(value_date)
) t ON true
LEFT JOIN daily_balances db ON db.account_id = a.id AND db.balance_date = t.transaction_date
WHERE a.is_active = true
ORDER BY a.account_number, t.transaction_date DESC;

-- ============================================================================
-- GRANT PERMISOS (ajustar según ambiente)
-- ============================================================================

-- Crear rol de aplicación
-- CREATE ROLE financecore_app WITH LOGIN PASSWORD 'your_secure_password';
-- GRANT CONNECT ON DATABASE financecore TO financecore_app;
-- GRANT USAGE ON SCHEMA public TO financecore_app;
-- GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO financecore_app;
-- GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO financecore_app;

-- Rol de solo lectura para reportes
-- CREATE ROLE financecore_readonly WITH LOGIN PASSWORD 'your_secure_password';
-- GRANT CONNECT ON DATABASE financecore TO financecore_readonly;
-- GRANT USAGE ON SCHEMA public TO financecore_readonly;
-- GRANT SELECT ON ALL TABLES IN SCHEMA public TO financecore_readonly;
