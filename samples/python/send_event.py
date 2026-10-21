"""Send an event. Needs only the standard library. Set WEBHOOKGATEWAY_API_KEY (and WEBHOOKGATEWAY_URL if not local)."""
import json
import os
import time
import urllib.error
import urllib.request
import uuid

URL = os.environ.get("WEBHOOKGATEWAY_URL", "http://localhost:8080")
API_KEY = os.environ["WEBHOOKGATEWAY_API_KEY"]


def send_event(event_type, data, idempotency_key=None, retries=4):
    """Returns {"eventId": ...}. Retries 429 and 503 with backoff, reusing ONE idempotency key so a retry never duplicates."""
    key = idempotency_key or str(uuid.uuid4())
    body = json.dumps({"type": event_type, "data": data}).encode()
    for attempt in range(retries + 1):
        request = urllib.request.Request(
            f"{URL}/v1/events",
            data=body,
            method="POST",
            headers={"Authorization": f"Bearer {API_KEY}", "Content-Type": "application/json", "Idempotency-Key": key},
        )
        try:
            with urllib.request.urlopen(request, timeout=10) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if error.code in (429, 503) and attempt < retries:
                time.sleep(float(error.headers.get("Retry-After", 2**attempt)))
                continue
            raise


if __name__ == "__main__":
    print(send_event("order.created", {"orderId": 42, "amount": 1999}))
