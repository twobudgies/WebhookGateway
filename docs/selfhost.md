# Running WebhookGateway yourself

For trying it on a laptop, `docker compose up` is enough (README quick start). This page is for running it for real: on a server, reachable from the internet, with data you care about.

> **Alpha software.** Read "What to expect" at the end before you depend on it.

## 1. What runs

`docker-compose.yml` starts: **postgres** (all state), **init** (one-shot: generates secrets), **migrator** (one-shot: applies `db/migrations`), **ingest** (:8080, the API for your applications), **fanout** and **dispatcher** (workers, no ports), **controlplane** (:8081, the operator API) and **receiver** (:9000, a demo endpoint; remove it).

Only PostgreSQL holds state. Ingest, Fan-out, Dispatcher and ControlPlane are stateless: you can restart them any time, and run several copies of each (see "Scaling").

## 2. Before you expose it

1. **Choose your own secrets.** Create a `.env` next to `docker-compose.yml` (see `.env.example`) before the first start:
   - `POSTGRES_PASSWORD` a long random password.
   - `WG_ADMIN_KEY` a long random value (this is the operator API key; whoever has it can create customers and read endpoint URLs). Generate with `openssl rand -base64 32`.
   - `WG_SECRET_KEY` base64 of 32 random bytes (`openssl rand -base64 32`). It encrypts the signing secrets of endpoints. **Back it up**: if it is lost, saved endpoint secrets cannot be decrypted and every endpoint must be recreated.
   If you start without them, random values are generated into the `secrets` volume and the admin key is printed once by the `init` container.
2. **Keep the operator port private.** Port 8081 (ControlPlane) listens on `127.0.0.1` only by default, so it is reachable from the host itself (use SSH port forwarding or a VPN to reach it from elsewhere). Only open it (`WG_ADMIN_BIND=0.0.0.0`) behind a VPN or an authenticating reverse proxy. Ingest (:8080) is the only port your applications need; if your TLS proxy runs on the same host, bind it to localhost too (`WG_INGEST_BIND=127.0.0.1`).
3. **Put TLS in front of Ingest.** The services speak plain HTTP. Use a reverse proxy that terminates TLS, for example Caddy:
   ```
   # Caddyfile
   events.example.com {
     reverse_proxy ingest:8080
   }
   ```
   run as one more compose service on the same network, with ports 80/443 published and 8080 no longer published. API keys travel in the `Authorization` header: never send them over plain HTTP across the internet.
4. **Decide who may choose endpoint URLs.** The defaults (`WG_ALLOW_INSECURE_HTTP=true`, `WG_ALLOW_PRIVATE_NETWORKS=true`) suit a private install where you control every endpoint, and they let the Dispatcher call internal services. If untrusted people can register endpoints, set both to `false`: then only public `https://` URLs are accepted, and requests to private, loopback and cloud-metadata addresses are refused (SSRF protection). The metadata address is blocked in every mode.
5. **Remove the demo receiver** (`receiver` service) once you are done trying it.

## 3. Operating

| Task | How |
|---|---|
| Logs | `docker compose logs -f <service>`. JSON lines with `component`, `level`, `message`, and ids. Ship them with your usual agent (Docker log driver, Vector, Promtail...). Payloads and secrets are never logged. |
| Health | `GET /healthz` on Ingest and ControlPlane. Compose marks them `healthy`. |
| Upgrade | `git pull && docker compose up -d --build`. The `migrator` runs first and applies new migrations once (forward-only, checksum checked). Read the release notes: this is alpha and migrations are not yet guaranteed backward compatible, so back up first. |
| Backup | Database: `docker compose exec postgres pg_dump -U webhook webhook > backup.sql` (or your usual volume snapshots). **Also back up the `secrets` volume** (or your `WG_SECRET_KEY`). Test a restore. |
| Restore | `docker compose down`, restore the `pgdata` volume or `psql < backup.sql` into a fresh database, `docker compose up`. |
| Look at the queues | `docker compose exec postgres psql -U webhook -c "select queue, count(*), min(enqueued_at) from webhook.queue_messages group by 1"`. A growing `deliveries-*` count means the Dispatcher is behind or an endpoint is slow; `*-dlq` queues hold messages that failed five times. |
| Look at failed deliveries | `select tenant_id, endpoint_id, status, attempt, last_status_code, last_error, next_attempt_at from webhook.delivery_state order by updated_at desc limit 20;` Successful deliveries leave no row on purpose. |
| Housekeeping | Automatic: an hourly job deletes expired delivery state, archived events (30 days), rollup minutes (7 days), idempotency keys and old dead-letter messages. Retention values are settings (`Backend__Postgres__*`). |

## 4. Scaling

- **More delivery capacity:** `docker compose up -d --scale dispatcher=3` (workers coordinate through the database queue; messages are never delivered twice at the same time). Same for `fanout` and `ingest` (put a load balancer in front of Ingest; its in-memory rate limit is then per instance, add Valkey for a shared one, see `docs/configuration.md`).
- **Bigger database:** this is the limit. Give PostgreSQL SSD storage, enough RAM for its working set, and regular `VACUUM` (autovacuum is on by default). The queue table is hot; keep it small by keeping consumers healthy.
- **Beyond one database:** switch to the AWS backend (`Backend__Mode=Aws`, SQS + DynamoDB + S3), designed for far higher volume. See `docs/architecture.md`.
- **Numbers:** not measured yet. Expect a few thousand events per second as the goal on one good database server, and measure with your own payloads before relying on it.

## 5. Security notes

- API keys and the admin key are secrets: treat them like passwords. Customer API keys are stored only as SHA-256 hashes and shown once; endpoint signing secrets are stored encrypted (AES-GCM with your `WG_SECRET_KEY`) and shown once.
- The database password and secret key are in the `secrets` volume and the compose environment; restrict who can run `docker` on the host.
- Customers' events pass through and are archived in your database (30 days by default) in clear text; the service must read them to route them. Encrypt the disk if that matters.
- Report vulnerabilities as described in `SECURITY.md`.

## 6. What to expect

Alpha: the pipeline is complete and tested, but it has not carried production traffic. Missing today: a dashboard, event search and replay, secret rotation, per-endpoint circuit breaker and rate limits, metrics endpoint, published images. Plan for backups, watch the logs, and pin the git commit you deploy.
