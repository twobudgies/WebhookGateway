# Decision log

Format: ID, date, decision, why. Add new entries at the bottom; do not rewrite history, supersede instead.

- **D1 (2026-10-20) .NET 10 LTS.** Supported to Nov 2028. .NET 8 LTS ends Nov 2026; .NET 9 is STS.
- **D2 (2026-10-20) AWS ECS Fargate for services.** Containers decouple us from Lambda managed-runtime lag for new .NET versions and suit long-running SQS consumers with in-memory token/secret caches. Revisit Lambda for Ingest if cost/traffic pattern favors it.
- **D3 (2026-10-20) SQS + S3 as core messaging/storage.** Managed, cheap. (DynamoDB part superseded by D10.)
- **D4 (2026-10-20) Queue message is a pointer, not the payload.** Keeps messages small, keeps secrets/PII off the queue, simplifies replay.
- **D5 (2026-10-20) At-least-once delivery, idempotent receivers.** Exactly-once is not achievable over HTTP; `webhook-id` enables receiver dedupe.
- **D6 (2026-10-20) Standard Webhooks signature scheme.** Existing ecosystem/libraries for receivers.
- **D7 (2026-10-20) Retries: 8 attempts over ~24h with jittered backoff; EventBridge Scheduler for delays over 15 min.** SQS max delay is 15 min.
- **D8 (2026-10-20) Control-plane API lives in Ingest repo for now.** Avoid a 4th repo until needed.
- **D9 (2026-10-20) IaC with AWS CDK (C#).** Same language as services. Location of infra code: per-repo stacks for its own resources; shared resources (queues, tables, buckets) defined in Global `infra/` once created. *(Open: confirm.)*
- **D10 (2026-10-20) PostgreSQL replaces DynamoDB** per user direction. Relational model fits tenants/endpoints/subscriptions, enables the transactional outbox, ad-hoc queries and replay. Details and open points in `database.md`. **Status: in review, not final.**
- **D11 (2026-10-20) Schema + migrations owned by Global (`db/migrations`), applied by pipeline.** One place for the shared schema. *(in review)*
- **D12 (2026-10-20) Transactional outbox for Ingest -> SQS.** Prevents events stored but not queued.
- **D13 (2026-10-20) Development is agent-driven; the user defines features, Claude implements via GitHub PRs.** See `workflow.md`.
- **D14 (2026-10-20) Monorepo + separate orchestrator repo.** Ingest, Dispatcher and Global merge into one repo; the orchestrator lives in its own private repo so the agent cannot modify the system that runs it. One PR can change a contract and both consumers, removing cross-repo ordering deadlocks. Chosen by the user from `orchestrator-spec.md` D2 option B. Supersedes the 3-repo split (D8 "avoid a 4th repo" is moot). **The merge of the existing repos is not done yet**; tracked in `worklog.md`.
- **D15 (2026-10-20) Agent billing: Claude Pro plan only, no extra-usage credits.** Chosen by the user. Risk noted in the spec: usage windows will probably stall the chain (~35% chance it sustains a daily pace). Revisit after the dry run measures cost per task.
- **D16 (2026-10-20) GitHub Pro upgrade deferred until after the dry run.** Until then the agent is credential-less (orchestrator pushes, opens PRs and merges) because the free plan has no branch protection. Chosen by the user.
- **D17 (2026-10-20) Auto-merge veto window is 12h for non-risky tasks.** Any comment, `hold` label or new push cancels it; `risky` tasks always wait for the owner's merge. Chosen by the user.
- **D18 (2026-10-20) Global becomes the monorepo.** No new monorepo repo is created. Ingest and Dispatcher (each only README + CLAUDE.md so far) are imported under `src/` with history; the old repos are archived, not deleted. Rename `WebhookGateway.Global` to `WebhookGateway` is proposed, not yet executed. Chosen by the user ("looks good", 2026-10-20). Refines D14.
- **D19 (2026-10-20) Build the orchestrator as a separate private repo (`webhookgateway-orchestrator`), C# on .NET 10, human-supervised build.** It runs the agent, holds credentials, verifies, pushes/merges. See `orchestrator-spec.md`. Approved by the user after the role explanation.
