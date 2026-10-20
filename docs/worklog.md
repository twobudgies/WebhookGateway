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
