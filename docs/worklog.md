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

### 2026-10-20 Roadmap execution (Phase 0 and 1, offline parts)
- **Status:** waiting on the owner (merges and Docker); non-Docker work done
- **Why:** Approved roadmap in `docs/roadmap.md`. Owner role: decide implementation myself, discuss only architecture-level. Owner was away, so work continued within the rules: routine items merged by me, `risky` items left as PRs.
- **Done and merged to main:** P0-2 solution scaffold (PR #9).
- **Open PRs (all green locally, `make verify` clean), merge order matters:**
  - #10 Contracts project (P1-1, **risky: contract**) - includes `DeliveryMessage.OccurredAt` fix and the solution-wide `EventId` alias. Base for the rest.
  - #11 `make verify` + GitHub Actions CI (P0-3, **risky: CI**), independent; CI passed on the PR.
  - #12 Ingest `POST /v1/events` (P1-3a, **risky: auth**), stacked on #10.
  - #13 Fan-out core (P1-5a), stacked on #10.
  - #14 Dispatcher signing, SSRF-safe sender, retry policy (P1-6a, **risky: security**), stacked on #10.
  - #15 Delivery processor (P1-6b/P1-7a), stacked on #14.
  - #16 Core: rollup accumulator and latency histogram (P1-10a), stacked on #10.
  - Branch `feature/local-dev-environment` (no PR): docker-compose stack (PostgreSQL, Valkey, Moto on 5050) + `scripts/dev-up.sh`; **not verified** because of the Docker problem below.
- **Decision recorded:** D29 (retry = first attempt + 8 retries = 9 attempts).
- **Blockers for the owner:**
  1. Docker Desktop is stuck behind a "new license" dialog (process `--name=new-license`); new containers hang at "Starting". Open Docker Desktop, accept the terms, restart it, then `docker rm -f webhookgateway-dev-sqs-1 webhookgateway-dev-elasticmq-1 webhookgateway-dev-aws-1` (wedged containers).
  2. Merge the `risky` PRs when reviewed (#10 first, then #12, #14; #11 anytime), then the stacked routine ones (#13, #15, #16) after their bases.
  3. Final review of `database.md` (gates P1-2 migrations).
- **Resume from:** after Docker works: verify and open the P0-4 PR (`./scripts/dev-up.sh`); then the AWS-backed adapters (SQS source/publishers, S3 archive, DynamoDB stores) with Moto and Testcontainers; Valkey limiter + idempotency + quota (P1-4); pacing (P1-8); connection permits (P1-9); scheduler loop; ControlPlane (P2). Offline-only options before that: pacing and connection-permit logic with in-memory state.

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
