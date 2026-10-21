# Working agreement (agent-driven development)

The user (product owner) finalizes the foundations once and afterwards **adds features**. Claude agents design, implement, test and open PRs. The user may review after the fact.

## Phases

1. **Foundation (user involved):** architecture, contracts, database, conventions. The foundation is final (architecture v4, contracts, `database.md` v1); open items are tracked in `roadmap.md`.
2. **Feature delivery (agent-driven):** user describes a feature; the agent does the steps below.

## Feature loop

1. Read Global docs. If the feature changes a contract/schema/architecture, update Global first (docs + `db/migrations`) in a PR to `WebhookGateway.Global`, and log it in `decisions.md`.
2. Implement in the owning repo(s) on a branch `feature/<short-name>` (or `fix/...`, `chore/...`). Never commit to `main` directly.
3. Add tests (unit; integration against PostgreSQL via Testcontainers; SQS via LocalStack where needed). Build and tests must pass locally before pushing.
4. Open a PR to `main` with: what/why, affected repos, contract or migration changes, test evidence. Cross-repo changes get linked PRs (Global first).
5. **Autonomous mode (standing approval from the user, 2026-10-20):** the agent creates branches, writes code and integration tests, fixes failures, pushes, opens a **real GitHub PR for every feature (for tracking)**, and **merges that PR into `main` itself** once build and tests pass. The user can comment on the PR at any time; the agent reads PR comments and follows up. No per-PR user review is required. The user may still comment after the fact; the agent follows up with fixes.

## Rules

- Small PRs, one concern each. Conventional commit style (`feat:`, `fix:`, `chore:`, `docs:`).
- Migrations are forward-only and backward compatible with the previous app version (expand, then contract) so deploys can roll back.
- No secrets in git. Config via environment / Secrets Manager.
- Standing approval covers: branch creation, commits, pushes, merging to `main` in the three repos, adding/fixing tests. It does **not** cover: deploying to or changing AWS resources, dropping/rewriting data, force-pushing or rewriting `main` history, or changing the licence or making the repository public/private. Ask first for those.
- Merge only when build + all tests pass. Merge via the GitHub PR (merge commit, no squash) so history shows the branch. Delete merged branches.
- Integration tests are required for any feature that touches the database, SQS or HTTP boundaries.
- Agents record non-obvious choices in `decisions.md`; unknowns are asked, not guessed.

## Tooling status

- Git remotes: `github.com/twobudgies/WebhookGateway` (monorepo, default branch `main`; old Ingest/Dispatcher repos archived), `webhookgateway-orchestrator` (private, empty), `webhookgateway-portal` (to be created).
- `gh` CLI is installed and authenticated; agents use it for PRs.
- CI (GitHub Actions: build, test, format check) TODO per repo once code exists.
