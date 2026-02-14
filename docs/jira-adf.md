# Jira ADF Guide

Navigation: [Docs Index](README.md) | [Supported Formats](supported-formats.md) | [CLI Usage](cli-usage.md)

Use this guide when your main workflow is Markdown content authored locally and posted to Jira Cloud as Atlassian Document Format (ADF).

## Markdown -> ADF JSON

Convert a Markdown file to ADF JSON:

```bash
docflux markdown adf --input-file ./issue.md --output-file ./issue.adf.json
```

Pipe Markdown from stdin:

```bash
cat ./issue.md | docflux markdown adf > issue.adf.json
```

CLI defaults to compact JSON for `adf` output. Use `--pretty` for indented output.

## Post ADF to Jira REST API

Example issue create request:

```bash
curl --request POST \
  --url "https://your-domain.atlassian.net/rest/api/3/issue" \
  --user "you@example.com:${JIRA_API_TOKEN}" \
  --header "Accept: application/json" \
  --header "Content-Type: application/json" \
  --data @- <<'JSON'
{
  "fields": {
    "project": { "key": "DOC" },
    "summary": "DocFlux generated issue",
    "issuetype": { "name": "Task" },
    "description": {
      "type": "doc",
      "version": 1,
      "content": [
        {
          "type": "paragraph",
          "content": [
            { "type": "text", "text": "Created from DocFlux output." }
          ]
        }
      ]
    }
  }
}
JSON
```

To use converted output directly, copy the contents of `issue.adf.json` into `fields.description`.

## Unknown Node Behavior

Behavior is controlled by:

- `--preserve-unknown <true|false>`
- `--emit-unknown-as-plain-text <true|false>`

| preserve | emit-unknown-as-plain-text | Result |
| --- | --- | --- |
| `true` | `true` | Unknown content degrades to readable markers. |
| `true` | `false` | Unknown content is preserved as structured unknown payload when possible. |
| `false` | `true` | Unknown payload is not preserved; output contains readable fallback markers where supported. |
| `false` | `false` | Unknown content is omitted where no mapping exists. |

## Code Fence Language Handling

When converting Markdown fenced code blocks to ADF `codeBlock`:

- only the first token from the fence info string is treated as language
- extra attributes (for example `{linenumbers=true}`) are ignored for ADF attrs
- `sh` and `shell` normalize to `bash` in ADF output

Example:

````markdown
```sh {linenumbers=true}
echo "hello"
```
````

emits ADF with `attrs.language = "bash"`.

## Task Lists (Markdown Checkboxes)

Markdown task syntax maps to Jira task nodes:

```
- [ ] open task
- [x] completed task
```

emits ADF `taskList` with `taskItem`/`blockTaskItem` children and `attrs.state` set to `TODO`/`DONE`.

When `localId` is missing in IR, DocFlux generates deterministic IDs (for example `docflux-tasklist-0001`, `docflux-taskitem-0001`) so repeated conversions remain stable.

## Jira-Oriented Support Snapshot

Current first-class coverage includes:

- paragraph, heading, bullet/ordered/task lists, blockquote, codeBlock, rule/thematicBreak
- tables
- emoji, mention, date, status, inlineCard (smart link inline form)
- link/strong/em/code/strike/underline/subsup marks

Common Jira-specific nodes that are not first-class IR nodes in this cycle (for example panel/expand/media-like blocks) are treated as unknown nodes and follow the unknown-node behavior settings above.

## Known Lossiness

This workflow targets normalized content equivalence, not markdown syntax identity:

- markdown reference-link form may round-trip as inline links
- markdown images are intentionally represented as links for ADF interoperability in this phase
