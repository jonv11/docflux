# Supported Formats

Navigation: [Docs Index](README.md) | [Architecture](architecture.md) | [CLI Usage](cli-usage.md) | [Development Guidelines](development-guidelines.md)

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

- Read: Markdig-based parsing into IR with `UseAdvancedExtensions()` enabled
- Write: deterministic markdown renderer for core constructs + extension-friendly output
- Handles: headings, paragraphs, emphasis/strong/strike, links, bullet/ordered/task lists, code, quotes, tables, underline/sub/sup via HTML tags
- Markdown images (`![alt](url)`) map to link semantics in IR/ADF output by design (`[alt](url)`)
- Task-list checkboxes map to first-class task list nodes in IR and ADF

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

- Read/write for Jira Cloud ADF core nodes with graceful degradation for unsupported nodes/marks
- Read handles: paragraph, heading, bullet/ordered/task lists, blockquote, codeBlock, rule, tables, hardBreak, link/strong/em/code/strike/underline/subsup marks, emoji, mention, date, status, inlineCard
- Write emits structured ADF nodes (not text fallbacks) for the shared IR subset, including tables and richer inline marks
- Task list writes include deterministic `localId` generation when IDs are not provided in IR
- Canonicalized output structure for deterministic tests
- Jira usage guide and REST examples: [jira-adf.md](jira-adf.md)

## Fidelity and Degradation

DocFlux preserves fidelity for common shared constructs across formats.

For Markdown/ADF round trips, the guaranteed target is normalized semantic equivalence:

- `markdown -> adf -> markdown -> adf` should be idempotent at canonical JSON level
- markdown syntax form can be normalized (for example reference links can become inline links)

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

Next: [CLI Usage](cli-usage.md)
