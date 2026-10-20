# WebhookGateway.Ingest

Responsibility: receives incoming webhook events. Authenticates producers, validates and stores events, fans out deliveries to SQS; also hosts the management API.

## Global context (read first)

This repo is one part of a multi-repo system. The full context lives in the sibling repo
`../WebhookGateway.Global`. Before designing or changing anything that touches other
components, read:

@../WebhookGateway.Global/CLAUDE.md
@../WebhookGateway.Global/docs/architecture.md
@../WebhookGateway.Global/docs/contracts.md
@../WebhookGateway.Global/docs/database.md
@../WebhookGateway.Global/docs/glossary.md
@../WebhookGateway.Global/docs/workflow.md

Decisions history: `../WebhookGateway.Global/docs/decisions.md` (read when you need the why).

## Working rules

- Follow `workflow.md`: feature branches and PRs, never commit to `main`; the user reviews and merges.
- `database.md` is *in review*: do not build on parts marked *(review)* without asking.
- Stay within this repo's responsibility. If a task needs another repo's behavior, check
  the Global contracts instead of assuming.
- Never change a shared contract (schemas, topics, API shapes) only here. Update
  `../WebhookGateway.Global/docs/contracts.md` as well, and tell the user which other
  repos are affected.
- If Global is missing or has TODO sections relevant to the task, say so and ask rather than guessing.
- If `../WebhookGateway.Global` is not found, ask the user for its location.

## This repo

TODO: build/test commands, project layout, conventions.
