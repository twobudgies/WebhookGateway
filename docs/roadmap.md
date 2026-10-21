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
| P1-1 | Shared `Contracts` project: IDs (prefix + UUIDv7), event and delivery messages, enums | S | risky (contract) | P0-2 |
| P1-2 | Config schema migrations (`plans`, `tenants`, `api_keys`, `endpoints`, `subscriptions`, `usage_monthly`, ...) per `database.md`; seed plans | M | risky (schema); **needs owner's final review of `database.md`** | P0-4 |
| P1-3 | Ingest: auth (API key), envelope validation, enqueue to `events`, `202`; abstractions `IEventPublisher`, `IRateLimiter`, `IStateStore` | M | risky (auth) | P1-1, P1-2 |
| P1-4 | Rate limiting and monthly quota in Valkey (token bucket, pre-reservation, local fallback), idempotency keys, `429` responses. **Mostly done 2026-10-21 (D42):** rate limit, idempotency, monthly count, 429 rate_limited/quota_exceeded (quota enforcement off until the owner sets numbers); pending: token chunk reservation, flush to `usage_monthly` | L | | P1-3 |
| P1-5 | Fan-out: read `events`, match subscriptions (cached config), S3 archive batches, enqueue deliveries (tier queues) | L | | P1-1, P1-2 |
| P1-6 | Dispatcher: consume deliveries, HMAC signing (Standard Webhooks), async HTTP with timeout, SSRF protection, result handling | L | risky (security) | P1-5 |
| P1-7 | Retries and `delivery_state` in DynamoDB; retry scheduler for delays over 15 min; circuit breaker; Expired/Exhausted states; DLQ handling | L | | P1-6 |
| P1-8 | Delivery pacing (send slots) and max backlog / backlog age | M | | P1-5, P1-4 |
| P1-9 | Connection budgets: per-tenant/per-endpoint permits in Valkey with leases, per-task fair share | M | | P1-6 |
| P1-10 | Rollups: in-memory counters and histograms flushed to DynamoDB `rollups`; result logs to S3 | M | | P1-6 |
| P1-11 | Load-test harness (producers up to 30k events/s) and mock subscribers (fast, slow, failing): split into LT-2..LT-4 below | L | | P1-3, P1-6 |

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
| P3-3 | Test environment (minimum config, ~$35 to 40 per month) and deploy pipeline; includes ECS task definitions for the load generator and mock subscriber (desired count 0 when idle) | M | risky, aws | P3-2, LT-3, LT-4 |
| P3-4 | Load and performance tests on the test environment: run LT-6..LT-8, fix what they find, write `capacity.md` (LT-9) | L | aws | LT-1..LT-5, P3-3 |
| P3-5 | Production environment sized from `capacity.md`, autoscaling and alarms from the measured numbers, backups restore drill, runbooks, dashboards | L | risky, aws | P3-4, LT-9 |

## Phase 4: autonomy and scale

| ID | Work item | Size | Tags | Depends |
|---|---|---|---|---|
| P4-1 | Orchestrator (`webhookgateway-orchestrator`), per `orchestrator-spec.md`; owner approval of that spec needed first | L+ | risky | P0-3 |
| P4-2 | Stage 2 swaps from `architecture.md` section 10, each when its trigger fires | per item | risky | P3-4 |

## Phase 5: developer site, docs and purchase (added 2026-10-21, built last)

Owner request: one website where developers learn the product and where customers buy a plan. Done **after** everything else works, but tracked now. Lives in its own repo (`webhookgateway-site`, to create) like the portal (D27); the OpenAPI document published from this repo is its source of truth for the API reference, so docs cannot drift from the code (rule D-3).

| ID | Work item | Size | Notes | Depends |
|---|---|---|---|---|
| S-1 | **Site shell:** docs website with a left side menu, sub-menus, search, previous/next, version selector, dark/light theme, mobile layout, copy buttons on every code block; choose the framework (Docusaurus, Nextra or custom React; decision recorded when started) | M | | P2-4 |
| S-2 | **Getting started:** what the product is, create account, get an API key, send the first event in 5 minutes, receive it, verify the signature | M | | S-1, P2-1 |
| S-3 | **Concepts:** events, endpoints, subscriptions, deliveries, retries and retry windows per plan, idempotency, ordering and at-least-once, rate limits, payload size, replay, plans | M | derive from `architecture.md` and `tenancy-and-limits.md` | S-1 |
| S-4 | **API reference** generated from OpenAPI (`/v1/events`, `/portal/v1`), with request/response examples and every error code | M | needs P0-5 (OpenAPI generation) | S-1 |
| S-5 | **Sample code in every language, tested in CI:** C#, Java, PHP, Python, Node.js (JS/TS), Go, Ruby, plus plain cURL. Each shows: send an event, handle the `429`/`503` with backoff, use an idempotency key, and **verify the webhook signature on the receiving side** (Standard Webhooks) with a minimal receiver. Language tabs remember the reader's choice | L | each snippet is compiled/run in CI against the local stack so it never rots | S-2 |
| S-6 | **Interactive pieces:** "Try it" request console with the reader's test key, live signature verifier (paste a payload, secret and headers), event type / payload builder, delivery simulator that shows a retry timeline for the chosen plan, copy-paste snippets that fill in the reader's own key | L | uses a sandbox tenant, never production data | S-4 |
| S-7 | **Guides:** receiving webhooks safely (HTTPS, signature check, replay protection with the timestamp, fast 2xx, queue and process later), handling retries and duplicates, testing locally with a tunnel, going to production checklist, migration from other providers | M | | S-3 |
| S-8 | **Security & trust page (accurate!):** what is encrypted and where (see "Encryption claims" below), SSRF protection, signing, key handling, data residency (`ap-south-1`), retention, sub-processors, incident contact, status page | M | claims reviewed against the code before publishing | S-1 |
| S-9 | **Pricing page and purchase flow:** plan comparison from the `plans` data, sign up, pick a plan, pay, receive the API key; upgrade/downgrade/cancel; invoices and tax (GST) | L | needs the payment provider decision (Razorpay or Stripe, India-friendly) and the pricing numbers (D42); a billing/subscription service is its own work: webhooks from the provider to the ControlPlane to change plans | S-1, P2-3 |
| S-10 | **Self-service signup in ControlPlane** replacing operator-created tenants (v1 is operator-only, D46), email verification, abuse controls | L | risky (auth) | P2-3 |
| S-11 | **Changelog, status page, support/contact, API versioning and deprecation policy** | S | | S-1 |
| S-12 | **Docs quality gates in CI:** link check, snippets compile and run, OpenAPI diff fails the build if the reference is stale, spell check | M | | S-4, S-5 |
| S-13 | **Hosting:** CloudFront + S3 (as the portal), custom domain, TLS | S | needs the domain and the owner's AWS go-ahead | P3-1 |

### Encryption claims (what we may say, and what we must not)

Customers must be able to trust what the site says, so the page states only what the system does. Today and planned:

- **In transit:** HTTPS/TLS 1.2+ for the API and for every delivery to the customer's endpoint (https is required, D46). Inside AWS, calls to SQS, S3, DynamoDB and KMS use TLS.
- **At rest:** SQS (KMS), S3 archive (KMS), DynamoDB (AWS-managed or KMS), RDS PostgreSQL (encrypted volume), Valkey (encrypted at rest and in transit). Endpoint signing secrets use envelope encryption with a per-secret KMS data key (D45) and API keys are stored only as SHA-256 hashes.
- **Authenticity of deliveries:** every webhook is signed (HMAC, Standard Webhooks) so the receiver can prove it came from us and was not altered.
- **Not "end-to-end encrypted" in the strict sense:** the service must read an event to route and deliver it, so it is encrypted in transit and at rest but not hidden from the platform. We will **not** use the words "end-to-end encrypted" unless we build it.
- **Customer-held payload encryption (true end-to-end) is out of scope** (owner decision 2026-10-21, D48).

| ID | Work item | Size | Notes | Depends |
|---|---|---|---|---|
| S-15 | Review the encryption settings of every AWS resource in the infrastructure code (P3-1) against the claims on the security page; fail the build if a resource is unencrypted | S | | P3-1 |

## Cross-cutting: application logging (added 2026-10-21)

No component logs yet. Sink decided: CloudWatch Logs, 14 days (D32, `logging.md`). Rule from now on: **every new work item includes its logging** (see `CLAUDE.md`). Never log secrets, API keys, signing secrets or payload bodies; always carry `tenantId`, `eventId`, `deliveryId`, `endpointId`, `attempt` and a correlation id where known.

| ID | Item | Size | Notes | Depends on |
|---|---|---|---|---|
| L-1 | Logging standard: structured JSON via `ILogger`, field names, levels, correlation id (W3C trace id), redaction rules, sampling of success logs; document in `docs/logging.md` | S | done: `docs/logging.md`, D32 | |
| L-2 | Host setup shared by all services: JSON console formatter, enrich with service/version/environment, request logging middleware in Ingest and ControlPlane | M | done: `src/Hosting` `AddStructuredJsonConsole` in all four services (request logging is ASP.NET Core's built-in Information log) | L-1 |
| L-3 | **done 2026-10-21** Backfill Ingest (P1-3): auth failures, validation rejects, rate-limit/quota rejects, publish failures (5xx), accepted (sampled) | M | | L-2 |
| L-4 | **done** Backfill Fan-out (P1-5): batch received, no subscription match, archive write failure, delivery publish failure, ack failure, poison/deserialize failure | M | | L-2 |
| L-5 | **done** (SSRF/blocked address appears as `error` on the failure line) Backfill Dispatcher (P1-6, P1-7): attempt outcome with status/latency/error class, SSRF block, retry scheduled, exhausted/expired, circuit breaker open/close, signing failure | M | | L-2 |
| L-6 | Backfill Core rollups (P1-10): flush failures, dropped counters | S | **done:** the writer and `RollupFlusher` log failed rows and keep them for the next flush (D41) | | L-2 |
| L-7 | **done** Backfill SQS adapters (`src/Aws`): batch partial failure, receive/delete errors, queue URL resolution failure | S | | L-2 |
| L-8 | Log hygiene tests: no secrets/payloads in log output | S | **partly done:** Dispatcher failure line test (ids yes, payload no); still to add for Ingest and Fan-out | L-3..L-5 |
| L-11 | `component` field on every log line (Ingest, FanOut, Dispatcher, ControlPlane), existing services updated; flat JSON formatter | S | **done 2026-10-21** (D38) | L-2 |
| L-9 | Sink: ship logs to the chosen store, retention, a saved query set for debugging by eventId/deliveryId (cloud, needs owner go-ahead) | M | aws | P3-2, sink decision |
| L-10 | Local dev: logs readable in the console with the same fields | S | | L-2 |

## Cross-cutting: load and performance testing (added 2026-10-21)

Plan, scenarios, metrics and the sizing output are in `docs/load-testing.md`. Short version: seed realistic tenants end to end, run the load generator and the mock subscribers as ECS tasks in the test environment (laptop only for smoke), measure every component, and turn the results into a "workload to minimum resources" table (`capacity.md`) that drives production sizing and autoscaling.

| ID | Item | Size | Notes | Depends on |
|---|---|---|---|---|
| LT-1 | Plan: `docs/load-testing.md`, decision D35 | S | done 2026-10-21 | |
| LT-2 | Seed tool: creates tenants, endpoints, subscriptions, API keys from a profile via the ControlPlane API (plan mix, fan-out, mock-behavior mix, deterministic seed, tear-down) | M | run locally first, same tool in the test env | P2-1 (until then, a direct-to-DB seeder behind the same profile) |
| LT-3 | Load generator `tools/LoadGenerator`: open-loop constant-rate, step/ramp/spike profiles, event size and type mix, idempotency duplicates, metrics output, container image | M | decide .NET vs k6 in this item | P1-3 |
| LT-4 | Mock subscriber service: fast / slow / failing / timeout / 429 per endpoint, counts received deliveries per event id (lost and duplicate check), container image | M | | P1-6 |
| LT-5 | Metrics emission from the services (CloudWatch EMF or equivalent): per-component rates, latencies, queue ages, connection counts, limits hit; same names locally and in the cloud | M | pair with L-9 sink; no per-event metric cost surprises, aggregate in process | L-2, P1-10 |
| LT-6 | Local functional run: S1 end to end against Moto/Docker with seed + generator + mock subscribers; fix defects found (no capacity numbers) | M | | LT-2..LT-4 |
| LT-7 | Test-environment run set: S1, S2 (step load to the knee), S3 soak, S4 spike | L | aws; needs owner go-ahead for spend | P3-3, LT-6, LT-5 |
| LT-8 | Test-environment behavior runs: S5 slow consumers, S6 failing consumers, S7 noisy tenant, S8 large payloads, S9 failure injection, S10 drain; run report script (`docs/load-test-runs/`) | L | aws | LT-7 |
| LT-9 | **Capacity table and metrics:** `docs/capacity.md` with per-task sustainable rates, knees, connection limits, Valkey/DynamoDB/RDS numbers, cost per million events, and the sizing table (workload to minimum resources, 30% headroom, N+1 across two AZs); update `architecture.md` section 8/11 and `cost-model.md` with measured values | M | the standing reference for production sizing | LT-7, LT-8 |
| LT-10 | Turn capacity numbers into production settings: autoscaling targets (CPU, queue age), min/max tasks, alarm thresholds, platform-wide accept cap; re-run S2/S4 against them | M | risky (infra), aws | LT-9, P3-2 |
| LT-11 | Re-run policy: after any change to the hot path (Ingest, Fan-out, Dispatcher, limiter, adapters) or each quarter, rerun S2 and compare to the last run in `docs/load-test-runs/`; flag regressions above 15% | rule | | LT-9 |

## Cross-cutting: documentation upkeep (added 2026-10-21)

Goal: the architect can understand the whole flow from `docs/` and ask for changes at any point.

| ID | Item | Size | Notes | Depends on |
|---|---|---|---|---|
| D-1 | `docs/flow-walkthrough.md`: one event end to end, code pointers, built vs. not built | S | done 2026-10-21 | |
| D-2 | **Definition of done for every item:** docs it affects are updated in the same PR (walkthrough "Built?" column, contracts, data-plane, logging, worklog) | rule | in `CLAUDE.md`; applies from now on | |
| D-3 | **Docs audit after each merged task:** compare the changed code to `flow-walkthrough.md` and the other docs; if anything drifted, open a docs-only PR, verify against the code, merge when CI is green | rule | agent does this automatically | D-2 |
| D-4 | Backfill: audit existing docs (`architecture`, `contracts`, `data-plane`, `tenancy-and-limits`, `portal`, `database`) against the implemented code and fix drift | M | | D-1 |

## Order and open owner items

Start with P0-2, P0-4 and P1-1 (no cost, no approval needed), then P1-2 once the owner has done the final review of `database.md`. The plan numbers in `tenancy-and-limits.md` are provisional but only needed at seed time (P1-2), so they do not block the first items. Nothing in Phase 3 starts without the owner's go-ahead.
