# Sample code

Send an event and verify a webhook, in six languages, standard library only. See the main README for the full guide.

```bash
export WEBHOOKGATEWAY_API_KEY=wgk_live_...        # a customer's API key (created with the operator API)
export WEBHOOKGATEWAY_URL=http://localhost:8080   # optional, this is the default
python3 python/send_event.py
```

`./test.sh` checks the verify code of every installed language against `test-vector.json` (a known-good signature, and a tampered body that must be rejected) and, when the stack is running and `WEBHOOKGATEWAY_API_KEY` is set, sends an event with every sample.

Prefer a browser? Both APIs have Swagger pages with "Try it out": <http://localhost:8080/docs> (events) and <http://localhost:8081/docs> (operator). `api.http` has every call for VS Code/JetBrains.

Rules the samples follow, and you should too:

- Use **one** `Idempotency-Key` per logical event and **reuse it on retries**, so a retry never creates a duplicate.
- Retry `429` and `503` only, honoring `Retry-After`; other errors will not succeed by repeating.
- Verify webhooks against the **raw** body, compare signatures in constant time, and reject timestamps older than five minutes.
