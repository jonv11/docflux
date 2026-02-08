# Contributing to DocFlux

Thanks for contributing.

## Before You Start

- Read `README.md` and docs under `docs/`.
- For non-trivial changes, open an issue to align on direction first.

## Development Setup

```bash
dotnet restore
dotnet build DocFlux.sln -c Release
dotnet test DocFlux.sln -c Release
```

## Contribution Scope

Good contributions include:

- adapter correctness improvements
- fidelity and determinism improvements
- test coverage additions
- documentation improvements

Avoid unrelated refactors in the same PR.

## Pull Request Checklist

- [ ] Code builds with no warnings/errors
- [ ] Tests added/updated and passing
- [ ] Docs updated if behavior changed
- [ ] Changes are scoped and reviewable

## Style and Quality

- Follow existing project conventions.
- Keep public API changes intentional and documented.
- Prefer clear behavior over clever implementations.

## Reporting Issues

Please include:

- input format/output format ids
- minimal reproduction input
- expected vs actual output
- DocFlux version/commit and .NET SDK version
