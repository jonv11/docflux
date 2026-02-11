# CLI Usage

Navigation: [Docs Index](README.md) | [Architecture](architecture.md) | [Supported Formats](supported-formats.md) | [Development Guidelines](development-guidelines.md)

`docflux` command:

```bash
docflux <source-format> <target-format> [content...] [options]
docflux list-formats
```

## Arguments

- `source-format`: input format id (`txt`, `markdown`, `html`, `xml`, `adf`)
- `target-format`: output format id (`txt`, `markdown`, `html`, `xml`, `adf`)
- `content`: optional inline input content (zero or more tokens)

## Options

- `-i`, `--input-file <path>`: read input content from file
- `-o`, `--output-file <path>`: write converted output to file

## Rules

- Use either inline `content` or `--input-file`, not both.
- If `--output-file` is omitted, output is written to stdout.

## Examples

Inline Markdown to HTML:

```bash
docflux markdown html "# Hello"
```

Markdown file to ADF file:

```bash
docflux markdown adf --input-file ./notes.md --output-file ./notes.adf.json
```

ADF JSON to Markdown:

```bash
docflux adf markdown --input-file ./issue.adf.json --output-file ./issue.md
```

HTML to Markdown (stdout):

```bash
docflux html markdown "<p>Hello <strong>DocFlux</strong></p>"
```

docflux --help
docflux list-formats

Display help:

```bash
docflux --help
```

List available formats:

```bash
docflux list-formats
```

## Release Build Output

Running a release build stages CLI runtime artifacts into repository-root `bin/` and writes `bin/docflux.cmd`.

If `bin/` is on `PATH` (Windows), `docflux` can be invoked from any console session.

Next: [Development Guidelines](development-guidelines.md)
