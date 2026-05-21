-- ============================================================================
-- FINANCECORE - V003 IDENTITY + REFRESH TOKEN SCHEMA
-- ============================================================================
-- Autor: Gabriel - FinanceCore Project
-- Fase A: Backend de autenticación con ASP.NET Core Identity + JWT
-- Descripción: Esquema de Identity (ASP.NET Core Identity) + tabla de refresh
--              tokens para el flujo JWT. Las tablas usan los nombres canónicos
--              de Identity en snake_case porque el DbContext aplica
--              ToSnakeCase en todo el modelo.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- ASP.NET Core Identity tables (esquema generado por EF, traducido a snake_case)
-- ----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS asp_net_roles (
    id                  TEXT PRIMARY KEY,
    name                VARCHAR(256),
    normalized_name     VARCHAR(256),
    concurrency_stamp   TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS role_name_index ON asp_net_roles (normalized_name);

CREATE TABLE IF NOT EXISTS asp_net_users (
    id                      TEXT PRIMARY KEY,
    user_name               VARCHAR(256),
    normalized_user_name    VARCHAR(256),
    email                   VARCHAR(256),
    normalized_email        VARCHAR(256),
    email_confirmed         BOOLEAN NOT NULL DEFAULT FALSE,
    password_hash           TEXT,
    security_stamp          TEXT,
    concurrency_stamp       TEXT,
    phone_number            TEXT,
    phone_number_confirmed  BOOLEAN NOT NULL DEFAULT FALSE,
    two_factor_enabled      BOOLEAN NOT NULL DEFAULT FALSE,
    lockout_end             TIMESTAMPTZ,
    lockout_enabled         BOOLEAN NOT NULL DEFAULT FALSE,
    access_failed_count     INT NOT NULL DEFAULT 0,
    -- Campos extra de ApplicationUser
    display_name            VARCHAR(100),
    is_active               BOOLEAN NOT NULL DEFAULT TRUE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX IF NOT EXISTS user_name_index  ON asp_net_users (normalized_user_name);
CREATE INDEX IF NOT EXISTS        email_index      ON asp_net_users (normalized_email);

CREATE TABLE IF NOT EXISTS asp_net_role_claims (
    id           SERIAL PRIMARY KEY,
    role_id      TEXT NOT NULL REFERENCES asp_net_roles(id) ON DELETE CASCADE,
    claim_type   TEXT,
    claim_value  TEXT
);
CREATE INDEX IF NOT EXISTS ix_asp_net_role_claims_role_id ON asp_net_role_claims (role_id);

CREATE TABLE IF NOT EXISTS asp_net_user_claims (
    id           SERIAL PRIMARY KEY,
    user_id      TEXT NOT NULL REFERENCES asp_net_users(id) ON DELETE CASCADE,
    claim_type   TEXT,
    claim_value  TEXT
);
CREATE INDEX IF NOT EXISTS ix_asp_net_user_claims_user_id ON asp_net_user_claims (user_id);

CREATE TABLE IF NOT EXISTS asp_net_user_logins (
    login_provider          TEXT NOT NULL,
    provider_key            TEXT NOT NULL,
    provider_display_name   TEXT,
    user_id                 TEXT NOT NULL REFERENCES asp_net_users(id) ON DELETE CASCADE,
    PRIMARY KEY (login_provider, provider_key)
);
CREATE INDEX IF NOT EXISTS ix_asp_net_user_logins_user_id ON asp_net_user_logins (user_id);

CREATE TABLE IF NOT EXISTS asp_net_user_roles (
    user_id  TEXT NOT NULL REFERENCES asp_net_users(id) ON DELETE CASCADE,
    role_id  TEXT NOT NULL REFERENCES asp_net_roles(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, role_id)
);
CREATE INDEX IF NOT EXISTS ix_asp_net_user_roles_role_id ON asp_net_user_roles (role_id);

CREATE TABLE IF NOT EXISTS asp_net_user_tokens (
    user_id         TEXT NOT NULL REFERENCES asp_net_users(id) ON DELETE CASCADE,
    login_provider  TEXT NOT NULL,
    name            TEXT NOT NULL,
    value           TEXT,
    PRIMARY KEY (user_id, login_provider, name)
);

-- ----------------------------------------------------------------------------
-- Refresh tokens (gestionados manualmente — no usamos UserTokens nativa)
-- ----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS refresh_tokens (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id         TEXT NOT NULL REFERENCES asp_net_users(id) ON DELETE CASCADE,
    token_hash      VARCHAR(128) NOT NULL,        -- SHA-256 hex del token plano
    expires_at      TIMESTAMPTZ NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at      TIMESTAMPTZ,
    replaced_by_id  UUID REFERENCES refresh_tokens(id),
    ip_address      INET,
    user_agent      VARCHAR(500)
);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user_id    ON refresh_tokens (user_id);
CREATE UNIQUE INDEX IF NOT EXISTS ix_refresh_tokens_hash ON refresh_tokens (token_hash);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_expiry     ON refresh_tokens (expires_at)
    WHERE revoked_at IS NULL;

-- ----------------------------------------------------------------------------
-- Roles iniciales (alineados con políticas Authorize del API)
-- ----------------------------------------------------------------------------
INSERT INTO asp_net_roles (id, name, normalized_name, concurrency_stamp)
VALUES
    ('role-admin',       'Admin',        'ADMIN',        gen_random_uuid()::text),
    ('role-financeadmin','FinanceAdmin', 'FINANCEADMIN', gen_random_uuid()::text),
    ('role-reader',      'Reader',       'READER',       gen_random_uuid()::text)
ON CONFLICT (id) DO NOTHING;
