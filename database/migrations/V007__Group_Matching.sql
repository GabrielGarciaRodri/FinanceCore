-- =============================================================================
-- V007: Matching N:1 de payouts de pasarela (SCRUM-41)
-- Perfiles por fuente + grupos de matching (payout ↔ N ventas, neto de fee).
-- Diseño: docs/design/SCRUM-41-group-matching.md
-- =============================================================================

-- Perfil de conciliación por pasarela/fuente.
CREATE TABLE reconciliation_source_profiles (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),

    -- NULL = el perfil aplica a todas las cuentas.
    account_id UUID REFERENCES financial_accounts(id),

    source_key VARCHAR(50) NOT NULL,
    display_name VARCHAR(100) NOT NULL,

    -- Regex sobre referencia + descripción de la línea de extracto.
    payout_pattern VARCHAR(200) NOT NULL,

    -- Campo de la transacción interna que identifica las ventas de la fuente.
    internal_match_field VARCHAR(30) NOT NULL, -- 'ExternalIdSource' | 'Category' | 'CounterpartyName'
    internal_match_pattern VARCHAR(200) NOT NULL,

    -- Comisión esperada y semibanda de tolerancia, como fracción (0.035 = 3.5%).
    expected_fee_percent DECIMAL(6, 5) NOT NULL,
    fee_tolerance_percent DECIMAL(6, 5) NOT NULL,

    -- Días hacia atrás desde el payout que puede cubrir un grupo.
    grouping_window_days INT NOT NULL,

    is_active BOOLEAN NOT NULL DEFAULT true,

    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ
);

-- Un perfil por fuente y cuenta (la fila global usa account_id NULL).
CREATE UNIQUE INDEX uq_source_profile_key_account
    ON reconciliation_source_profiles (source_key, COALESCE(account_id, '00000000-0000-0000-0000-000000000000'::uuid));

CREATE INDEX idx_source_profile_account ON reconciliation_source_profiles (account_id);

-- Grupo de matching: un payout (línea de extracto) ↔ N ventas internas.
CREATE TABLE reconciliation_match_groups (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    reconciliation_id UUID NOT NULL REFERENCES reconciliations(id) ON DELETE CASCADE,
    source_profile_id UUID NOT NULL REFERENCES reconciliation_source_profiles(id),

    external_reference VARCHAR(100) NOT NULL,
    payout_amount DECIMAL(18, 4) NOT NULL,
    payout_date DATE NOT NULL,

    grouped_count INT NOT NULL,
    grouped_amount DECIMAL(18, 4) NOT NULL,
    fee_amount DECIMAL(18, 4) NOT NULL,
    fee_percent DECIMAL(9, 6) NOT NULL,

    -- Transacción Fee generada al conciliar el grupo.
    fee_transaction_id UUID REFERENCES transactions(id),

    window_start DATE NOT NULL,
    window_end DATE NOT NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ
);

CREATE INDEX idx_match_group_reconciliation ON reconciliation_match_groups (reconciliation_id);

-- Un payout (referencia) forma a lo sumo un grupo dentro de una conciliación.
CREATE UNIQUE INDEX uq_match_group_reference
    ON reconciliation_match_groups (reconciliation_id, external_reference);

-- Miembros del grupo. El índice único por transacción garantiza exclusividad:
-- una venta pertenece a lo sumo a UN grupo, para siempre.
CREATE TABLE reconciliation_match_group_items (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    group_id UUID NOT NULL REFERENCES reconciliation_match_groups(id) ON DELETE CASCADE,
    transaction_id UUID NOT NULL REFERENCES transactions(id),

    -- Snapshot del monto al momento de agrupar.
    amount DECIMAL(18, 4) NOT NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ
);

CREATE UNIQUE INDEX uq_match_group_item_transaction ON reconciliation_match_group_items (transaction_id);
CREATE INDEX idx_match_group_item_group ON reconciliation_match_group_items (group_id);
