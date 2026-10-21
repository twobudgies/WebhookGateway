# End-to-end flow walkthrough

Status: **living document**, kept in step with the code (rule in `CLAUDE.md`, items D-1..D-4 in `roadmap.md`). Written for the architect and new developers: follow one event from the producer to the subscriber, and see what exists in code today. Design reasons are in `architecture.md`; message shapes in `contracts.md` and `data-plane.md`; log rules in `logging.md`.

Last verified against the code: **2026-10-21** (after the logging backfill).

## The path of one event

```
Producer --POST /v1/events--> Ingest --SQS events--> Fan-out --SQS deliveries-{tier}--> Dispatcher --POST--> Subscriber
                                                       |                                    |
                                                  S3 archive                  failures/retries -> DynamoDB delivery_state
                                                                              counters -> DynamoDB rollups, result logs -> S3
```

| Step | What happens | Code | Built? |
|---|---|---|---|
| 1. Accept | API-key auth, tenant status check, rate limit, body size and envelope validation, build `EventMessage`, publish, answer `202` (or `401/403/413/400/429`, `503` if the queue write fails) | `src/Ingest/Api/EventsEndpoint.cs` | Yes. With `Backend:Mode=Aws` the host publishes to SQS (D39). With `Valkey:ConnectionString` set, rate limit, idempotency keys and the monthly count are shared across tasks and fall back to local behavior if Valkey is down (D42). With `Postgres:ConnectionString` set, API keys and plan limits come from the config database (cached 30 s; D43) |
| 2. Queue `events` | Durable hand-off Ingest to Fan-out | `src/Aws/Sqs/SqsEventPublisher.cs`, `SqsEventSource.cs` | Adapters done and wired into the hosts with `Backend:Mode=Aws`; tested against Moto (Ingest to the `events` queue) |
| 3. Fan-out | Read a batch, write it to the archive, look up the tenant's subscriptions, make one `DeliveryMessage` per matching endpoint, publish to the plan's tier queue, then acknowledge | `src/FanOut/EventConsumer.cs`, `Processing/FanOutProcessor.cs` | Yes. S3 archive adapter done (`src/Aws/S3/S3EventArchive.cs`, tested on Moto, not wired into the host); subscription store adapter not built |
| 4. Queue `deliveries-{tier}` | One message per delivery; retries go back here with `DelaySeconds` (max 15 min) | `src/Aws/Sqs/SqsDeliveryPublisher.cs` | Publisher and Dispatcher-side source (`SqsDeliverySource`) done and tested on Moto |
| 5. Dispatch | Skip if already finished, expire if too old, load endpoint and payload, sign (Standard Webhooks HMAC), POST with SSRF-safe client, decide: delivered / retry / schedule / fail / exhaust | `src/Dispatcher/Delivery/DeliveryProcessor.cs`, `Outbound/WebhookSender.cs`, `Signing/`, `Retry/RetryPolicy.cs` | Logic done and unit-tested. `src/Dispatcher/Source/DeliveryConsumer.cs` runs one bounded-concurrency loop per tier (D36); `SqsDeliverySource` reads the queues; `S3PayloadResolver` reads large payloads back. Wired and started only with `Backend:Mode=Aws`; the endpoint store is still in-memory until the ControlPlane exists |
| 6. State | Only failures and retries are stored (`delivery_state`); success leaves no row | `Dispatcher/Delivery/IDeliveryStateStore.cs`; DynamoDB adapter `src/Aws/DynamoDb/DynamoDbDeliveryStateStore.cs` (tested on Moto, wired with `Backend:Mode=Aws`; `src/Dispatcher/Retry/RetryScheduler.cs` moves due long waits back to the tier queue, D40) |
| 7. Rollups | Fan-out counts `created`, Dispatcher counts attempts, queue wait and response time in memory; `RollupFlusher` writes them to DynamoDB `rollups` every 10 s (D41) | `src/Core/Rollups/RollupAccumulator.cs`, `src/Hosting/RollupFlusher.cs`, `src/Aws/DynamoDb/DynamoDbRollupWriter.cs` | Done and wired in Aws mode. S3 result logs and hourly/daily compaction not built |
| 8. Control plane / portal | Tenants, endpoints, subscriptions, keys, portal read API | `src/ControlPlane` (health check only) | Not started |

## Rules the whole flow relies on

- **At-least-once.** A message is deleted only after its work is done; duplicates are harmless because delivery ids are deterministic (`dlv_{event}_{endpoint}`) and receivers dedupe on `webhook-id`.
- **Success is cheap.** No row per successful delivery; counters and sampled logs only.
- **Retry window depends on plan** (D31): Basic about 24 h, Advanced about 3 days, Premium about 7 days.
- **Logs** are JSON on stdout to CloudWatch, 14 days, never payload bodies (D32).

## Where to look when something breaks

1. Find the `eventId` or `deliveryId` (the `202` response carries `eventId`; subscribers see `deliveryId` as `webhook-id`).
2. Search the logs for that id (CloudWatch Logs Insights once deployed; console locally).
3. Failed or retrying deliveries: `delivery_state` row. Payload: S3 archive by `eventId`.

## Local run

`./scripts/dev-up.sh` (PostgreSQL, Valkey, Moto), `make verify` (format, build, tests). See `dev/README.md`.
