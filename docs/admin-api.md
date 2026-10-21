# Admin API (ControlPlane, operator use)

Status: **v1, implemented** (D46). Tenants are created by an operator in v1 (no self-service signup). The customer-facing portal API (`/portal/v1`, Cognito login) is separate and comes later (P2-2, P2-3).

**Interactive docs:** with the stack running, <http://localhost:8081/docs> (Swagger UI, click Authorize and paste the admin key) and the OpenAPI definition at `/openapi/v1.json`. Turn them off with `ApiDocs__Enabled=false` (`WG_API_DOCS=false` in Compose).

Base path `/admin/v1`, header `X-Admin-Key: <operator key>` on every call. Without `ControlPlane:AdminKey` configured the API answers `503 admin_disabled`; a wrong key is `401` and logged as a warning. Errors use `{ "error": { "code", "message" } }`. Changes become visible to Ingest, Fan-out and Dispatcher within their cache time (30 s).

| Call | What it does |
|---|---|
| `POST /tenants` `{name, plan}` | Create a tenant on `basic`, `advanced` or `premium`. |
| `GET /tenants/{id}` | Read a tenant. |
| `PATCH /tenants/{id}` `{status?, plan?}` | `status`: `active`, `suspended` (stops ingest, pauses delivery), `closed`. A plan change is refused (`409 plan_downgrade_refused`) if the tenant has more endpoints than the new plan allows. |
| `POST /tenants/{id}/api-keys` | Create an API key. **The full key is in this response only**; the database keeps a SHA-256 hash and a 12-character prefix. |
| `GET /tenants/{id}/api-keys` | List keys (prefix, created, revoked). |
| `DELETE /tenants/{id}/api-keys/{keyId}` | Revoke a key. |
| `POST /tenants/{id}/endpoints` `{url, eventTypes[]}` | Create an endpoint with its subscriptions. URL must be https and pass the SSRF checks (same validator the Dispatcher uses before sending). `409 endpoint_limit` at the plan's cap. **The signing secret (`whsec_...`) is in this response only**; it is stored encrypted (D45). |
| `GET /tenants/{id}/endpoints` | List endpoints with their event types. |
| `PATCH /tenants/{id}/endpoints/{endpointId}` `{url?, status?}` | Change the URL or disable/enable (`active`, `disabled`). |
| `PUT /tenants/{id}/endpoints/{endpointId}/subscriptions` `{eventTypes[]}` | Replace the endpoint's event types (`*` matches all). |

## Onboard a customer

```
KEY=...   # the operator key
curl -s -H "X-Admin-Key: $KEY" -H 'content-type: application/json' -d '{"name":"Acme","plan":"basic"}' $CP/admin/v1/tenants
curl -s -X POST -H "X-Admin-Key: $KEY" $CP/admin/v1/tenants/<tenantId>/api-keys          # give the key to the customer
curl -s -H "X-Admin-Key: $KEY" -H 'content-type: application/json' \
  -d '{"url":"https://customer.example.com/hook","eventTypes":["order.created"]}' $CP/admin/v1/tenants/<tenantId>/endpoints   # give the secret to the customer
```

Not in v1 of this API: secret rotation (two active secrets), per-tenant overrides, member management, usage reads. They are listed on the roadmap.
