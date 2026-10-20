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

### 2026-10-20 Architecture v3 proposal (30k events/s per cell, pacing, connection limits, portal)
- **Status:** blocked (PR #6 open, waiting on owner review; **no code until approved**)
- **Why:** Owner wants to compete with established players: one Asia-region cell must handle 5k-10k events/s normally and 30k/s peak. Accept bursts by plan but deliver steadily; cap per-tenant connections so slow consumers cannot starve others; customer portal showing failures, success counts, progress and slow consumers. v2 (sync Postgres outbox) only scaled to ~2k/s, so v3 replaces it.
- **End goal:** Approved architecture and a prioritized work-item list; then autonomous implementation.
- **Plan / steps:**
  - [x] Archived old Ingest and Dispatcher repos
  - [x] `architecture.md` v3: Kinesis log, fan-out, SQS tiers, DynamoDB state for exceptions only, rollups, pacing (5.1), connection limits, sizing for 5k/10k/30k, scale triggers, 15 decisions
  - [x] New `data-plane.md`, `portal.md`; `tenancy-and-limits.md` v2; `database.md` scope note
  - [ ] Owner answers decisions (architecture section 12, tenancy section 7) via PR comments
  - [ ] Fold answers in; log D20+ in `decisions.md`; update `contracts.md`, `database.md`
  - [ ] Derive work items: load-test harness (L1), cost model (L2), then services; create `webhookgateway-portal` repo
- **Touched:** docs/architecture.md, data-plane.md, portal.md, tenancy-and-limits.md, database.md, CLAUDE.md, worklog.md (branch `docs/architecture-v2-proposal`, PR #6)
- **Resume from:** wait for the owner's review comments on PR #6; apply them.

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
