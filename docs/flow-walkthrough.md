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
| 1. Accept | API-key auth, tenant status check, rate limit, body size and envelope validation, build `EventMessage`, publish, answer `202` (or `401/403/413/400/429`, `503` if the queue write fails) | `src/Ingest/Api/EventsEndpoint.cs` | Yes. In-memory publisher/limiter/key store in the host; SQS publisher exists in `src/Aws` but is not wired in yet |
| 2. Queue `events` | Durable hand-off Ingest to Fan-out | `src/Aws/Sqs/SqsEventPublisher.cs`, `SqsEventSource.cs` | Adapters done and tested against Moto; not wired into the hosts |
| 3. Fan-out | Read a batch, write it to the archive, look up the tenant's subscriptions, make one `DeliveryMessage` per matching endpoint, publish to the plan's tier queue, then acknowledge | `src/FanOut/EventConsumer.cs`, `Processing/FanOutProcessor.cs` | Yes. S3 archive adapter done (`src/Aws/S3/S3EventArchive.cs`, tested on Moto, not wired into the host); subscription store adapter not built |
| 4. Queue `deliveries-{tier}` | One message per delivery; retries go back here with `DelaySeconds` (max 15 min) | `src/Aws/Sqs/SqsDeliveryPublisher.cs` | Publisher done; the Dispatcher's queue consumer is not built |
| 5. Dispatch | Skip if already finished, expire if too old, load endpoint and payload, sign (Standard Webhooks HMAC), POST with SSRF-safe client, decide: delivered / retry / schedule / fail / exhaust | `src/Dispatcher/Delivery/DeliveryProcessor.cs`, `Outbound/WebhookSender.cs`, `Signing/`, `Retry/RetryPolicy.cs` | Logic done and unit-tested; `S3PayloadResolver` reads large payloads back from the archive; host does not consume a queue yet |
| 6. State | Only failures and retries are stored (`delivery_state`); success leaves no row | `Dispatcher/Delivery/IDeliveryStateStore.cs`; DynamoDB adapter `src/Aws/DynamoDb/DynamoDbDeliveryStateStore.cs` (tested on Moto, not wired into the host; retry scheduler loop over the due index not built) |
| 7. Rollups | In-memory counters and latency histograms drained into per-minute rows | `src/Core/Rollups/RollupAccumulator.cs` | Accumulator done; `src/Aws/DynamoDb/DynamoDbRollupWriter.cs` writes drained rows with atomic ADD (tested on Moto). The periodic flush loop and S3 result logs are not built |
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
