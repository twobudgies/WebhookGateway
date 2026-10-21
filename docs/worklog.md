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

### 2026-10-20 Architecture v4 approved; roadmap created
- **Status:** done (approved 2026-10-20: owner approved all 18 decisions; PR #6 merged)
- **Why / goal:** Owner wanted the architecture finalized (cost-first, SQS, scale path to 30k events/s, portal) before any code.
- **Outcome:** `architecture.md` v4 approved; decisions D20-D28 logged; `contracts.md` v2; `database.md` rewritten config-only (still **in review**); `docs/roadmap.md` lists the work items; plan numbers still provisional.
- **Resume from:** see the next entry (roadmap execution).

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
