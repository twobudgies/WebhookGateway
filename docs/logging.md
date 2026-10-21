# Application logging

Status: **APPROVED** (D32, 2026-10-21). Implementation items: L-1..L-10 in `roadmap.md`.

## Sink and retention

- stdout as JSON, one line per event. ECS ships it to CloudWatch Logs (`awslogs` driver), retention **14 days**. Optional S3 export for longer history.
- Search with CloudWatch Logs Insights by `eventId`, `deliveryId`, `tenantId`, `traceId`.
- Not the same as tenant-facing result logs (`data-plane.md`).

## Rules

- Use `ILogger<T>` with message templates and named fields (never string interpolation), so fields stay queryable.
- **Never log:** payload bodies, headers of inbound requests, API keys, signing secrets, OAuth tokens, endpoint URLs' query strings. Log ids, sizes, status codes and error classes only.
- Always include when known: `tenantId`, `eventId`, `deliveryId`, `endpointId`, `attempt`, `traceId`. Standard fields added by the host: `service`, `version`, `environment`.
- Exceptions are logged once, where they are handled, with the exception object (not just the message).

## Levels

| Level | Use |
|---|---|
| Error | An operation failed and needs attention: publish failure, archive write failure, unhandled exception, message sent to DLQ |
| Warning | Handled but unusual: validation reject burst, retry scheduled after failure, circuit breaker open, rate limit hit, SSRF block |
| Information | Lifecycle and sampled success: service start/stop, batch processed (counts), delivery success (**sampled**, default 1%) |
| Debug | Off in production; local dev only |

## Volume

Success paths are sampled or aggregated (counts per batch), failures are always logged. Target: low log volume per `cost-model.md`.
