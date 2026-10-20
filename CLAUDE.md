# WebhookGateway.Global

This repo is the **source of truth for the full WebhookGateway context**. The other repos
implement one part each and point here for the big picture.

## Repos in the system

| Repo | Responsibility |
|---|---|
| `WebhookGateway.Ingest` | Receives incoming webhook events. Authenticates producers, validates/stores events, fans out to per-endpoint deliveries on SQS. Also hosts the tenant/endpoint management API for now. |
| `WebhookGateway.Dispatcher` | Sends events to subscriber endpoints: signs payloads, manages OAuth2 tokens, schedules retries, flushes audit records. Named "Dispatcher" because it covers send plus retry policy. |
| `WebhookGateway.Global` | Shared context: architecture, contracts, glossary, decisions (this repo). |

## Tech baseline

.NET 10 (LTS), C# 14, AWS (ECS Fargate, SQS, PostgreSQL (Aurora/RDS), S3, Secrets Manager, EventBridge Scheduler).
Full flow in `docs/architecture.md`; boundary contracts in `docs/contracts.md`.

Sibling repos are expected at `../WebhookGateway.<Name>` relative to this one.

## Where things live

Keep detail in `docs/` and link it here rather than growing this file.

- `docs/architecture.md`: end-to-end flow, components, boundaries between repos
- `docs/contracts.md`: shared message/event schemas, queue topics, APIs between repos
- `docs/database.md`: PostgreSQL design and migrations (**in review**)
- `docs/workflow.md`: how work is done (agent-driven, GitHub)
- `docs/glossary.md`: domain terms
- `docs/decisions.md`: decision log (what, why, date)

Fill these in as the design firms up. Until then, treat missing information as unknown and ask.

## Rules for maintaining this repo

- Anything used by more than one repo (schemas, naming, retry/audit semantics) is defined **here first**, then implemented in the repos.
- If a change in one repo alters a contract, update `docs/contracts.md` in the same piece of work.
- Record non-obvious design choices in `docs/decisions.md` with the reason.
