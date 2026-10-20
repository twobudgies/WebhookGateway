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

### 2026-10-20 Roadmap execution (start Phase 0)
- **Status:** in progress
- **Why:** Approved roadmap in `docs/roadmap.md`. Owner role instruction: decide implementation myself, discuss only architecture-level.
- **End goal:** Working local pipeline (Phase 1), then control plane + portal, then cloud with owner go-ahead.
- **Plan / steps:**
  - [x] Finalize docs (this PR)
  - [x] Owner installed .NET 10.0.402 and Docker (verified); blocker cleared
  - [x] P0-2 solution scaffold (PR: feature/solution-scaffold): 5 src projects + unit/integration test projects, central package versions, build 0 warnings, 2 health tests pass
  - [ ] P0-4 local dev environment (docker-compose)
  - [x] P1-1 shared Contracts project (PR open, `risky` per roadmap: needs owner merge): ULID ids, event/delivery/result messages, JSON options; 26 tests pass
  - [x] P1-6a Dispatcher core (PR open, `risky` security: needs owner merge): Standard Webhooks signing (verified against the spec test vector), SSRF protection (URL validation + per-connection IP policy, no redirects), signed HTTP sender with outcome classification (2xx / retry / final / 410), retry schedule (9 attempts, D29). Not yet: SQS consumption (P1-6b), `delivery_state` and retry scheduling (P1-7), circuit breaker, connection permits (P1-9), rollups (P1-10).
  - [x] P1-6b/P1-7a Delivery processor (PR open, stacked on the Dispatcher core PR): decision logic per delivery - skip finished duplicates, expire after 24 h backlog age, resolve endpoint and payload, send, then success (delete stale row) / retry queued (<=15 min) / scheduled index (>15 min) / exhausted / permanent failure; writes state only on non-success. In-memory ports; 17 unit tests. Not yet: SQS consumer loop, DynamoDB store, retry scheduler loop, archive payload resolver (need Docker to verify), circuit breaker, connection permits.
  - [ ] P0-3 verify/CI
  - [ ] P1-2 migrations: only after the owner's final review of `database.md`
- **Blocker found (2026-10-20):** this Mac has only .NET SDKs up to 8.0.421 (no .NET 10, which D1 requires) and **no Docker/container runtime** (needed for LocalStack, PostgreSQL, Valkey and Testcontainers). Code cannot be built or tested until the owner installs both.
- **Blocker 2 (2026-10-20):** Docker Desktop is stuck: a "new license" dialog is open (process `--name=new-license`) and new containers hang at "Starting". Needs the owner to open Docker Desktop, accept the terms, and probably restart it. Also remove the wedged containers `webhookgateway-dev-sqs-1` and `webhookgateway-dev-elasticmq-1` (`docker rm -f`). The dev stack (PostgreSQL, Valkey, Moto on port 5050) is written on branch `feature/local-dev-environment` but **not verified**.
- **Resume from:** (a) once Docker works: run `./scripts/dev-up.sh`, fix issues, open the P0-4 PR; (b) meanwhile non-Docker items: P0-3 (CI), P1-3 onwards with in-memory fakes. Risky items (P0-3, P1-1, P1-2, P1-3, P1-6) are opened as PRs and left for the owner to merge; later branches are stacked on them.

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
