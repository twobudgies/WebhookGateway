# Architecture

Status: **Baseline v1 (finalized for step 1).** Changes go through `decisions.md`.

## Goal

Webhook-as-a-service: tenants register endpoints and subscribe to event types; producers
send events; the platform reliably delivers them (signed, retried, audited) to subscriber
endpoints.

## Tech stack

- **.NET 10 (LTS, supported to Nov 2028)**, C# 14. Do not use .NET 8 (support ends Nov 2026) or .NET 9 (STS).
- **AWS**, infrastructure as code (AWS CDK in C#; see decisions).
- Compute: **ECS Fargate** containers for Ingest and Dispatcher (container images avoid depending on Lambda managed-runtime availability for the newest .NET).
- Queueing: **SQS** (standard queues + DLQs). Long retry delays: **EventBridge Scheduler**.
- Storage: **PostgreSQL** (Aurora/RDS; config, events, deliveries; see `database.md`, in review), **S3** (audit logs, optional payload archive), **Secrets Manager** (signing secrets, OAuth2 client secrets).
- Edge: **API Gateway (HTTP API)** or ALB in front of Ingest; WAF for rate limiting.
- Observability: CloudWatch logs/metrics, OpenTelemetry traces (X-Ray).

## Components

| Component | Repo | Role |
|---|---|---|
| Ingest | `WebhookGateway.Ingest` | Public API. Authenticates producers, validates and stores events, resolves matching subscriptions, enqueues one delivery per subscription. Returns `202` fast. |
| Dispatcher | `WebhookGateway.Dispatcher` | Consumes deliveries. Signs payload, obtains OAuth2 token if endpoint requires it, POSTs to the subscriber, records the attempt, schedules retries, flushes audit records. |
| Global | `WebhookGateway.Global` | Shared docs, contracts, decisions. Later also shared contract library (see below). |

Tenant/endpoint/subscription management (control-plane API) is **hosted in the Ingest repo as a separate project/host** for now. It can be split into its own repo if it grows.

## End-to-end flow

```
Producer
   |  POST /v1/events  (API key, Idempotency-Key)
   v
[API GW / WAF] --> [Ingest]
                     1. authenticate tenant
                     2. validate envelope + size limit
                     3. dedupe on (tenant, idempotency key)
                     4. one DB transaction: insert Event, look up active Subscriptions for (tenant, eventType),
                        insert a Delivery (Pending) + outbox row per subscription
                     7. outbox relay publishes a DeliveryMessage per delivery to SQS `deliveries`
                     8. return 202 {eventId}
                              |
                              v
                        SQS `deliveries`  --(maxReceive)--> DLQ `deliveries-dlq`
                              |
                              v
                        [Dispatcher]
                     1. load Delivery + Endpoint config (PostgreSQL), skip if already Succeeded/Cancelled
                     2. load signing secret (Secrets Manager, cached)
                     3. if endpoint auth = OAuth2: get cached token or fetch (client credentials)
                     4. sign payload (HMAC-SHA256), POST with timeout (10s)
                     5. record Attempt (status code, latency, error)
                     6a. 2xx            -> Delivery=Succeeded
                     6b. 429/5xx/timeout -> schedule retry (below)
                     6c. other 4xx       -> Delivery=Failed (no retry); 410 may auto-disable endpoint
                     7. buffer audit record -> flush batches to S3 (and Delivery state to PostgreSQL)
```

## Retry policy

- Max **8 attempts** over about 24h: backoff ~ 30s, 2m, 10m, 30m, 1h, 4h, 8h, 10h with full jitter.
- Delay up to 15 min: SQS `DelaySeconds`/visibility timeout. Longer: EventBridge Scheduler one-shot schedule that re-enqueues the `DeliveryMessage`.
- After final failure: Delivery=`Exhausted`, message goes to DLQ, alarm fires. Tenants can replay from the API.
- Honor `Retry-After` on 429/503 (capped).
- Circuit breaker per endpoint: after N consecutive failures, pause new attempts and set Endpoint=`Degraded`; auto-disable after prolonged failure and notify tenant.

## Delivery semantics

- **At-least-once.** Subscribers must dedupe using the `webhook-id` header (= deliveryId stable across retries).
- No global ordering guarantee. Per-endpoint ordering is out of scope for v1.
- Isolation: a slow or failing endpoint must not starve others (per-endpoint concurrency cap and circuit breaker in Dispatcher).

## Security

- Producers: API key (hashed at rest) per tenant; later optionally JWT/Cognito.
- Outbound signing: HMAC-SHA256, [Standard Webhooks](https://www.standardwebhooks.com/) style headers `webhook-id`, `webhook-timestamp`, `webhook-signature`. Secret per endpoint, supports rotation (two active secrets).
- SSRF protection in Dispatcher: HTTPS only, resolve and block private/link-local/metadata IP ranges, no redirects followed.
- Secrets only in Secrets Manager; never logged. Payloads may contain PII: encrypt at rest (KMS), redact in logs.

## Multi-tenancy

Every record and message carries `tenantId`. every table has `tenant_id` and every query filters on it. Per-tenant rate limits at the edge and in Ingest.

## Environments

`dev`, `staging`, `prod`, separate AWS accounts (or at minimum separate stacks), same IaC.

## Out of scope for v1

Event transformation, per-endpoint ordering, customer-facing UI/portal, non-HTTP destinations (SQS/EventBridge targets), multi-region.
