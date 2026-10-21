# Dispatcher (`src/Dispatcher`)

Responsibility: sends events to subscriber endpoints. Signs payloads, manages OAuth2 tokens, schedules retries, and flushes audit records.

## Context (read first)

This is one component of the WebhookGateway monorepo. Full context lives at the repo root:

@../../CLAUDE.md
@../../docs/architecture.md
@../../docs/contracts.md
@../../docs/database.md
@../../docs/glossary.md
@../../docs/workflow.md
@../../docs/worklog.md

Decisions history: `../../docs/decisions.md` (read when you need the why).

## Working rules

- Follow `docs/workflow.md`: feature branches and PRs, never commit to `main`.
- `database.md` is final (D44); schema changes are new migrations in `db/migrations`.
- Stay within this component's responsibility. If a task needs another component's behavior, check `docs/contracts.md` instead of assuming.
- A shared contract change (schemas, topics, API shapes) updates `docs/contracts.md` in the same PR, and the PR names the other affected components.
- If docs are missing or have TODO sections relevant to the task, say so and ask rather than guessing.

## This component

Build/test commands are in the root `CLAUDE.md`. Project: `WebhookGateway.Dispatcher.csproj`. Layout and conventions: TODO as the component is built.
