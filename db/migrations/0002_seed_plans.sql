-- Provisional plan values from docs/tenancy-and-limits.md section 1. Only the accept rates (100/500/1000) are the
-- owner's numbers; everything else is a placeholder to confirm. Plans are data: change them with a new migration or
-- through the ControlPlane, not a deploy.
INSERT INTO webhook.plans (code, name, queue_tier, accept_rate, burst_capacity, monthly_quota, delivery_rate, max_connections,
    max_connections_per_endpoint, max_backlog, max_backlog_age, retry_window, max_endpoints, per_endpoint_rate,
    max_payload_bytes, retention_days, idempotency_window)
VALUES
    ('basic',    'Basic',    'basic',    100,  200,  1000000,   300,  200,  20,  500000,   interval '24 hours', interval '24 hours', 10,  50,  262144, 30, interval '1 hour'),
    ('advanced', 'Advanced', 'advanced', 500,  1000, 20000000,  1500, 1000, 50,  5000000,  interval '24 hours', interval '3 days',   50,  200, 262144, 30, interval '24 hours'),
    ('premium',  'Premium',  'premium',  1000, 2000, 100000000, 3000, 3000, 100, 20000000, interval '24 hours', interval '7 days',   200, 500, 262144, 90, interval '24 hours')
ON CONFLICT (code) DO NOTHING;
