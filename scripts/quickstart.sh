#!/usr/bin/env bash
# Try WebhookGateway end to end against a running "docker compose up":
# creates a customer (tenant), an API key and an endpoint pointing at the demo receiver, sends one event,
# and shows it arriving with a verified signature. Safe to run repeatedly (it creates a new tenant each time).
set -euo pipefail
cd "$(dirname "$0")/.."

INGEST="${INGEST_URL:-http://localhost:${WG_INGEST_PORT:-8080}}"
ADMIN="${ADMIN_URL:-http://localhost:${WG_ADMIN_PORT:-8081}}"
RECEIVER="${RECEIVER_URL:-http://localhost:${WG_RECEIVER_PORT:-9000}}"

field() { sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" | head -1; }
step() { printf '\n\033[1m%s\033[0m\n' "$*"; }

step "1/6 Waiting for the services"
for url in "$INGEST/healthz" "$ADMIN/healthz" "$RECEIVER/healthz"; do
  for _ in $(seq 1 60); do curl -fsS -o /dev/null "$url" 2>/dev/null && break; sleep 1; done
  curl -fsS -o /dev/null "$url" || { echo "Not reachable: $url - is 'docker compose up' running?"; exit 1; }
done

step "2/6 Reading the admin key generated on first start"
ADMIN_KEY="${WG_ADMIN_KEY:-$(docker compose exec -T controlplane sh -c '. /secrets/env; echo "$ControlPlane__AdminKey"' | tr -d '\r')}"
[ -n "$ADMIN_KEY" ] || { echo "Could not read the admin key. Set WG_ADMIN_KEY or see the README."; exit 1; }
admin() { curl -fsS -H "X-Admin-Key: $ADMIN_KEY" -H 'content-type: application/json' "$@"; }

step "3/6 Creating a customer (tenant)"
TENANT=$(admin -d '{"name":"Quickstart customer","plan":"basic"}' "$ADMIN/admin/v1/tenants" | field id)
echo "tenant id: $TENANT"

step "4/6 Creating an API key for the customer (shown only once)"
API_KEY=$(admin -X POST "$ADMIN/admin/v1/tenants/$TENANT/api-keys" | field key)
echo "api key:   $API_KEY"

step "5/6 Registering the demo receiver as the customer's endpoint"
ENDPOINT=$(admin -d '{"url":"http://receiver:8080/webhook","eventTypes":["order.created"]}' "$ADMIN/admin/v1/tenants/$TENANT/endpoints")
SECRET=$(echo "$ENDPOINT" | field secret)
echo "signing secret: $SECRET   (shown only once; the receiver needs it to verify signatures)"
curl -fsS -X PUT --data "$SECRET" "$RECEIVER/secret" -o /dev/null

step "6/6 Sending an event"
curl -fsS -H "Authorization: Bearer $API_KEY" -H 'content-type: application/json' \
  -d '{"type":"order.created","data":{"orderId":42,"amount":1999}}' "$INGEST/v1/events"
echo
printf 'Waiting for delivery'
for _ in $(seq 1 30); do
  BODY=$(curl -fsS "$RECEIVER/received")
  [ "$BODY" != "[]" ] && break
  printf '.'; sleep 1
done
echo
if [ "$BODY" = "[]" ]; then echo "Nothing arrived yet. Check: docker compose logs dispatcher"; exit 1; fi
echo "$BODY"
echo
echo "Done. The event went Ingest -> queue -> Fan-out -> queue -> Dispatcher -> signed POST -> receiver."
echo "Open $RECEIVER in a browser to see it, and read README.md to call the API from your own code."
