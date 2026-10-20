# Database (PostgreSQL)

Status: **IN REVIEW. Not final.** **Scope change proposed (2026-10-20, architecture v3):** PostgreSQL would hold *configuration only* (tenants, plans, overrides, api_keys, members, endpoints, subscriptions, usage_monthly). The `events`, `deliveries`, `delivery_attempts`, `outbox` and `audit_log` tables below move to the data plane (`data-plane.md`) if the owner approves. Until then, nothing here is built. Do not build features that depend on details marked *(review)* without checking with the user. Once approved, change this line to `Final v1` and log it in `decisions.md`.

## Engine

Amazon **Aurora PostgreSQL** (or RDS PostgreSQL for dev), PostgreSQL 16+ . Accessed from Fargate via private subnets; credentials from Secrets Manager (IAM auth *(review)*).
Replaces the earlier DynamoDB plan (see D10).

## Ownership and migrations

- One database, one schema `webhook`. Both Ingest and Dispatcher connect to it *(review: shared DB is simple but couples the repos; the contract is this document)*.
- **Schema and migrations live in this repo**: `db/migrations/NNNN_description.sql` (forward-only, plain SQL). Applied by a migration job in the deploy pipeline, never by app startup.
- Table ownership (who writes) is listed below. A repo must not write to tables it does not own, except where stated.
- Data access in services: Npgsql (+ Dapper) *(review: vs EF Core)*. No ORM-generated schema.

## Tables (draft)

| Table | Purpose | Writer |
|---|---|---|
| `tenants` | id, name, status, limits | Ingest (mgmt) |
| `api_keys` | tenant_id, key_hash, prefix, revoked_at | Ingest (mgmt) |
| `endpoints` | id, tenant_id, url, status, auth_mode (`none`/`oauth2`), oauth2 token_url/client_id, secret refs (Secrets Manager ARNs), created_at | Ingest (mgmt) |
| `subscriptions` | tenant_id, endpoint_id, event_type (`*` allowed), unique(endpoint_id, event_type) | Ingest (mgmt) |
| `events` | id, tenant_id, type, occurred_at, payload jsonb, idempotency_key, created_at; unique(tenant_id, idempotency_key) | Ingest |
| `deliveries` | id, tenant_id, event_id, endpoint_id, status, attempt_count, next_attempt_at, last_error, updated_at | Ingest inserts, Dispatcher updates |
| `delivery_attempts` | id, delivery_id, attempt, started_at, status_code, latency_ms, outcome, error | Dispatcher |
| `outbox` | id, delivery_id, payload, created_at, published_at | Ingest writes, relay marks published |
| `audit_log` *(review: or S3 only)* | audit records, see contracts | Dispatcher |

Notes:
- IDs: prefixed ULIDs/UUIDv7 as text or uuid *(review)*.
- `events`, `delivery_attempts`, `audit_log` are time-partitioned by month (`created_at`) with retention by dropping partitions (default 30d events, 90d attempts) *(review)*.
- Indexes at minimum: `deliveries(status, next_attempt_at)`, `deliveries(tenant_id, event_id)`, `subscriptions(tenant_id, event_type)`, `endpoints(tenant_id)`.
- Row-level tenant filtering is enforced in code on every query (`tenant_id` required); RLS *(review)*.

## Consistency rules

- **Transactional outbox.** Ingest inserts the event, deliveries and outbox rows in one transaction. A relay (in Ingest) publishes outbox rows to SQS and sets `published_at`. This removes the "saved but never queued" failure.
- Dispatcher updates deliveries with guarded statements, e.g. `UPDATE deliveries SET status=... WHERE id=@id AND status='Pending' AND attempt_count=@attempt`, so duplicate SQS delivery is harmless.
- Connection pooling: bounded pool per task (RDS Proxy *(review)*) to protect the database from Fargate scale-out.
