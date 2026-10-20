# Roadmap and work items

Status: **v1 (2026-10-20)**, derived from the approved architecture v4 (D20-D28). The owner reviews the list; after that, agents work through it under `workflow.md`. Tags: `risky` = schema, auth, infra, contract or security change, needs the owner's merge; `aws` = touches AWS, needs the owner's go-ahead per `workflow.md`. Sizes: S (hours), M (1 to 2 days), L (3 to 5 days).

## Phase 0: foundations (no cloud cost)

| ID | Work item | Size | Tags | Depends |
|---|---|---|---|---|
| P0-1 | Verify AWS ap-south-1 prices and build the cost spreadsheet (replaces estimates in `cost-model.md`) | M | | |
| P0-2 | Solution scaffold in the monorepo: `src/{Contracts,Ingest,FanOut,Dispatcher,ControlPlane}`, `tests/`, `Directory.Build.props` (.NET 10, nullable, warnings as errors), `.editorconfig` | M | | |
| P0-3 | `Makefile` with `verify` and `verify-fast`; GitHub Actions: build, test, format check | M | risky (CI) | P0-2 |
| P0-4 | Local dev environment: docker-compose with PostgreSQL, LocalStack (SQS, DynamoDB, S3, KMS), Valkey; run script | M | | P0-2 |
| P0-5 | Create repo `webhookgateway-portal` (empty) and OpenAPI generation setup in this repo | S | | P0-2 |

## Phase 1: core pipeline (local, tested against LocalStack and Testcontainers)

| ID | Work item | Size | Tags | Depends |
|---|---|---|---|---|
| P1-1 | Shared `Contracts` project: IDs (ULID), event and delivery messages, enums | S | risky (contract) | P0-2 |
| P1-2 | Config schema migrations (`plans`, `tenants`, `api_keys`, `endpoints`, `subscriptions`, `usage_monthly`, ...) per `database.md`; seed plans | M | risky (schema); **needs owner's final review of `database.md`** | P0-4 |
| P1-3 | Ingest: auth (API key), envelope validation, enqueue to `events`, `202`; abstractions `IEventQueue`, `IRateLimiter`, `IStateStore` | M | risky (auth) | P1-1, P1-2 |
| P1-4 | Rate limiting and monthly quota in Valkey (token bucket, pre-reservation, local fallback), idempotency keys, `429` responses | L | | P1-3 |
| P1-5 | Fan-out: read `events`, match subscriptions (cached config), S3 archive batches, enqueue deliveries (tier queues) | L | | P1-1, P1-2 |
| P1-6 | Dispatcher: consume deliveries, HMAC signing (Standard Webhooks), async HTTP with timeout, SSRF protection, result handling | L | risky (security) | P1-5 |
| P1-7 | Retries and `delivery_state` in DynamoDB; retry scheduler for delays over 15 min; circuit breaker; Expired/Exhausted states; DLQ handling | L | | P1-6 |
| P1-8 | Delivery pacing (send slots) and max backlog / backlog age | M | | P1-5, P1-4 |
| P1-9 | Connection budgets: per-tenant/per-endpoint permits in Valkey with leases, per-task fair share | M | | P1-6 |
| P1-10 | Rollups: in-memory counters and histograms flushed to DynamoDB `rollups`; result logs to S3 | M | | P1-6 |
| P1-11 | Load-test harness (producers up to 30k events/s) and mock subscribers (fast, slow, failing) | L | | P1-3, P1-6 |

## Phase 2: control plane and portal

| ID | Work item | Size | Tags | Depends |
|---|---|---|---|---|
| P2-1 | ControlPlane API: tenants, plans, API keys, endpoints, subscriptions; config cache invalidation; envelope-encrypted secrets | L | risky (auth, secrets) | P1-2 |
| P2-2 | Portal read API (`/portal/v1`): overview, endpoints, failures, event lookup, usage; replay | L | | P1-7, P1-10, P2-1 |
| P2-3 | Cognito integration and tenant-scoped authorization | M | risky (auth) | P2-1 |
| P2-4 | Portal UI (repo `webhookgateway-portal`): Overview, Endpoints (slow consumers, connections), Failures + replay | L | | P2-2, P2-3 |
| P2-5 | Event lookup, API keys, usage and limits, alerts screens | L | | P2-4 |

## Phase 3: cloud (needs the owner's go-ahead before any AWS change)

| ID | Work item | Size | Tags | Depends |
|---|---|---|---|---|
| P3-1 | CDK stacks: network (public subnets, no NAT), SQS, DynamoDB, S3, KMS, RDS, Valkey | L | risky, aws | P1-* |
| P3-2 | CDK: ECS services (Ingest, worker, ControlPlane), ALB, autoscaling on CPU and queue age, alarms | L | risky, aws | P3-1 |
| P3-3 | Test environment (minimum config, ~$35 to 40 per month) and deploy pipeline | M | risky, aws | P3-2 |
| P3-4 | Load test on the test environment (short runs), replace capacity assumptions with measurements | M | aws | P1-11, P3-3 |
| P3-5 | Production environment, backups restore drill, runbooks, dashboards | L | risky, aws | P3-4 |

## Phase 4: autonomy and scale

| ID | Work item | Size | Tags | Depends |
|---|---|---|---|---|
| P4-1 | Orchestrator (`webhookgateway-orchestrator`), per `orchestrator-spec.md`; owner approval of that spec needed first | L+ | risky | P0-3 |
| P4-2 | Stage 2 swaps from `architecture.md` section 10, each when its trigger fires | per item | risky | P3-4 |

## Order and open owner items

Start with P0-2, P0-4 and P1-1 (no cost, no approval needed), then P1-2 once the owner has done the final review of `database.md`. The plan numbers in `tenancy-and-limits.md` are provisional but only needed at seed time (P1-2), so they do not block the first items. Nothing in Phase 3 starts without the owner's go-ahead.
