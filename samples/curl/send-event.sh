#!/usr/bin/env bash
# Send one event. Set WEBHOOKGATEWAY_API_KEY (and WEBHOOKGATEWAY_URL if not local).
set -euo pipefail
URL="${WEBHOOKGATEWAY_URL:-http://localhost:8080}"
curl -sS -w '  (HTTP %{http_code})' -X POST "$URL/v1/events" \
  -H "Authorization: Bearer $WEBHOOKGATEWAY_API_KEY" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: order-42-created" \
  -d '{"type":"order.created","data":{"orderId":42,"amount":1999}}'
echo
