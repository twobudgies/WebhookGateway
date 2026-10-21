"""Verify a webhook from WebhookGateway (Standard Webhooks). Use the RAW request body, not re-serialized JSON."""
import base64
import hashlib
import hmac
import os
import time


def verify(secret, webhook_id, timestamp, signature_header, body, tolerance_seconds=300):
    if abs(time.time() - int(timestamp)) > tolerance_seconds:  # reject old or future timestamps: stops replays
        return False
    key = base64.b64decode(secret.removeprefix("whsec_"))
    signed = f"{webhook_id}.{timestamp}.{body}".encode()
    expected = base64.b64encode(hmac.new(key, signed, hashlib.sha256).digest()).decode()
    # The header can hold several signatures ("v1,abc v1,def") while a secret is being rotated.
    return any(hmac.compare_digest(expected, part[3:]) for part in signature_header.split() if part.startswith("v1,"))


if __name__ == "__main__":  # used by samples/test.sh with the fixed test vector
    ok = verify(
        os.environ["SECRET"], os.environ["WEBHOOK_ID"], os.environ["TIMESTAMP"],
        os.environ["SIGNATURE"], os.environ["BODY"], int(os.environ.get("TOLERANCE_SECONDS", "300")),
    )
    print("VALID" if ok else "INVALID")
