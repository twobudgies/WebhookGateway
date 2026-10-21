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

## Build and test

.NET 10 SDK (pinned in `global.json`). From the repo root:

```
make verify                                      # the one gate: format check + build + tests (also run by CI)

dotnet build WebhookGateway.slnx -c Release      # warnings are errors
dotnet test WebhookGateway.slnx -c Release
dotnet format WebhookGateway.slnx --verify-no-changes
```

Projects: `src/{Contracts,Ingest,FanOut,Dispatcher,ControlPlane}`, `tests/{UnitTests,IntegrationTests}`. Package versions are central in `Directory.Packages.props`; shared compiler settings in `Directory.Build.props`.

## Where things live

Keep detail in `docs/` and link it here rather than growing this file.

- `docs/architecture.md`: end-to-end flow, components, deployment, cost-first Stage 1 on SQS, scale path to 30k events/s (**approved v4**)
- `docs/cost-model.md`: monthly bill estimate and cost per million events (**estimate; prices unverified**)
- `docs/data-plane.md`: SQS queues, DynamoDB tables, S3 layout for the high-volume path (**approved**)
- `docs/tenancy-and-limits.md`: plans, accept/delivery/connection limits, quotas, tenant config (**approved structure, provisional numbers**)
- `docs/portal.md`: customer UI spec (**approved**)
- `docs/contracts.md`: shared message/event schemas, queue topics, APIs between components
- `docs/database.md`: PostgreSQL design and migrations (**in review**)
- `docs/workflow.md`: how work is done (agent-driven, GitHub)
- `docs/orchestrator-spec.md`: **DRAFT v0.1** spec for the autonomous task-chain orchestrator (not yet approved; supersedes `workflow.md` only on approval)
- `docs/flow-walkthrough.md`: **start here**: one event end to end, with code pointers and what is built vs. not (living; update with every task)
- `docs/load-testing.md`: load/performance test plan, scenarios, metrics, how capacity numbers are derived (**plan v1**; results go to `capacity.md`)
- `docs/glossary.md`: domain terms
- `docs/decisions.md`: decision log (what, why, date)
- `docs/worklog.md`: agent work log (why, end goal, status, resume point); **read first after any interruption, update as you work**

Fill these in as the design firms up. Until then, treat missing information as unknown and ask.

## Rules for maintaining this repo

- **Docs stay in step with the code.** Every work item updates the docs it affects (`docs/flow-walkthrough.md` always checked; plus architecture, contracts, data-plane, tenancy, logging, roadmap, worklog as relevant) **in the same PR**. If a doc turns out to be stale later, open a docs-only PR, check it against the code, and merge it once `make verify` and CI pass. Tracked as D-1..D-4 in `docs/roadmap.md`.
- **Every work item includes its application logging** (structured, no secrets or payload bodies, ids as fields). Items finished before 2026-10-21 are backfilled under L-3..L-7 in `docs/roadmap.md`.
- Anything used by more than one component (schemas, naming, retry/audit semantics) is defined **here first**, then implemented in the components. Contract change plus consumers can ship in one PR.
- If a change in one component alters a contract, update `docs/contracts.md` in the same piece of work.
- Record non-obvious design choices in `docs/decisions.md` with the reason.
