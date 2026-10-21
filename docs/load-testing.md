# Load and performance testing

Status: **plan v1 (2026-10-21)**. Replaces every capacity assumption in `architecture.md` (sections 8 and 11) and `cost-model.md` with measurements. Work items: LT-1..LT-10 in `roadmap.md`. Results go to `capacity.md` (created by LT-9).

## 1. Goal

1. Prove the pipeline works end to end under load: no lost events, no duplicate deliveries beyond at-least-once, retries and pacing behave.
2. Find the breaking point of each component and what breaks first.
3. Produce the **capacity table**: for a given workload, the minimum resources to run in production (task count and size, Valkey, DynamoDB, RDS, SQS), with headroom, and the cost per million events at that size.
4. Keep those metrics as a standing reference: re-run after big changes; compare to the last run.

## 2. Where the pieces run

| Piece | Where | Why |
|---|---|---|
| System under test (Ingest, Fan-out, Dispatcher, ControlPlane, Postgres, Valkey, SQS, DynamoDB, S3) | Test environment in AWS (P3-3) | Real network, real limits, real prices |
| **Load generator** (producer) | **ECS task in the same region**, started on demand (desired count 0 otherwise) | A laptop cannot reach thousands of events/s and home-network latency would be measured instead of ours. Several tasks give 30k events/s. |
| **Mock subscriber** (consumer endpoints) | **ECS task(s)** in the same region behind an internal ALB or Cloud Map names; behavior set per endpoint: fast, slow (latency distribution), failing (5xx rate), timeout, 429 | Dispatcher must call real HTTP endpoints; slow and failing ones are what stress connection budgets and retries |
| Local run | Developer laptop against Moto/Docker (P0-4) | Functional smoke and harness development only; **no capacity numbers are taken from it** |

The generator and the mock subscriber are the same tools used locally (P1-11), packaged as container images; the test environment just runs them on ECS. Both scale to zero when not testing, so the bill stays small.

## 3. Test data (seed)

One seed tool (`LT-2`) creates a complete tenant set through the ControlPlane API (so the config path is also exercised), driven by a profile file:

- Tenants across all plans in realistic proportion (default: 200 Basic, 40 Advanced, 10 Premium, 2 "whale" tenants at the plan's top rate).
- Endpoints per tenant (1 to 20), subscriptions with event-type filters so the average fan-out is 3 (profile option up to 10), and a share of endpoints pointing at each mock-subscriber behavior (default 85% fast, 10% slow, 4% failing, 1% timeout).
- API keys per tenant, written to a local file the generator reads. Secrets never go into git or logs.
- Event mix: sizes 1 KB / 2 KB / 10 KB / 100 KB (weights configurable), several event types, a share carrying idempotency keys (and a share of deliberate duplicates).
- Deterministic seed number so a run can be repeated with identical data.
- Tear-down command that removes the tenants and drains the queues between runs.

## 4. Scenarios

Each scenario states its pass criteria up front. Targets are initial values; they become the SLOs after the first runs.

| ID | Scenario | What it shows | Pass criteria (initial) |
|---|---|---|---|
| S1 | Smoke: 10 events/s for 5 min, all endpoints fast | Wiring end to end | 100% delivered once, p99 accept < 100 ms |
| S2 | Step load: raise accept rate in steps (500, 1k, 2k, 5k, 10k, 20k, 30k events/s), 5 min each | Where each component saturates | Per step: error rate, queue age, CPU; stop when a pass criterion fails |
| S3 | Soak at 60% of the knee for 2 h (short, then 8 h once) | Leaks, drift, DynamoDB/Valkey hot keys, credential refresh | Flat memory, flat latency |
| S4 | Spike: 0 to 5x steady in 10 s, hold 2 min | Autoscaling lag, `503`/`429` behavior, SQS buffering | No lost events; accept errors only as designed; backlog drains |
| S5 | Slow consumers: 20% of endpoints answer in 5 to 25 s | Connection budgets, fair share, one tenant not starving others | Other tenants' delivery latency unchanged (within 10%) |
| S6 | Failing consumers: 30% of endpoints return 5xx; some time out | Retry scheduler, circuit breaker, DynamoDB write load, DLQ | Retries on schedule, circuit opens/closes, no retry storm |
| S7 | Noisy tenant: one tenant at 10x its plan rate | Per-tenant limits and `429`, pacing | Others unaffected; excess rejected or paced as per plan |
| S8 | Large payloads: 100 KB to 200 KB events | Bytes-bound limits, SQS size path, S3 pointer | Throughput in MB/s reported, no payload truncation |
| S9 | Failure injection: kill a Dispatcher task, a Fan-out task, restart Valkey, throttle DynamoDB | Recovery, visibility timeouts, local-limit fallback | No lost events; duplicates bounded; recovery time recorded |
| S10 | Drain: stop producers with a large backlog | Backlog drain speed (delivery rate per task) | Time to drain = backlog / measured rate |

## 5. Metrics to collect

Collected per run and written to the run report (LT-8). The application emits them (LT-5); AWS metrics come from CloudWatch.

**Load side (generator and mock subscriber):** events sent per second, accept latency percentiles (p50/p95/p99/max), accept status counts (`202`, `429`, `5xx`), deliveries received at mock subscribers, duplicates, **end-to-end latency** (event created to first delivery received) percentiles, lost events (sent minus received after drain; must be 0).

**Per component (Ingest, Fan-out, Dispatcher, ControlPlane):** CPU %, memory, task count, requests or messages per second per task, handler latency, error rate, GC pause, thread-pool queue, open connections, bytes in/out.

**Queues:** `ApproximateNumberOfMessagesVisible`, in flight, **age of oldest message** (the scaling signal), receives and sends per second, DLQ depth, per queue (events and each delivery tier).

**Stores:** Valkey ops/s, CPU, memory, network, command latency; DynamoDB consumed read/write capacity, throttled requests, per table; RDS CPU, connections, IOPS, query latency (config cache hit rate in the app); S3 puts/s, bytes/s, archive batch size and flush latency.

**Network and cost drivers:** outbound bytes per second (data transfer is the dominant cost at scale), SQS requests per event, DynamoDB write units per event, Valkey commands per event.

## 6. Derived capacity numbers (the output)

From the step-load run (S2) and the others, per task type, define the **sustainable rate** as the highest rate where CPU is at most 60% and every pass criterion holds, and the **knee** as the first rate where one fails. Record:

| Number | Meaning |
|---|---|
| Ingest events/s per task (2 KB, per task size) | accept capacity per task |
| Fan-out messages/s per task | matching and archive capacity |
| Dispatcher deliveries/s per task, for fast and for slow consumers | delivery capacity; slow consumers cost connections, not CPU |
| Max concurrent connections per Dispatcher task | sets the connection budget |
| Valkey ops per event and ops/s ceiling of the chosen node | when to scale Valkey |
| DynamoDB WCU per delivery (success vs retry) | table mode and cost |
| Postgres connections and query rate from config cache misses | RDS size |
| Cost per million events at each tested rate | replaces `cost-model.md` estimates |

**Sizing table** (in `capacity.md`): for workloads 100, 500, 1k, 2.5k, 5k, 10k, 20k, 30k events/s at fan-out 3, list the minimum task count and size per service with 30% headroom and N+1 across two AZs, Valkey and RDS size, DynamoDB mode, expected monthly cost. The autoscaling parameters (target CPU, queue-age thresholds, min/max tasks) and production alarm thresholds are taken from these numbers.

## 7. Tooling

- **Generator:** a .NET console app (`tools/LoadGenerator`) using `HttpClient` with HTTP/2 and connection pooling, open-loop (constant arrival rate, not "as fast as possible") so latency is not hidden by coordinated omission; supports ramp/step/spike profiles; emits metrics. An existing tool (k6, Vegeta) is acceptable if the .NET app proves slower to build; decision recorded in LT-3.
- **Mock subscriber:** ASP.NET Core minimal API, behavior chosen by endpoint path or header (`/fast`, `/slow?ms=`, `/fail?rate=`, `/timeout`), counts received deliveries per event id for the lost/duplicate check.
- **Report:** a script that pulls CloudWatch metrics for the run window and writes `docs/load-test-runs/<date>-<scenario>.md` (inputs, results, pass/fail, graphs as tables).

## 8. Safety and cost

- Runs only in the test environment; never against production.
- Each run is time-boxed, with a hard stop (generator exits at the end; a budget alarm on the test account). Expected cost: a few dollars per short run; the 30k events/s step is run for minutes, not hours.
- Outbound traffic from the mock subscribers stays inside the VPC/region to avoid data-transfer charges where possible; the egress price is still measured from real runs.
- The first run of each scenario is announced in the worklog; starting the test environment and the generator needs the owner's go-ahead per `workflow.md` (AWS spend).
