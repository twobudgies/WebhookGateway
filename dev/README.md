# Local development stack

Runs PostgreSQL, Valkey and Moto (an emulator for SQS, DynamoDB, S3 and KMS) in Docker. No AWS account or licence token is needed. Dev-only credentials; never reuse them.

```
./scripts/dev-up.sh     # start, then create queues, tables, bucket and KMS key (safe to rerun)
./scripts/dev-down.sh   # stop and delete all local data
```

| Service | Endpoint | Notes |
|---|---|---|
| PostgreSQL 16 | `localhost:5432`, db/user/password `webhook` | config database |
| Valkey 8 | `localhost:6379` | limits, counters, permits |
| Moto (AWS emulator) | `http://localhost:5050`, region `ap-south-1`, keys `local`/`local` | account id `123456789012` |

Resources created by `dev-up.sh` (names from `docs/data-plane.md`): SQS `events`, `deliveries-basic|advanced|premium` (+ `-dlq`), DynamoDB `delivery_state` and `rollups`, S3 bucket `webhookgateway-dev`, KMS alias `alias/webhookgateway-dev`.

Port 5000 is not used because macOS AirPlay Receiver occupies it.

Status: written and reviewed but **not yet verified end to end** (Docker Desktop was blocked by a licence dialog on the dev machine; see `docs/worklog.md`).

## Running a service against the local stack

Services default to in-memory ports. To use Moto instead:

```
./scripts/dev-up.sh
Backend__Mode=Aws Aws__ServiceUrl=http://localhost:5050 dotnet run --project src/Ingest -c Release
```

The same two variables work for `src/FanOut` and `src/Dispatcher` (the Dispatcher queue consumer starts only in `Aws` mode). Bucket, table and queue names default to the ones `dev-up.sh` creates. Subscriptions and endpoints are still in-memory, so Fan-out and Dispatcher have nothing to route until the config database stores exist. Decision D39.

To share limits across Ingest tasks (rate limit, idempotency keys, monthly count) point it at Valkey: `Valkey__ConnectionString=localhost:6379`. Without it Ingest uses in-process limits. Quota enforcement stays off unless `Ingest__Quota__Enforce=true` (D42).

## Config database

`./scripts/dev-up.sh` runs the migrator (`src/Migrator`) against the local PostgreSQL and seeds the plans. To make Ingest and Fan-out read tenants, keys and subscriptions from it: `Postgres__ConnectionString="Host=localhost;Database=webhook;Username=webhook;Password=webhook"`. Until the ControlPlane exists, create a tenant, key hash (`ApiKeyHasher`: SHA-256 hex), endpoint and subscription with SQL (see `tests/IntegrationTests/Aws/PostgresConfigTests.cs` for working inserts).

## ControlPlane admin API

`Postgres__ConnectionString=... ControlPlane__AdminKey=dev-admin Secrets__LocalKey=$(openssl rand -base64 32) dotnet run --project src/ControlPlane -c Release`, then see `docs/admin-api.md` for the calls. `ControlPlane__AllowInsecureHttp=true` allows `http://` endpoint URLs for local tests.
