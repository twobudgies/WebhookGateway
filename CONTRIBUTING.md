# Contributing

Thanks for helping. This project aims to stay small, readable and reliable.

## Get set up

Requirements: .NET 10 SDK (pinned in `global.json`) and Docker.

```bash
./scripts/dev-up.sh    # local PostgreSQL, Valkey and an AWS emulator + migrations
make verify            # the one gate: format check, build (warnings are errors) and all tests
```

Tests that need the local stack skip themselves when it is not running, so `make verify` works without Docker, but please run it with the stack up before opening a pull request.

## How we work

1. **Open an issue first** for anything bigger than a small fix, so we can agree on the approach.
2. **One change per pull request**, branched from `main`. Keep the diff focused.
3. **Tests:** new behavior has tests; bug fixes start with a failing test.
4. **Logging:** anything you add that can fail logs it, with ids as structured fields and never payloads, secrets or keys (see `docs/logging.md`).
5. **Docs in the same pull request:** update the docs your change touches. `docs/flow-walkthrough.md` always gets a look. Non-obvious design choices are recorded in `docs/decisions.md` with the reason.
6. **Style:** the analyzers and `dotnet format` are the style guide; `make verify` enforces them. Match the surrounding code.
7. **Commits:** short imperative subject, a body that explains why.

## Layout

`src/Abstractions` holds the ports (interfaces) every adapter implements; `src/Aws`, `src/Postgres` and `src/Valkey` are adapters; `src/Ingest`, `src/FanOut`, `src/Dispatcher` and `src/ControlPlane` are the services. `CLAUDE.md` is the repository guide used by AI coding assistants and is a good map for people too.

## Reporting problems

Bugs and feature requests: GitHub issues. Security problems: see `SECURITY.md`, not a public issue.

## License

By contributing you agree that your contributions are licensed under the Apache License 2.0.
