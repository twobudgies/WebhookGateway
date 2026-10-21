# WebhookGateway

An open-source **webhook delivery service**: your application sends one event to a single API, and WebhookGateway delivers it reliably to every subscribed endpoint with signing, retries, rate limits and a record of what happened. Built on .NET 10, PostgreSQL and (optionally) Valkey.

> **Status: alpha.** The core pipeline works and is covered by tests, but there is no one-command Docker Compose stack yet. It is being built next (see the [roadmap](docs/roadmap.md), Phase O). Not ready for production use.

## What it does

```
Your app --POST /v1/events--> Ingest --> queue --> Fan-out --> queue --> Dispatcher --signed POST--> your customers' endpoints
```

- **Accepts events** over a simple HTTP API with API-key authentication, per-tenant rate limits, payload limits and idempotency keys.
- **Routes** each event to the endpoints whose subscriptions match its type.
- **Delivers** with [Standard Webhooks](https://www.standardwebhooks.com/)-style HMAC signatures, SSRF protection (no calls to private or loopback addresses) and a per-plan retry schedule from 24 hours up to 7 days.
- **Remembers failures**, not successes: only failed and retrying deliveries are stored, which keeps the cost per event very low.
- **Multi-tenant** with plans (rate, quota, endpoints, retention) kept as data; an operator API manages tenants, keys, endpoints and subscriptions.
- **Observable:** structured JSON logs with a `component` field on every line, rollup counters per endpoint and minute.

## Try it from source

You need the .NET 10 SDK and Docker.

```bash
./scripts/dev-up.sh         # PostgreSQL, Valkey and an AWS emulator, then migrations and sample queues/tables
make verify                 # format check, build and all tests (including end to end tests against the stack)
```

The end-to-end test `tests/IntegrationTests/Aws/PipelineEndToEndTests.cs` shows the full path: create a tenant and endpoint, post an event, watch it routed and delivered. `dev/README.md` explains how to run each service against the local stack, and `docs/admin-api.md` shows how to create tenants and keys with the operator API.

## Documentation

Start with [`docs/flow-walkthrough.md`](docs/flow-walkthrough.md) (one event end to end, with code pointers and what is built).

| | |
|---|---|
| [`docs/architecture.md`](docs/architecture.md) | Design, components, scale path |
| [`docs/admin-api.md`](docs/admin-api.md) | Operator API with an onboarding example |
| [`docs/configuration.md`](docs/configuration.md) | Every setting, container images |
| [`docs/data-plane.md`](docs/data-plane.md), [`docs/database.md`](docs/database.md) | Queues, state, archive; configuration database |
| [`docs/logging.md`](docs/logging.md) | Log format and rules |
| [`docs/decisions.md`](docs/decisions.md) | Why things are the way they are |
| [`docs/roadmap.md`](docs/roadmap.md) | What is next |

## Contributing

Contributions are welcome: read [CONTRIBUTING.md](CONTRIBUTING.md). Report security problems privately as described in [SECURITY.md](SECURITY.md). Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## License

Apache License 2.0, see [LICENSE](LICENSE) and [NOTICE](NOTICE).
