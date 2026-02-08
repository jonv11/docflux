# Supported Formats

DocFlux currently supports five built-in format ids:

- `txt`
- `markdown`
- `html`
- `xml`
- `adf`

## Compatibility Matrix

All supported formats can be converted through the common IR pipeline:

`any supported input -> DocFlux IR -> any supported output`

## Per-Format Notes

### txt

- Read: line/paragraph-oriented parsing
- Write: plain text rendering with best-effort fallback for rich nodes
- Best for: simple text workflows

### markdown

- Read: Markdig-based parsing into IR
- Write: deterministic markdown renderer for core constructs
- Handles: headings, paragraphs, emphasis/strong, links, lists, code, quotes

### html

- Read: AngleSharp DOM mapping into semantic IR blocks/inlines
- Write: minimal semantic HTML with safe escaping
- Ignores layout/style semantics intentionally

### xml

- Generic XML adapter, not schema-specific
- Read: stores XML as structured unknown payload when representational mismatch exists
- Write: deterministic XML emission (stable attribute ordering)
- Non-XML-shaped IR writes into `<docflux>` wrapper

### adf

- Uses `ADFNet.Core` + `ADFNet.Json`
- Read/write for supported subset with graceful degradation for unsupported ADF nodes/marks
- Canonicalized output structure for deterministic tests

## Fidelity and Degradation

DocFlux preserves fidelity for common shared constructs across formats.

When a source construct has no IR equivalent:

- it is captured as `UnknownBlock` / `UnknownInline` when preservation is enabled
- on output, adapters can preserve metadata or degrade to readable plain text markers

## Options Affecting Behavior

- `FormatReadOptions`
  - `NormalizeLineEndings`
  - `PreserveUnknownNodes`
- `FormatWriteOptions`
  - `LineEnding`
  - `PreferSingleLine`
  - `EmitUnknownNodesAsPlainText`
  - `PreserveUnknownNodes`

See tests for exact current behavior per adapter.
