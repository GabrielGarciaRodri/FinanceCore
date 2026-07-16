-- =============================================================================
-- V008: Reglas de alerta de negocio (SCRUM-45)
-- Alertas configurables por el usuario: payout esperado que no llegó,
-- discrepancia sobre umbral y saldo bajo. Entrega por email y/o webhook.
-- =============================================================================

CREATE TABLE alert_rules (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),

    name VARCHAR(100) NOT NULL,

    -- 'MissingPayout' | 'DiscrepancyThreshold' | 'LowBalance'
    rule_type VARCHAR(30) NOT NULL,

    -- Cuenta vigilada. Requerida para LowBalance; NULL = todas (DiscrepancyThreshold).
    account_id UUID REFERENCES financial_accounts(id),

    -- Perfil de fuente vigilado (requerido para MissingPayout).
    source_profile_id UUID REFERENCES reconciliation_source_profiles(id),

    -- Umbral absoluto en la moneda de la cuenta (LowBalance, DiscrepancyThreshold).
    threshold_amount DECIMAL(18, 4),

    -- Umbral como fracción del total externo conciliado (DiscrepancyThreshold).
    threshold_percent DECIMAL(9, 6),

    -- Días sin payout antes de disparar (MissingPayout).
    lookback_days INT,

    -- Flags como texto: 'Email' | 'Webhook' | 'Email, Webhook'.
    channels VARCHAR(30) NOT NULL,

    -- Destinatario del email; NULL = destinatario global de la config.
    email_to VARCHAR(200),

    -- Anti-spam: horas mínimas entre disparos consecutivos.
    cooldown_hours INT NOT NULL DEFAULT 24,

    is_enabled BOOLEAN NOT NULL DEFAULT true,
    last_triggered_at TIMESTAMPTZ,

    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ
);

CREATE INDEX idx_alert_rules_account ON alert_rules (account_id);
CREATE INDEX idx_alert_rules_source_profile ON alert_rules (source_profile_id);
