# Development Guidelines

## Prerequisites

- .NET SDK 8.0+

## Local Workflow

Restore and build:

```bash
dotnet restore
dotnet build DocFlux.sln -c Release
```

Run tests:

```bash
dotnet test DocFlux.sln -c Release
```

## Coding Guidelines

- Keep conversion logic in `DocFlux.Core`.
- Keep contracts and IR in `DocFlux.Abstractions`.
- Prefer explicit, deterministic behavior.
- Preserve unknown constructs when possible; degrade gracefully when not.
- Avoid speculative features not covered by current scope.

## Testing Expectations

When changing adapters, converter, or CLI:

- add/update unit tests
- include positive and negative/error path tests
- cover option behavior (`PreserveUnknownNodes`, line endings, etc.)
- add determinism checks for serializers/renderers where relevant

## Fixture-Based Golden Tests

Richer conversion test cases are stored under:

- `tests/DocFlux.Core.Tests/Fixtures/cases/`
- `tests/DocFlux.Core.Tests/Fixtures/cases.json`

To add a new fixture case:

1. Create a new folder under `Fixtures/cases/<case-name>/`.
2. Add `input.<ext>` and `expected.<ext>` files.
3. Add a case entry in `Fixtures/cases.json`:
   - `name`
   - `sourceFormat`
   - `targetFormat`
   - `input`
   - `expected`
   - `comparison` (`text` or `json`)
4. Run `dotnet test` and ensure all tests pass.

## CLI Guidelines

- Keep CLI thin and library-driven.
- Parse arguments/options via `System.CommandLine`.
- Keep core conversion behavior inside `DocFlux.Core`.
- Ensure file I/O errors are explicit and non-crashing.

## Pull Requests

- Keep changes small and reviewable.
- Include tests with each functional change.
- Update docs when user-visible behavior changes.
