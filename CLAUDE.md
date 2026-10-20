# WebhookGateway

Monorepo and **source of truth for the full WebhookGateway context** (decision D14/D18). Shared docs and schema live at the root; each service lives under `src/`.

## Layout

| Path | Responsibility |
|---|---|
| `src/Ingest` | Receives incoming webhook events. Authenticates producers, validates/stores events, fans out to per-endpoint deliveries on SQS. Also hosts the tenant/endpoint management API for now. |
| `src/Dispatcher` | Sends events to subscriber endpoints: signs payloads, manages OAuth2 tokens, schedules retries, flushes audit records. Named "Dispatcher" because it covers send plus retry policy. |
| `docs/` | Architecture, contracts, database, decisions, workflow, worklog (this is the source of truth). |
| `db/migrations/` | Shared PostgreSQL schema (created with the first migration). |

The agent orchestrator lives in a separate private repo, `webhookgateway-orchestrator` (D19), so the agent cannot modify the system that runs it.

## Tech baseline

.NET 10 (LTS), C# 14, AWS (ECS Fargate, SQS, PostgreSQL (Aurora/RDS), S3, Secrets Manager, EventBridge Scheduler).
Full flow in `docs/architecture.md`; boundary contracts in `docs/contracts.md`.

## Where things live

Keep detail in `docs/` and link it here rather than growing this file.

- `docs/architecture.md`: end-to-end flow, components, boundaries between components
- `docs/contracts.md`: shared message/event schemas, queue topics, APIs between components
- `docs/database.md`: PostgreSQL design and migrations (**in review**)
- `docs/workflow.md`: how work is done (agent-driven, GitHub)
- `docs/orchestrator-spec.md`: **DRAFT v0.1** spec for the autonomous task-chain orchestrator (not yet approved; supersedes `workflow.md` only on approval)
- `docs/glossary.md`: domain terms
- `docs/decisions.md`: decision log (what, why, date)
- `docs/worklog.md`: agent work log (why, end goal, status, resume point); **read first after any interruption, update as you work**

Fill these in as the design firms up. Until then, treat missing information as unknown and ask.

## Rules for maintaining this repo

- Anything used by more than one component (schemas, naming, retry/audit semantics) is defined **here first**, then implemented in the components. Contract change plus consumers can ship in one PR.
- If a change in one component alters a contract, update `docs/contracts.md` in the same piece of work.
- Record non-obvious design choices in `docs/decisions.md` with the reason.
