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
