# Contracts

Source of truth for anything crossing a repo boundary. Change here first.
Status: **Draft v1**, aligned with `architecture.md`. JSON uses camelCase; timestamps are ISO-8601 UTC.

## 1. Producer API (Ingest, public)

`POST /v1/events`
Headers: `Authorization: Bearer <apiKey>`, `Idempotency-Key: <string, <=128>` (optional but recommended)

```json
{ "type": "order.created", "occurredAt": "2026-10-20T10:00:00Z", "data": { } }
```
Response `202`: `{ "eventId": "evt_...", "deliveries": 3 }`
Errors: `400` invalid, `401`, `413` (max 256 KB payload), `429`. Duplicate idempotency key returns the original `202` body.

## 2. DeliveryMessage (SQS `deliveries`, Ingest -> Dispatcher)

```json
{
  "schemaVersion": 1,
  "tenantId": "ten_...",
  "eventId": "evt_...",
  "deliveryId": "dlv_...",
  "endpointId": "ep_...",
  "attempt": 1,
  "enqueuedAt": "2026-10-20T10:00:00Z"
}
```
The message is a **pointer only**; payload and endpoint config are read from the database, so messages stay small and secrets never travel on the queue. Dispatcher re-enqueues the same shape with `attempt+1` for retries.

## 3. Outbound webhook request (Dispatcher -> subscriber)

`POST <endpoint url>`, `Content-Type: application/json`
Headers: `webhook-id` (= deliveryId), `webhook-timestamp` (unix seconds), `webhook-signature` (`v1,<base64 hmac-sha256 of "{id}.{timestamp}.{body}">`; multiple space-separated during secret rotation), optional `Authorization: Bearer <oauth2 token>`.
Body:
```json
{ "id": "evt_...", "type": "order.created", "occurredAt": "...", "data": { } }
```
Success = any `2xx` within 10s.

## 4. Database

PostgreSQL, **in review**. Tables, ownership and consistency rules: see `database.md`. Services must not rely on DynamoDB.

## 5. Audit record (Dispatcher -> S3)

Batched JSON Lines at `s3://<audit-bucket>/tenant=<id>/date=<yyyy-mm-dd>/<batch>.jsonl.gz`:
```json
{ "ts": "...", "tenantId": "", "eventId": "", "deliveryId": "", "endpointId": "", "attempt": 1,
  "statusCode": 200, "latencyMs": 123, "outcome": "Succeeded|Retry|Failed|Exhausted", "error": null }
```
No payload bodies and no secrets in audit records.

## 6. Shared code

IDs, enums and message DTOs are defined by this document. Until a shared package exists, each repo mirrors them; when duplication hurts, publish a `WebhookGateway.Contracts` NuGet package from this repo (see decisions).
