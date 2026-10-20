# Database (PostgreSQL)

Status: **IN REVIEW. Updated for architecture v4 (2026-10-20); awaiting the owner's final review before anything is built.** Once approved, change this line to `Final v1` and log it in `decisions.md`.

## Scope (changed by D21)

PostgreSQL holds **configuration only**: low-volume, relational data that is read through caches. It is **not** in the per-event path. Events, deliveries, attempts and rollups live in SQS / DynamoDB / S3 (`data-plane.md`). The earlier tables `events`, `deliveries`, `delivery_attempts`, `outbox` and `audit_log` are removed from this design.

## Engine

**RDS PostgreSQL 16+**, db.t4g class, Multi-AZ for production, from Fargate in the VPC; credentials from Secrets Manager (IAM auth *(review)*). Moves to Aurora PostgreSQL when load or availability needs it; same engine, no schema change. Replaces the earlier DynamoDB plan only for configuration (D10, D21).

## Ownership and migrations

- One database, one schema `webhook`. **ControlPlane is the only writer** of configuration tables; Ingest, Fan-out and Dispatcher read through an in-memory cache (TTL 30 s, invalidated on change). `usage_monthly` is written by the usage flusher (Ingest).
- **Schema and migrations live in this repo**: `db/migrations/NNNN_description.sql` (forward-only, plain SQL, expand then contract). Applied by a migration job in the deploy pipeline, never by app startup. Migrations are tagged `risky` for review (see `workflow.md`).
- Data access: Npgsql (+ Dapper) *(review: vs EF Core)*. No ORM-generated schema.

## Tables

Full column-level model in `tenancy-and-limits.md` section 6.

| Table | Purpose |
|---|---|
| `plans` | Plan limits as data: accept rate, burst, monthly quota, delivery rate, connection limits, backlog caps, endpoint cap, retention, idempotency window, queue tier |
| `tenants` | id, name, status, `plan_code`, `home_region` |
| `tenant_overrides` | per-tenant exceptions to plan values |
| `api_keys` | tenant_id, key hash, prefix, revoked_at, last_used_at |
| `tenant_members` | portal users mapped to a tenant and role (identity lives in Cognito) |
| `endpoints` | id, tenant_id, url, status, auth mode, per-endpoint rate and connection limits, encrypted signing secret + KMS key id (D26) |
| `subscriptions` | tenant_id, endpoint_id, event type (`*` allowed), unique(endpoint_id, event_type) |
| `usage_monthly` | tenant_id, month, accepted, rejected_rate, rejected_quota, rejected_backlog |

Notes:
- IDs: prefixed ULIDs as text *(review)*.
- Indexes at minimum: `subscriptions(tenant_id, event_type)`, `endpoints(tenant_id)`, `api_keys(key_hash)`.
- Every query filters on `tenant_id`; RLS *(review)*.
- Backups: automated, point-in-time recovery; restore tested before first customer.

## Consistency rules

- Config changes publish an invalidation (SNS or a version number in Valkey) so caches refresh within seconds.
- Endpoint secrets are encrypted at the application layer with a KMS data key (envelope encryption); only ciphertext is stored.
- Connection pooling: a small bounded pool per task (RDS Proxy *(review)*, probably not needed at launch because of the caches).
