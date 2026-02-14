# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- CLI stdin fallback when inline content and `--input-file` are omitted.
- CLI flags for unknown-node handling and output formatting:
  - `--preserve-unknown`
  - `--emit-unknown-as-plain-text`
  - `--line-ending`
  - `--pretty`
  - `--compact`
- Jira ADF documentation with REST examples and conversion limitations.
- Jira ADF fixture coverage for panel/expand/media-like content, mentions, inline cards, nested tables, and complex fence info strings.
- Tag-driven release workflow for NuGet, GitHub Releases, single-file CLI binaries, and GHCR Docker images.

### Changed

- CLI defaults ADF output to compact JSON unless `--pretty` is explicitly set.
- `AdfFormatAdapter` now honors `FormatWriteOptions.PreferSingleLine` for compact vs indented JSON output.
