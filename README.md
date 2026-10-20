# WebhookGateway

Webhook-as-a-service on AWS (.NET 10). Monorepo: shared docs and schema at the root, services under `src/`.

- `docs/`: architecture, contracts, database, decisions, workflow, worklog
- `src/Ingest`: receives events, fans out deliveries
- `src/Dispatcher`: signs, sends and retries deliveries

Start with `CLAUDE.md`.
