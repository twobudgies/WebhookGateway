#!/usr/bin/env bash
# Checks the sample code with the toolchains installed on this machine:
#   - every verify-webhook sample must accept a known-good signature and reject a tampered body;
#   - with a running stack (docker compose up) and WEBHOOKGATEWAY_API_KEY set, every send-event sample must get an eventId back (HTTP 202).
# Languages whose tool is not installed are skipped, not failed.
set -uo pipefail
cd "$(dirname "$0")"
V=test-vector.json
field() { python3 -c "import json,sys;print(json.load(open('$V'))['$1'])"; }
export SECRET="$(field secret)" WEBHOOK_ID="$(field webhookId)" TIMESTAMP="$(field timestamp)" SIGNATURE="$(field signatureHeader)" BODY="$(field body)" TOLERANCE_SECONDS=999999999
fail=0; ran=0

check() { # name, command...
  local name="$1"; shift
  local ok bad
  ok=$("$@" 2>&1 | tail -1); bad=$(BODY='{"tampered":true}' "$@" 2>&1 | tail -1)
  if [ "$ok" = VALID ] && [ "$bad" = INVALID ]; then echo "ok   verify $name"; ran=$((ran+1)); else echo "FAIL verify $name (got '$ok' / '$bad')"; fail=1; fi
}
have() { command -v "$1" >/dev/null 2>&1; }

have python3 && check python python3 python/verify_webhook.py || echo "skip python"
have ruby && check ruby ruby ruby/verify_webhook.rb || echo "skip ruby"
have go && check go go run go/verify_webhook.go || echo "skip go"
have java && check java java java/VerifyWebhook.java || echo "skip java"
have dotnet && check csharp dotnet run csharp/verify-webhook.cs || echo "skip csharp"

if [ -n "${WEBHOOKGATEWAY_API_KEY:-}" ] && curl -fsS -o /dev/null "${WEBHOOKGATEWAY_URL:-http://localhost:8080}/healthz" 2>/dev/null; then
  send() { local name="$1"; shift; if "$@" 2>&1 | grep -q 'eventId'; then echo "ok   send   $name"; else echo "FAIL send   $name"; fail=1; fi; }
  send curl bash curl/send-event.sh
  have python3 && send python python3 python/send_event.py
  have ruby && send ruby ruby ruby/send_event.rb
  have go && send go go run go/send_event.go
  have java && send java java java/SendEvent.java
  have dotnet && send csharp dotnet run csharp/send-event.cs
else
  echo "skip send samples (start the stack and set WEBHOOKGATEWAY_API_KEY to test them)"
fi
exit $fail
