-- Configuration database (docs/database.md). Forward-only; applied by the migration job, never by app startup.
CREATE SCHEMA IF NOT EXISTS webhook;

CREATE TABLE webhook.plans (
    code                        text PRIMARY KEY,
    name                        text        NOT NULL,
    queue_tier                  text        NOT NULL CHECK (queue_tier IN ('basic', 'advanced', 'premium')),
    accept_rate                 integer     NOT NULL CHECK (accept_rate > 0),
    burst_capacity              integer     NOT NULL CHECK (burst_capacity > 0),
    monthly_quota               bigint      NOT NULL CHECK (monthly_quota > 0),
    delivery_rate               integer     NOT NULL CHECK (delivery_rate > 0),
    max_connections             integer     NOT NULL CHECK (max_connections > 0),
    max_connections_per_endpoint integer    NOT NULL CHECK (max_connections_per_endpoint > 0),
    max_backlog                 bigint      NOT NULL CHECK (max_backlog > 0),
    max_backlog_age             interval    NOT NULL,
    retry_window                interval    NOT NULL,
    max_endpoints               integer     NOT NULL CHECK (max_endpoints > 0),
    per_endpoint_rate           integer     NOT NULL CHECK (per_endpoint_rate > 0),
    max_payload_bytes           integer     NOT NULL CHECK (max_payload_bytes > 0),
    retention_days              integer     NOT NULL CHECK (retention_days > 0),
    idempotency_window          interval    NOT NULL
);

CREATE TABLE webhook.tenants (
    id           uuid PRIMARY KEY,
    name         text        NOT NULL,
    status       text        NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'suspended', 'closed')),
    plan_code    text        NOT NULL REFERENCES webhook.plans (code),
    home_region  text        NOT NULL DEFAULT 'ap-south-1',
    created_at   timestamptz NOT NULL DEFAULT now()
);

-- A per-tenant exception to one plan value; field is a plans column name.
CREATE TABLE webhook.tenant_overrides (
    tenant_id  uuid NOT NULL REFERENCES webhook.tenants (id) ON DELETE CASCADE,
    field      text NOT NULL,
    value      text NOT NULL,
    PRIMARY KEY (tenant_id, field)
);

CREATE TABLE webhook.api_keys (
    id            uuid PRIMARY KEY,
    tenant_id     uuid        NOT NULL REFERENCES webhook.tenants (id) ON DELETE CASCADE,
    key_hash      text        NOT NULL UNIQUE,
    prefix        text        NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    revoked_at    timestamptz,
    last_used_at  timestamptz
);
CREATE INDEX api_keys_tenant_idx ON webhook.api_keys (tenant_id);

CREATE TABLE webhook.tenant_members (
    tenant_id  uuid NOT NULL REFERENCES webhook.tenants (id) ON DELETE CASCADE,
    user_sub   text NOT NULL,
    role       text NOT NULL CHECK (role IN ('owner', 'developer', 'viewer')),
    PRIMARY KEY (tenant_id, user_sub)
);

CREATE TABLE webhook.endpoints (
    id                    uuid PRIMARY KEY,
    tenant_id             uuid        NOT NULL REFERENCES webhook.tenants (id) ON DELETE CASCADE,
    url                   text        NOT NULL,
    status                text        NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'disabled')),
    auth_mode             text        NOT NULL DEFAULT 'hmac',
    delivery_rate_limit   integer,
    connection_limit      integer,
    secret_ciphertext     bytea       NOT NULL,
    secret_kms_key_id     text        NOT NULL,
    created_at            timestamptz NOT NULL DEFAULT now(),
    updated_at            timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX endpoints_tenant_idx ON webhook.endpoints (tenant_id);

CREATE TABLE webhook.subscriptions (
    id           uuid PRIMARY KEY,
    tenant_id    uuid NOT NULL REFERENCES webhook.tenants (id) ON DELETE CASCADE,
    endpoint_id  uuid NOT NULL REFERENCES webhook.endpoints (id) ON DELETE CASCADE,
    event_type   text NOT NULL,
    UNIQUE (endpoint_id, event_type)
);
CREATE INDEX subscriptions_tenant_type_idx ON webhook.subscriptions (tenant_id, event_type);

CREATE TABLE webhook.usage_monthly (
    tenant_id         uuid NOT NULL REFERENCES webhook.tenants (id) ON DELETE CASCADE,
    month             date NOT NULL,
    accepted          bigint NOT NULL DEFAULT 0,
    rejected_rate     bigint NOT NULL DEFAULT 0,
    rejected_quota    bigint NOT NULL DEFAULT 0,
    rejected_backlog  bigint NOT NULL DEFAULT 0,
    PRIMARY KEY (tenant_id, month)
);
