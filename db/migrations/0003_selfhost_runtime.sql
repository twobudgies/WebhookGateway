-- Runtime tables for the self-host backend (Backend:Mode=Postgres, D53): queues, delivery state, rollups, event
-- archive, idempotency keys and monthly counters. The AWS backend uses SQS, DynamoDB and S3 instead and ignores these.

-- A durable queue: messages become visible at visible_at, are leased by pushing visible_at forward, and move to
-- "<queue>-dlq" after too many receives (the same contract as SQS standard queues).
CREATE TABLE webhook.queue_messages (
    id            bigserial PRIMARY KEY,
    queue         text        NOT NULL,
    body          text        NOT NULL,
    enqueued_at   timestamptz NOT NULL DEFAULT now(),
    visible_at    timestamptz NOT NULL DEFAULT now(),
    receive_count integer     NOT NULL DEFAULT 0,
    receipt       uuid
);
CREATE INDEX queue_messages_ready_idx ON webhook.queue_messages (queue, visible_at, id);

-- Non-success deliveries only (docs/data-plane.md section 4).
CREATE TABLE webhook.delivery_state (
    tenant_id        text        NOT NULL,
    delivery_id      text        NOT NULL,
    endpoint_id      text        NOT NULL,
    event_id         text        NOT NULL,
    event_type       text        NOT NULL,
    occurred_at      timestamptz NOT NULL,
    status           text        NOT NULL,
    attempt          integer     NOT NULL,
    tier             text        NOT NULL,
    next_attempt_at  timestamptz,
    last_status_code integer,
    last_error       text,
    payload          text,
    payload_ref      text,
    created_at       timestamptz NOT NULL,
    updated_at       timestamptz NOT NULL,
    expires_at       timestamptz NOT NULL,
    PRIMARY KEY (tenant_id, delivery_id)
);
CREATE INDEX delivery_state_due_idx ON webhook.delivery_state (next_attempt_at) WHERE status = 'Scheduled';
CREATE INDEX delivery_state_tenant_status_idx ON webhook.delivery_state (tenant_id, status, updated_at DESC);
CREATE INDEX delivery_state_endpoint_idx ON webhook.delivery_state (tenant_id, endpoint_id, updated_at DESC);
CREATE INDEX delivery_state_expiry_idx ON webhook.delivery_state (expires_at);

-- Per tenant/endpoint and minute counters and latency histograms, added to with ON CONFLICT (docs/data-plane.md).
CREATE TABLE webhook.rollups (
    pk          text        NOT NULL,
    sk          text        NOT NULL,
    expires_at  timestamptz NOT NULL,
    created bigint NOT NULL DEFAULT 0,
    attempts bigint NOT NULL DEFAULT 0,
    succeeded bigint NOT NULL DEFAULT 0,
    retried bigint NOT NULL DEFAULT 0,
    failed bigint NOT NULL DEFAULT 0,
    exhausted bigint NOT NULL DEFAULT 0,
    timeouts bigint NOT NULL DEFAULT 0,
    connection_ms bigint NOT NULL DEFAULT 0,
    rt_sum bigint NOT NULL DEFAULT 0,
    qw_sum bigint NOT NULL DEFAULT 0,
    rt0 bigint NOT NULL DEFAULT 0,
    rt1 bigint NOT NULL DEFAULT 0,
    rt2 bigint NOT NULL DEFAULT 0,
    rt3 bigint NOT NULL DEFAULT 0,
    rt4 bigint NOT NULL DEFAULT 0,
    rt5 bigint NOT NULL DEFAULT 0,
    rt6 bigint NOT NULL DEFAULT 0,
    rt7 bigint NOT NULL DEFAULT 0,
    rt8 bigint NOT NULL DEFAULT 0,
    rt9 bigint NOT NULL DEFAULT 0,
    rt10 bigint NOT NULL DEFAULT 0,
    rt11 bigint NOT NULL DEFAULT 0,
    rt12 bigint NOT NULL DEFAULT 0,
    qw0 bigint NOT NULL DEFAULT 0,
    qw1 bigint NOT NULL DEFAULT 0,
    qw2 bigint NOT NULL DEFAULT 0,
    qw3 bigint NOT NULL DEFAULT 0,
    qw4 bigint NOT NULL DEFAULT 0,
    qw5 bigint NOT NULL DEFAULT 0,
    qw6 bigint NOT NULL DEFAULT 0,
    qw7 bigint NOT NULL DEFAULT 0,
    qw8 bigint NOT NULL DEFAULT 0,
    qw9 bigint NOT NULL DEFAULT 0,
    qw10 bigint NOT NULL DEFAULT 0,
    qw11 bigint NOT NULL DEFAULT 0,
    qw12 bigint NOT NULL DEFAULT 0,
    PRIMARY KEY (pk, sk)
);
CREATE INDEX rollups_expiry_idx ON webhook.rollups (expires_at);

-- Event archive: one gzip JSON Lines batch per row (the replay source and home of large payloads).
CREATE TABLE webhook.archive_batches (
    key         text        PRIMARY KEY,
    body        bytea       NOT NULL,
    event_count integer     NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    expires_at  timestamptz NOT NULL
);
CREATE INDEX archive_batches_expiry_idx ON webhook.archive_batches (expires_at);

CREATE TABLE webhook.idempotency_keys (
    tenant_id   text        NOT NULL,
    key         text        NOT NULL,
    event_id    text        NOT NULL,
    expires_at  timestamptz NOT NULL,
    PRIMARY KEY (tenant_id, key)
);
CREATE INDEX idempotency_keys_expiry_idx ON webhook.idempotency_keys (expires_at);

CREATE TABLE webhook.usage_counters (
    tenant_id  text   NOT NULL,
    month      text   NOT NULL,
    accepted   bigint NOT NULL DEFAULT 0,
    PRIMARY KEY (tenant_id, month)
);
