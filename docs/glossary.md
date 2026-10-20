# Glossary

- **Tenant**: a customer account; owns endpoints, subscriptions, API keys.
- **Event**: something a producer reports (`type` + `data`). Immutable.
- **Endpoint**: a subscriber HTTPS URL plus auth/signing configuration.
- **Subscription**: links a tenant's endpoint to an event type (or `*`).
- **Delivery**: the obligation to send one event to one endpoint. Has a lifecycle and many attempts.
- **Attempt**: one HTTP call for a delivery.
- **Exhausted**: all retry attempts used without success.
- **DLQ**: dead-letter queue for messages that cannot be processed.
- **Audit record**: immutable log line describing an attempt outcome.
