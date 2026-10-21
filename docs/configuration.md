# Configuration reference

Every setting is a standard .NET configuration key. In containers set them as environment variables with `__` for the colon (`Postgres:ConnectionString` is `Postgres__ConnectionString`). Nothing secret belongs in the repo or an image: secrets come from Secrets Manager into the task environment.

Images: `docker build --build-arg PROJECT=<Ingest|FanOut|Dispatcher|ControlPlane|Migrator> -t <name> .` (or `make images`). Web services listen on port 8080 (`/healthz`). Workers (Fan-out, Dispatcher) have no port. The Migrator is a one-shot job: `Migrator "<connection string>" db/migrations` (the image contains the SQL files).

| Key | Used by | Meaning |
|---|---|---|
| `Backend__Mode` | Ingest, Fan-out, Dispatcher | `InMemory` (default, no AWS) or `Aws`. Production: `Aws`. The Dispatcher's queue consumer, retry scheduler and rollup flusher start only in `Aws`. |
| `Aws__Region` | all with `Aws` | Default `ap-south-1`. |
| `Aws__ServiceUrl` | local only | Emulator endpoint (Moto). **Leave empty in production**: it also switches to static dev credentials. |
| `Aws__Archive__Bucket`, `Aws__Archive__Region`, `Aws__Archive__KmsKeyId` | Fan-out | S3 archive bucket, region label in object keys, KMS key for server-side encryption. |
| `Aws__DynamoDb__DeliveryStateTable`, `...__RollupsTable` | Dispatcher, Fan-out | Table names (defaults `delivery_state`, `rollups`). |
| `Postgres__ConnectionString` | Ingest, Fan-out, Dispatcher, ControlPlane | Config database. Without it Ingest/Fan-out/Dispatcher use in-memory config (dev only). |
| `Valkey__ConnectionString` | Ingest | Shared rate limit, idempotency keys, monthly count. Without it Ingest uses in-process limits. |
| `Ingest__Quota__Enforce` | Ingest | `true` rejects over-quota events with 429. Default `false` until plan quotas are set (D42). |
| `Secrets__KmsKeyId` | Dispatcher, ControlPlane | KMS key (id or alias) protecting endpoint signing secrets. Required in production. |
| `Secrets__LocalKey` | dev/tests | Base64 AES key instead of KMS. **Never in production.** One of the two must be set or the service refuses to start. |
| `ApiDocs__Enabled` | Ingest, ControlPlane | `true` (default) serves the OpenAPI definition at `/openapi/v1.json` and the Swagger UI at `/docs`; `false` hides both. |
| `ControlPlane__AdminKey` | ControlPlane | Operator key for `/admin/v1`. Unset means the admin API answers 503. |
| `ControlPlane__AllowInsecureHttp` | ControlPlane | Allow `http://` endpoint URLs (dev only). |
| `Dispatcher__Consumer__MaxConcurrencyPerTier` | Dispatcher | Deliveries in flight per tier queue (default 100). |
| `Dispatcher__Scheduler__PollInterval` | Dispatcher | How often long retry waits are moved back to the queue (default 10 s). |
| `Dispatcher__Delivery__MaxBacklogAge` | Dispatcher | Default 24 h, older deliveries expire. |
| `Rollups__FlushInterval` | Fan-out, Dispatcher | Default 10 s. |
| `Postgres__CacheTtl` | readers | Config cache time, default 30 s. |

IAM for the task roles is defined with the infrastructure (P3): Ingest needs `sqs:SendMessage` on `events`; Fan-out needs receive/delete on `events`, send on the `deliveries-*` queues, `s3:PutObject` on the archive; Dispatcher needs receive/delete/send on the `deliveries-*` queues, `s3:GetObject`, DynamoDB read/write on `delivery_state` and its indexes and `rollups`, and `kms:Decrypt` on the secrets key; ControlPlane needs `kms:GenerateDataKey` on the secrets key.
