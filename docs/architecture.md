# Architecture

## Overview

DocFlux uses a library-first architecture with a shared intermediate representation (IR):

`input adapter -> DocFlux IR -> output adapter`

This avoids pair-specific converters (`markdown->html`, `adf->xml`, etc.) and keeps behavior consistent as new formats are added.

## Projects

- `DocFlux.Abstractions`
  - IR node types
  - adapter and registry contracts
  - read/write option contracts
- `DocFlux.Core`
  - `DocFluxConverter` conversion service
  - `FormatRegistry` for adapter lookup
  - built-in adapters (`txt`, `markdown`, `html`, `xml`, `adf`)
- `DocFlux.Cli`
  - command-line argument handling (`System.CommandLine`)
  - file input/output orchestration

## IR Model

The IR is a tree:

- Root: `DocDocument`
- Blocks: paragraph, heading, lists, code block, quote block, thematic break, unknown block
- Inlines: text, line break, inline code, link, emphasis, strong, unknown inline

Unknown nodes (`UnknownBlock`, `UnknownInline`) are used as escape hatches for unsupported source constructs.

## Conversion Flow

1. Resolve input adapter by format id.
2. Read source text into `DocDocument`.
3. Resolve output adapter by format id.
4. Write `DocDocument` to destination text.

`DocFluxConverter` exposes:

- `Convert(input, inFormatId, outFormatId, options)`
- `ConvertToDocument(input, inFormatId, readOptions)`
- `ConvertFromDocument(document, outFormatId, writeOptions)`

## Determinism

DocFlux aims for deterministic output so tests and CI are stable:

- normalized line ending handling via options
- stable serialization patterns where practical
- fixture-based golden tests for richer cases

## Extension Model

Add a new format by implementing `IFormatAdapter` and registering it in a registry.

Minimum requirements:

- lower-case `FormatId`
- `Read` implementation to IR
- `Write` implementation from IR
- explicit behavior for unsupported constructs (preserve vs degrade)
- tests (smoke, roundtrip, options, determinism where applicable)
