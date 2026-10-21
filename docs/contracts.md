# Contracts

Source of truth for anything crossing a component or repo boundary. Change here first.
Status: **Draft v2**, aligned with `architecture.md` v4 and `data-plane.md`. JSON uses camelCase; timestamps are ISO-8601 UTC.

## 1. Producer API (Ingest, public)

`POST /v1/events`
Headers: `Authorization: Bearer <apiKey>`, `Idempotency-Key: <string, <=128>` (optional but recommended)

```json
{ "type": "order.created", "occurredAt": "2026-10-20T10:00:00Z", "data": { } }
```
Response `202`: `{ "eventId": "evt_..." }` (fan-out happens asynchronously, so the delivery count is not known at accept time; read it from the portal/API)
Errors: `400` invalid, `401`, `403 tenant_suspended`, `413` (max 256 KB payload), `429`. Duplicate idempotency key (within the plan's window) returns the original `202` body.
`202` means *accepted* (durably queued), not *delivered*.

`429` body and headers (`RateLimit-Limit`, `RateLimit-Remaining`, `RateLimit-Reset`, `Retry-After`):
```json
{ "error": { "code": "rate_limited | quota_exceeded | backlog_full", "message": "..." } }
```
Details and plan values: `tenancy-and-limits.md`.

## 2. Internal messages (SQS)

Defined in `data-plane.md` section 3: the **event message** (queue `events`, Ingest -> Fan-out) and the **DeliveryMessage** (queues `deliveries-{tier}`, Fan-out -> Dispatcher). The DeliveryMessage carries the payload **inline** (up to ~200 KB, else `payloadRef`), the endpoint id, attempt number and `notBefore`. Secrets never travel on the queue. Dispatcher re-enqueues the same shape with `attempt+1` for retries.

## 3. Outbound webhook request (Dispatcher -> subscriber)

`POST <endpoint url>`, `Content-Type: application/json`
Headers: `webhook-id` (= deliveryId), `webhook-timestamp` (unix seconds), `webhook-signature` (`v1,<base64 hmac-sha256 of "{id}.{timestamp}.{body}">`; multiple space-separated during secret rotation), optional `Authorization: Bearer <oauth2 token>`.
Body:
```json
{ "id": "evt_...", "type": "order.created", "occurredAt": "...", "data": { } }
```
Success = any `2xx` within 10s.

## 4. Data stores

PostgreSQL holds configuration only (`database.md`, final v1). Failed/retrying deliveries and rollups are in DynamoDB; payload archive and result logs are in S3 (`data-plane.md`). Services must not write to stores they do not own.

## 5. Result log (Dispatcher -> S3)

Batched JSON Lines, one line per attempt, shape in `data-plane.md` section 3, at `s3://<bucket>/results/tenant=<id>/date=<yyyy-mm-dd>/<worker>-<minute>.jsonl.gz`. No payload bodies and no secrets.

## 5b. Portal API (ControlPlane)

`/portal/v1/...` read and management API for the customer portal; sketch in `portal.md` section 4. The OpenAPI document will be published from this repo; the portal generates its client from it.

## 6. Shared code

IDs, enums and message DTOs are defined by this document. In the monorepo they live in a shared project `src/Contracts` referenced by Ingest, FanOut, Dispatcher and ControlPlane; the portal uses the generated OpenAPI client.
