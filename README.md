# DocFlux

DocFlux is a .NET document conversion toolkit with:

- `DocFlux.Core`: library-first conversion engine
- `DocFlux.Cli`: command-line interface (`docflux`)

The pipeline is format-agnostic:

`input format -> DocFlux IR -> output format`

## Quick Navigation

- Docs index: [docs/README.md](docs/README.md)
- Architecture: [docs/architecture.md](docs/architecture.md)
- Supported formats: [docs/supported-formats.md](docs/supported-formats.md)
- CLI usage: [docs/cli-usage.md](docs/cli-usage.md)
- Development guidelines: [docs/development-guidelines.md](docs/development-guidelines.md)
- CLI project README: [src/DocFlux.Cli/README.md](src/DocFlux.Cli/README.md)
- Contributing: [CONTRIBUTING.md](CONTRIBUTING.md)
- Code of conduct: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- Security: [SECURITY.md](SECURITY.md)

## Status

DocFlux currently supports these format ids:

- `txt`
- `markdown`
- `html`
- `xml`
- `adf`

See [docs/supported-formats.md](docs/supported-formats.md) for behavior and fidelity notes per format.

## Why DocFlux

- Single conversion pipeline for all supported format pairs
- Minimal, extensible IR (document/block/inline tree)
- Graceful degradation for unsupported constructs
- Deterministic output behavior for reliable tests and CI

## Repository Layout

- `src/DocFlux.Abstractions/`: IR types and contracts
- `src/DocFlux.Core/`: converter, registry, built-in adapters
- `src/DocFlux.Cli/`: CLI entrypoint and argument handling
- `tests/DocFlux.Core.Tests/`: adapter, converter, CLI and fixture tests
- `docs/`: project documentation (start at [docs/README.md](docs/README.md))

## Prerequisites

- .NET SDK 8.0+

## Build

```bash
dotnet restore
dotnet build DocFlux.sln -c Release
```

Release build for `DocFlux.Cli` also stages runnable artifacts into repository-root `bin/` and generates `bin/docflux.cmd`.
If you add that folder to `PATH`, you can run `docflux ...` from any console on Windows.

## Test

```bash
dotnet test DocFlux.sln -c Release
```

## CLI Quick Start

Inline conversion:

```bash
docflux markdown html "# Hello DocFlux"
```

File-based conversion:

```bash
docflux markdown adf --input-file ./input.md --output-file ./output.adf.json
```

Help:

```bash
docflux --help
```

List available formats:

```bash
docflux list-formats
```

For full CLI details, see [docs/cli-usage.md](docs/cli-usage.md).

## Library Usage

```csharp
using DocFlux.Core.Conversion;

var converter = new DocFluxConverter();
var html = converter.Convert("# Title", "markdown", "html");
```

## Determinism and Test Fixtures

The test suite includes fixture-based golden tests under:

- `tests/DocFlux.Core.Tests/Fixtures/cases/`
- `tests/DocFlux.Core.Tests/Fixtures/cases.json`

This ensures richer real-world cases remain stable across changes.

## Contributing

See:

- `CONTRIBUTING.md`
- `docs/development-guidelines.md`

## Security

See `SECURITY.md` for reporting process.

## License

MIT (`LICENSE`).
