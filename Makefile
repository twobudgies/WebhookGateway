# Single gate: `make verify` runs the same steps locally and in CI.
SLN := WebhookGateway.slnx
CONFIG := Release

.PHONY: restore build format-check test verify verify-fast dev-up dev-down

restore:
	dotnet restore $(SLN)

build: restore
	dotnet build $(SLN) -c $(CONFIG) --no-restore

format-check:
	dotnet format $(SLN) --verify-no-changes

test: build
	dotnet test $(SLN) -c $(CONFIG) --no-build

# Everything that must pass before a PR merges.
verify: format-check test

# Same as verify today; will skip container-based integration tests once they exist.
verify-fast: verify

dev-up:
	./scripts/dev-up.sh

dev-down:
	./scripts/dev-down.sh
