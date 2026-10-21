# Security policy

## Reporting a vulnerability

Please **do not open a public issue** for a security problem. Use GitHub's private reporting: on the repository page choose **Security**, then **Report a vulnerability**. Include what you found, how to reproduce it and the version or commit.

You will get an acknowledgement within 5 working days, and we will keep you updated until the issue is fixed and released. We are happy to credit you in the release notes if you wish.

## Scope

In scope: authentication and authorization, tenant isolation, request signing, SSRF protection in the Dispatcher, handling of secrets and API keys, injection, denial of service by a single tenant against others.

Out of scope: findings that need access to the host or database already, social engineering, issues in third-party services we depend on (report those upstream), missing hardening headers on the demo receiver.

## Supported versions

The project is in alpha; only the latest commit on `main` is supported.

## Design notes that matter for reviewers

- API keys are stored only as SHA-256 hashes; endpoint signing secrets are stored encrypted (envelope encryption) and shown once.
- The Dispatcher refuses to call private, loopback, link-local and metadata addresses, and does not follow redirects.
- Payload bodies, keys and secrets are never written to logs.
