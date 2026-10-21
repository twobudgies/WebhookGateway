# Agent work log

Resume point for agents after a sudden stop, crash, or new session. **Read this first, then the other Global docs.**

## How to use

- Before starting any piece of work, add an entry with **Status: in progress**: why, end goal, plan, files touched.
- Update the entry as steps finish. Mark each step done as it happens, not at the end.
- When done, set **Status: done** and note the outcome. If blocked or waiting on the user, set **blocked** and say what is needed.
- Newest entry on top. Keep entries short; link to PRs/decisions instead of copying them.
- Entry template:

```
### <date> <short title>
- **Status:** in progress | blocked | done
- **Why:** what prompted it (user request, bug, gap)
- **End goal:** what "finished" looks like
- **Plan / steps:** [ ] step ... (tick as done)
- **Touched:** repos, files, branches, PRs
- **Resume from:** the exact next action if work stops now
```

---

## Entries

### 2026-10-21 Load and performance test plan (LT-1..LT-11)
- **Status:** plan done (docs PR); implementation not started
- **Why:** Owner wants end-to-end load/perf tests on the test env (generator and consumer on ECS or from the laptop, seeded tenants), and metrics saying the minimum production resources per workload, tracked in the todo items.
- **Done:** `docs/load-testing.md`, roadmap section LT-1..LT-11 (P1-11, P3-3, P3-4, P3-5 updated), D35, CLAUDE.md index.
- **Resume from:** LT-3/LT-4 (generator, mock subscriber) as the pipeline items land; LT-2 seed tool after ControlPlane (P2-1) or via a direct-to-DB seeder. Nothing in AWS starts without the owner's go-ahead.

### 2026-10-20 Architecture v4 approved; roadmap created
- **Status:** done (approved 2026-10-20: owner approved all 18 decisions; PR #6 merged)
- **Why / goal:** Owner wanted the architecture finalized (cost-first, SQS, scale path to 30k events/s, portal) before any code.
- **Outcome:** `architecture.md` v4 approved; decisions D20-D28 logged; `contracts.md` v2; `database.md` rewritten config-only (still **in review**); `docs/roadmap.md` lists the work items; plan numbers still provisional.
- **Resume from:** see the next entry (roadmap execution).

### 2026-10-21 Launch readiness (Phase L)
- **Status:** in progress (owner items waiting)
- **Why:** owner plans to release on the web soon: no registered organization, worldwide audience, first customer in 1 to 3 months.
- **Done now:** roadmap Phase L (L-L1..L-L11), D50, D49 updated.
- **Waiting on the owner (long lead time, start now):** business registration and bank account (L-L1), AWS account in the business name (L-L4), domain and email (L-L5), legal pages review (L-L3), payment provider that accepts a new sole proprietor (L-L2).
- **Agent next:** CDK/infrastructure code and staging (L-L6) as soon as the AWS account exists, alarms and backups (L-L7), load test (L-L8), launch-lite site (L-L9); secret rotation and `usage_monthly` flusher in the gaps.

### 2026-10-21 Developer site and purchase flow (planned, built last)
- **Status:** planned (todo only), D47
- **Why:** Owner wants a docs website (side menu, sub-menus, interactive, samples in C#/Java/PHP/Python/others, getting-started, security page) where customers also buy a plan; wants customers to trust the encryption.
- **Done now:** roadmap Phase 5 (S-1..S-13, S-15) and the "Encryption claims" section; D47. Owner skipped end-to-end payload encryption (S-14 removed, D48).
- **Resume from:** nothing to do until the product is feature-complete; start with S-1/S-2. Decisions needed then: framework, payment provider, prices, domain.

### 2026-10-21 Container images and configuration reference
- **Status:** PR open
- **Outcome:** one parameterised `Dockerfile` (non-root, port 8080), `make images`, `docs/configuration.md` (all keys, IAM needs). Ingest image verified (`/healthz` 200) and Dispatcher image starts with structured logs.
- **Needs the owner before any further deployment work:** AWS account/profile and go-ahead for P3 (nothing in AWS has been touched), a domain name for the API, the plan quota numbers, who holds the operator admin key, first customer / launch date.
- **Resume from:** CDK stacks in an infra project (written but not deployed until the go-ahead), staging load test, `usage_monthly` flusher, endpoint secret rotation.

### 2026-10-21 End-to-end pipeline test
- **Status:** PR open
- **Outcome:** `PipelineEndToEndTests` proves Ingest -> SQS -> Fan-out (S3, Postgres routing) -> SQS -> Dispatcher (endpoint + decrypted secret) with real adapters on the local stack. The Moto-queue tests share one xunit collection so they do not race on queue names.
- **Resume from:** deployment prep: Dockerfiles for the four services, health/readiness, config via environment, CI image build; CDK stacks (P3) need the owner's AWS account decisions and go-ahead. Also: staging load test (LT items), `usage_monthly` flusher, endpoint secret rotation, portal.

### 2026-10-21 ControlPlane admin API (P2-1)
- **Status:** PR open
- **Outcome:** `/admin/v1` (tenants, API keys, endpoints, subscriptions), EF Core on the existing tables, admin key auth, plan caps, SSRF URL check, one-time secret/key display, logs for every change; `docs/admin-api.md`; 8 integration tests on real Postgres. D46.
- **Now possible end to end (locally):** create tenant + key + endpoint through the API, POST an event to Ingest, Fan-out routes it, Dispatcher delivers it (Moto + Postgres + Valkey).
- **Resume from:** an end-to-end smoke script/test across the three services; then deployment prep (Dockerfiles, CDK; needs the owner's AWS go-ahead); portal read API (P2-2).

### 2026-10-21 Endpoint secrets and endpoint store
- **Status:** PR open
- **Outcome:** `ISecretProtector` (KMS envelope + local dev), `PostgresEndpointStore`, Dispatcher wiring, tests (unit, KMS on Moto, Postgres). D45.
- **Resume from:** ControlPlane admin API (tenants, API keys, endpoints with secret generation, subscriptions) using EF Core (D43); then the portal read API.

### 2026-10-21 Config database (P1-2) - PR open, waiting for the owner's review
- **Status:** done: approved by the owner and merged (D44)
- **Outcome:** `db/migrations/0001..0002`, `src/Postgres` (MigrationRunner, PostgresApiKeyStore, PostgresSubscriptionStore), `src/Migrator`, wiring when `Postgres:ConnectionString` is set, `dev-up.sh` runs migrations, tests against local Postgres. D43 resolves the open review items.
- **Do not merge until the owner approves.** After approval set `database.md` to `Final v1` and log it.
- **Resume from:** ControlPlane API (P2-1) with KMS envelope encryption for endpoint secrets, `PostgresEndpointStore`, then the portal read API; meanwhile P1-8/P1-9 pacing and connection permits.

### 2026-10-21 Valkey accept limits (P1-4)
- **Status:** PR open
- **Outcome:** `src/Valkey` (token bucket Lua, idempotency, monthly counter), decorators with fallback in Ingest, endpoint order count -> claim -> publish with rollback on failure, tests (Valkey integration, degraded mode, endpoint). D42 holds the reasons and what is deliberately not done.
- **For the owner:** monthly quota numbers (Basic/Advanced/Premium) are placeholders; enforcement is off until you set them.
- **Resume from:** per-delivery pacing and connection permits (P1-8, P1-9) in Valkey, circuit breaker; config stores after the `database.md` review.

### 2026-10-21 Rollup counting and flushing (P1-10 part)
- **Status:** PR open
- **Outcome:** processors feed the accumulator, `RollupFlusher` (Hosting) writes every 10 s, failed rows carried over, wired in Aws mode for Fan-out and Dispatcher; D41.
- **Not built:** S3 result logs (per attempt, batched), hourly/daily compaction job.
- **Resume from:** Valkey limiter/idempotency/quota (P1-4), pacing (P1-8), circuit breaker; config stores after `database.md` review.

### 2026-10-21 Retry scheduler
- **Status:** PR open
- **Outcome:** `RetryScheduler` (poll the due index, publish, flip row), `IDueDeliveryIndex`, DynamoDB gsi3 query, `Tier` on `DeliveryStateRecord`, wired in Aws mode; unit + Moto tests. D40 has the reasons.
- **Resume from:** rollup flush loop (P1-10), Valkey limiter/idempotency/quota (P1-4), circuit breaker / parking; config database stores after the owner reviews `database.md`.

### 2026-10-21 Host wiring
- **Status:** PR open
- **Outcome:** `Backend:Mode=Aws` switch (D39) with `AddAwsIngest/FanOut/Dispatcher`; Dispatcher host now runs the delivery consumer in Aws mode; startup log line states the mode; Moto integration test proves Ingest to the `events` queue.
- **Gap for launch:** subscriptions and endpoints still in-memory. Next: config database stores (needs the owner's final review of `database.md`, gates P1-2), then ControlPlane (P2-1) so tenants can create endpoints.
- **Resume from:** retry scheduler loop (gsi3), rollup flush loop, Valkey (P1-4); in parallel remind the owner about `database.md`.

### 2026-10-21 Abstractions project, component field in logs
- **Status:** PR open
- **Why:** hosts could not reference `src/Aws` (circular), needed before wiring; owner asked for a `component` in every log line.
- **Outcome:** `src/Abstractions` (D37); `ComponentJsonFormatter` in `src/Hosting` with `component` on all lines of all four services (D38), tested; `logging.md`, roadmap L-11 updated.
- **Resume from:** host wiring (DI) with AWS vs in-memory config; then retry scheduler loop (gsi3), rollup flush loop, Valkey (P1-4).

### 2026-10-21 Dispatcher consumer
- **Status:** PR open
- **Outcome:** `DeliveryConsumer` (loop per tier, bounded concurrency), `IDeliverySource`, `SqsDeliverySource`, `InMemoryDeliverySource`, `IDeliveryProcessor`; logs; unit tests and a Moto round trip. D36 has the reasons.
- **Resume from:** host wiring (DI) with AWS vs in-memory config for Ingest, Fan-out, Dispatcher; then retry scheduler loop (gsi3), rollup flush loop, Valkey (P1-4).

### 2026-10-21 DynamoDB adapters
- **Status:** PR open
- **Outcome:** `DynamoDbDeliveryStateStore` (gsi1/2/3 attributes, TTL) and `DynamoDbRollupWriter` (atomic ADD) in `src/Aws/DynamoDb`, with logs and Moto tests. Decision D34 holds the reasons.
- **Resume from:** SQS delivery source + worker loop for Dispatcher, retry scheduler loop over gsi3, rollup flush loop, then host wiring (DI) with config for AWS vs in-memory, then Valkey (P1-4).

### 2026-10-21 S3 archive adapter
- **Status:** PR open
- **Outcome:** `S3EventArchive` (gzip JSONL per batch, KMS optional, `payloadRef` `s3://bucket/key#line`) and `S3PayloadResolver` (Dispatcher side) in `src/Aws/S3`, with logs (no payloads) and Moto tests. D33 records gzip instead of zstd. `data-plane.md` and the walkthrough updated.
- **Resume from:** DynamoDB `IDeliveryStateStore` + rollup writer, SQS delivery source for Dispatcher, then host wiring.

### 2026-10-21 Documentation upkeep (D-1..D-4)
- **Status:** in progress
- **Why:** Owner (architect) wants docs that explain the whole flow, updated with every task, tracked automatically.
- **Done:** `docs/flow-walkthrough.md` (D-1); rule in `CLAUDE.md` (D-2, D-3); D-items in `roadmap.md`.
- **Resume from:** D-4 audit of the older docs against the code; after every merged task run the D-3 audit (walkthrough "Built?" column first).

### 2026-10-21 Application logging gap
- **Status:** in progress (sink decided: D32; L-1..L-5, L-7 done in the backfill PR; L-6 deferred to the rollup flusher; L-8 partly; L-9 cloud later)
- **Why:** Owner noticed no component logs anything; needed to debug where code breaks.
- **Done:** added L-1..L-10 to `docs/roadmap.md` (standard, host setup, backfill per finished component, hygiene tests, sink); rule added to `CLAUDE.md`. SQS adapters PR #21 merged.
- **Proposed sink:** CloudWatch Logs (7-14 day retention, Logs Insights) plus S3 export for cheap long retention; no Elasticsearch/OpenSearch at launch (fixed cost well above the budget). Needs owner answer, then record as a decision.
- **Resume from:** after the sink answer: L-1 (`docs/logging.md`), L-2, then backfill L-3..L-7. S3 archive / DynamoDB adapters continue after.

### 2026-10-21 SQS adapters (src/Aws)
- **Status:** done (PR #21 merged)
- **Why:** Roadmap: replace in-memory ports with AWS-backed ones.
- **Outcome:** new `WebhookGateway.Aws` project: `SqsEventPublisher` (Ingest), `SqsEventSource` (Fan-out), `SqsDeliveryPublisher` (Fan-out first attempt + Dispatcher retry with `DelaySeconds`, clamped 0-15 min). Integration tests run against Moto and skip when it is not up. Gotcha: AWSSDK v4 returns null `Messages` on an empty receive.
- **Resume from:** S3 `IEventArchive`, DynamoDB `IDeliveryStateStore` + rollups, SQS delivery source for Dispatcher, then host wiring (DI) and P1-4 Valkey.

### 2026-10-21 Local dev environment verified (P0-4)
- **Status:** PR open
- **Why:** Docker unblocked; `feature/local-dev-environment` was unverified.
- **Outcome:** removed wedged containers; `./scripts/dev-up.sh` brings up PostgreSQL, Valkey, Moto and creates queues, tables, bucket, KMS key; second run is idempotent; `make verify` passes (183 unit, 18 integration). Note: old `elasticmq` compose service is gone, SQS is Moto only.
- **Resume from:** after merge, AWS-backed adapters (SQS/DynamoDB/S3) against Moto, then P1-4 Valkey limiter/idempotency/quota.

### 2026-10-20 Roadmap execution (Phase 0 and 1, offline parts)
- **Status:** offline work merged to `main`; waiting on the owner for Docker; next steps below
- **Why:** Approved roadmap in `docs/roadmap.md`. Owner role: decide implementation myself, discuss only architecture-level. Owner reviewed PR #10 and #14 comments and then said to merge.
- **Merged to main (owner approved the merge; each passed `make verify` locally and CI):** #9 scaffold, #10 Contracts, #11 `make verify` + CI, #12 Ingest accept endpoint, #13 Fan-out core, #14 Dispatcher signing/SSRF/sender/retry, #16 Core rollups, #18 delivery processor (replaced #15, which GitHub closed when its base branch was deleted). Lesson: retarget stacked PRs to `main` **before** deleting the base branch.
- **Decisions from owner review:** D30 identifiers are prefix + UUIDv7 (not ULID); D31 retry window by plan (Basic ~24 h, Advanced ~3 days, Premium ~7 days); D29 retry count clarified.
- **What exists now:** Ingest `POST /v1/events` (API-key auth, local token bucket, validation, in-memory publisher); Fan-out processor + consumer (in-memory ports); Dispatcher signing, SSRF-safe sender, plan-based retry policy, delivery processor (in-memory ports); Core rollup accumulator. 183 unit + 18 integration tests.
- **Not built yet:** every AWS adapter (SQS source/publishers, S3 archive, DynamoDB stores, retry scheduler loop), Valkey limiter/idempotency/quota, pacing, connection permits, ControlPlane, portal, IaC.
- **Blockers for the owner:**
  1. Docker Desktop is stuck behind a "new license" dialog (process `--name=new-license`); new containers hang at "Starting". Open Docker Desktop, accept the terms, restart it, then `docker rm -f webhookgateway-dev-sqs-1 webhookgateway-dev-elasticmq-1 webhookgateway-dev-aws-1` (wedged containers).
  2. Final review of `database.md` (gates P1-2 migrations).
- **Resume from:** branch `feature/local-dev-environment` (docker-compose with PostgreSQL, Valkey, Moto on 5050 + `scripts/dev-up.sh`, **unverified**): once Docker works, run `./scripts/dev-up.sh`, fix, open the P0-4 PR. Then AWS-backed adapters with Moto/Testcontainers; then P1-4 (Valkey limiter, idempotency, quota), P1-8 pacing, P1-9 connection permits, retry scheduler loop, P2 ControlPlane/portal. Check open PR comments first (`gh pr list`, then `gh api repos/twobudgies/WebhookGateway/pulls/<n>/comments`).

### 2026-10-20 Orchestrator spec: owner decisions recorded
- **Status:** in progress
- **Why:** User answered the open architecture questions from `orchestrator-spec.md` section 15; the draft cannot be finalized until they are acted on.
- **End goal:** Spec approved and the autonomous task-chain agent built (spec phases 0-7).
- **Decisions (see `decisions.md` D14-D17):** monorepo + orchestrator repo; Pro plan only; GitHub Pro later; 12h veto window.
- **Plan / steps:**
  - [x] Record D14-D17 in `decisions.md`
  - [ ] Update `orchestrator-spec.md` section 15 to mark these as decided
  - [ ] Decide how to merge Ingest/Dispatcher/Global into one monorepo (history-preserving); ask the user before touching remotes (architecture + destructive)
  - [ ] Spec phase 0 prerequisites: verify the "unverified items" list in section 15 (GitHub free-plan limits, Pro terms for scripted use, sandbox + Docker, Keychain from launchd)
  - [ ] User approves the spec; then update/replace `workflow.md`
- **Resume from:** update spec section 15, then plan the monorepo merge.

### 2026-10-20 Consolidate docs into Global
- **Status:** done (2026-10-20)
- **Why:** A top-level `WebhookGateway/docs/` folder (outside every repo) was created holding `architecture-spec.md`. The user wants all docs in `WebhookGateway.Global` since it is the source of truth, and the stray copy is unversioned and can drift from `Global/docs/architecture.md`.
- **End goal:** Every doc lives in `WebhookGateway.Global/docs/`. No `docs/` folder outside a repo. Ingest and Dispatcher hold only a short README pointing to Global.
- **Plan / steps:**
  - [x] Agree with user that Global is the only home for docs
  - [x] Create this work log and add it to the Global `CLAUDE.md` index
  - [x] Compared the two files: they are **not duplicates**. `architecture-spec.md` is a 595-line DRAFT v0.1 spec for the autonomous task-chain orchestrator (agent, state machine, security, budget); `Global/docs/architecture.md` is the product architecture. Nothing to merge into `architecture.md`.
  - [x] Copied it to `Global/docs/orchestrator-spec.md` (byte-identical, verified with `cmp`) and indexed it in `CLAUDE.md`
  - [x] Committed on branch `docs/consolidate-docs-and-worklog`
  - [x] Removed `WebhookGateway/docs/`: user approved; the harness blocked `rm -r`, so it was moved to the session scratchpad as a backup instead (safe to delete later)
  - [x] PR opened: https://github.com/twobudgies/WebhookGateway.Global/pull/1 (`gh` is installed and authenticated now; `workflow.md` tooling note is outdated)
- **Touched:** `WebhookGateway/docs/architecture-spec.md` (to remove), `Global/docs/worklog.md` (new), `Global/docs/orchestrator-spec.md` (new), `Global/CLAUDE.md`
- **Resume from:** nothing left. PR #1 merged into `main` (merge commit 42825c5).
- **Note:** the spec says it replaces `workflow.md` and decisions D13 **only on the user's approval**. It is still a draft; do not treat it as the active workflow.
