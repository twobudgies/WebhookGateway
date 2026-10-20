# Cost model

Status: **ESTIMATE v1, approved as a planning basis** (2026-10-20); prices unverified until work item P0-1. Written from public AWS list prices as I remember them (US-region style, with a small premium assumed for `ap-south-1`). **Not verified against the current ap-south-1 price list.** Treat every number as plus or minus 30% until work item L2 (verify prices, build a spreadsheet model, replace assumptions with load-test measurements). Amounts are USD per month; 1 USD is about 88 INR.

## 1. Assumptions

- Average event 2 KB; outbound request 2.5 KB with headers; fan-out 3 deliveries per event; 2% of deliveries fail at least once.
- 730-hour month, 2.63 million seconds.
- Stage 1 choices from `architecture.md` section 8: no NAT, no WAF, no Kinesis, RDS (not Aurora), one small Valkey node, Spot for the worker.
- Free tiers ignored except where noted (SQS 1M requests, DynamoDB, Cognito).

## 2. Stage 1 fixed floor (near-zero traffic)

| Item | Sizing | ~USD/month |
|---|---|---|
| ALB | 1 load balancer + small LCU | 22 |
| Fargate: Ingest | 2 tasks, 0.25 vCPU / 0.5 GB, on-demand | 20 |
| Fargate: ControlPlane | 1 task, 0.25 vCPU / 0.5 GB | 10 |
| Fargate: worker (Fan-out + Dispatcher + retry scheduler) | 2 tasks, 0.5 vCPU / 1 GB, Spot | 14 |
| Public IPv4 addresses | ~3 for ALB, ~5 for tasks (billed hourly) | 29 |
| RDS PostgreSQL | db.t4g.small, Multi-AZ, 20 GB | 55 |
| ElastiCache Valkey | 1 x cache.t4g.micro | 12 |
| DynamoDB on-demand | tiny | 3 |
| SQS | idle polling inside free tier | 1 |
| S3 (archive, logs, portal site) | tiny | 4 |
| CloudWatch logs and metrics | low log volume, sampled | 15 |
| KMS keys, Secrets Manager, ECR, Route 53 | | 10 |
| CloudFront + Cognito | within free tiers | 2 |
| **Total** | | **~$200 to 250** (about 18 to 22 thousand INR) |

Cheaper still: RDS db.t4g.micro Single-AZ saves about $40 but removes the database failover; I would not do that for production with paying customers. Dev runs locally (Docker, LocalStack, Testcontainers); staging is created for release testing and torn down (about $5 to 10 per day while up).

Not included at launch on purpose: NAT gateway (about $33 + $0.045/GB), WAF ($5 + $1 per rule + $0.60 per million requests), Kinesis, Aurora, ElastiCache replicas.

## 3. Variable cost per million events

Fan-out 3, 2.5 KB outbound.

| Item | Calculation | USD per million events |
|---|---|---|
| SQS requests | `events` queue 0.3 requests/event + `deliveries` 0.9 requests/event (batches of 10: send, receive, delete) = 1.2 x $0.40 per million | 0.48 |
| Outbound internet data | 3 x 2.5 KB = 7.5 GB per million events x ~$0.11/GB (Mumbai, first tier; lower at volume) | 0.82 |
| DynamoDB | ~0.06 failure rows per event x ~3 writes + rollup flushes, on-demand writes | 0.16 |
| S3 + KMS requests | batched archive and result logs, compressed storage | 0.03 |
| Marginal compute | Fargate at 30k events/s is ~135 tasks for 78.8B events/month | 0.13 |
| **Total** | | **~1.6** |

Roughly half of the variable cost is **outbound data** and a third is **SQS**. The levers, in order of effect: smaller payloads and fewer headers (outbound), lower fan-out per event, compressing request bodies when subscribers accept it, then SQS batching efficiency.

## 4. Monthly bill by volume

Fixed floor of about $225 plus $1.6 per million events.

| Events per month | Average events/s | ~USD/month |
|---|---|---|
| 1 million | 0.4 | 230 |
| 10 million | 4 | 240 |
| 100 million | 40 | 390 |
| 1 billion | 400 | 1,850 |
| 13 billion (5k events/s sustained) | 5,000 | ~21k (+ ~$300 larger Valkey/RDS/logs) |
| 26 billion (10k events/s sustained) | 10,000 | ~42k |
| **79 billion (30k events/s sustained)** | 30,000 | **~$130k** (+ ~$5 to 10k for Valkey cluster, bigger RDS/Aurora, logs) |

Real traffic averages well below its peak, so a product that must handle 30,000 events/s at peak may run at a fraction of these monthly figures. If a tenant requires fixed outbound IPs, add NAT: about $33 per gateway plus $0.045/GB on that tenant's traffic (about $0.34 per million events at fan-out 3 and 2.5 KB), which should be priced into that plan.

## 5. Kinesis versus SQS for the `events` queue

We chose SQS for launch (no hourly floor). For reference, the crossover:

| | SQS standard | Kinesis provisioned |
|---|---|---|
| Floor | $0 | ~$11 per shard-month (1 shard = 1,000 records/s or 1 MB/s; 2 KB events = 500 events/s per shard) |
| Per million events | ~$0.12 (0.3 requests) | ~$0.02 (shard share + PUT payload units) |
| Replay | No (S3 archive instead) | Yes, 24 h to 7 d |
| Pacing by delay | Yes (`DelaySeconds`) | No |

Kinesis becomes cheaper for the events stage above roughly **200 million events per month** (about 80 events/s average), but the saving is small in absolute terms (about $80 per billion events). The much bigger SQS cost is the `deliveries` stage (0.9 requests per event), where Kinesis does not fit because we need per-message delay, retries and DLQs. So Kinesis is an optimization to evaluate later, not a launch decision, and it is not needed to reach 30,000 events/s.

## 6. What I would watch to keep the bill low

1. **Outbound bytes per event**: log and chart it per tenant from day one (it is the largest variable cost).
2. **SQS requests per event**: should stay near 1.2; empty receives and tiny batches raise it.
3. **Idle polling**: use long polling (20 s) and scale workers to zero-queue floors of one task.
4. **DynamoDB write volume**: should stay near 0.2 writes per event; a regression means success rows are being written.
5. **Log volume**: sample success logs; keep error logs.
6. **Public IPv4 count**: each address costs about $3.65 per month; do not give every task its own address needlessly.

## 7. Open questions for the owner

1. Target price per million events for each plan (I need it to check margin against about $1.6).
2. Will any launch customer require fixed outbound IPs or WAF?
3. Is a Multi-AZ database required from the first paying customer ($40 per month more)?
