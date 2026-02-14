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
- `--preserve-unknown <true|false>`: preserve unknown nodes while reading/writing (default: `true`)
- `--emit-unknown-as-plain-text <true|false>`: emit plain text fallback markers for unknown nodes (default: `true`)
- `--line-ending <lf|crlf>`: line ending for text-like outputs (default: `lf`)
- `--compact`: compact single-line output when supported
- `--pretty`: pretty indented output when supported

## Rules

- Use either inline `content` or `--input-file`, not both.
- If neither inline `content` nor `--input-file` is provided, input is read from stdin.
- `--pretty` and `--compact` are mutually exclusive.
- If `--output-file` is omitted, output is written to stdout.
- For `adf` output, CLI defaults to compact JSON unless `--pretty` is passed.

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

Markdown stdin to ADF (compact by default):

```bash
cat ./notes.md | docflux markdown adf > ./notes.adf.json
```

Markdown to pretty ADF JSON:

```bash
docflux markdown adf --input-file ./notes.md --pretty --output-file ./notes.pretty.adf.json
```

Preserve unknown nodes without plain text fallback:

```bash
docflux adf markdown --input-file ./issue.adf.json --preserve-unknown true --emit-unknown-as-plain-text false
```

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
