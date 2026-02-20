# Release Notes Formatting Guide

When creating or editing release notes content, follow these rules to ensure the markdown is well-formed and renders correctly on GitHub.

## GitHub Auto-Linking

GitHub automatically links `@username`, `#123`, and `@org/repo#123` references in release notes. Never use full URLs for these — use the shorthand forms:

- `@stephentoub` not `[@stephentoub](https://github.com/stephentoub)`
- `#1234` not `[#1234](https://github.com/modelcontextprotocol/csharp-sdk/pull/1234)`

This keeps the markdown source readable and avoids brittle links.

## Writing the Release Body

Always compose the full release body as a complete markdown file before uploading. Never perform incremental string replacements on the body through shell commands or API calls — this risks collapsing newlines, introducing encoding artifacts, or corrupting the markdown structure.

### Workflow for Updates

When the user requests changes to existing release notes:

1. Fetch the current release body and save it to a local file
2. Write the **entire** corrected body to a separate local file (ensuring proper line breaks between all sections, entries, and paragraphs)
3. **If the release is already published** (not a draft): run `git diff --no-index` between the original and updated files and present the raw diff output directly in the response as a fenced code block with `diff` syntax highlighting. Do not summarize or paraphrase the diff. Require explicit confirmation before uploading. Before uploading, offer to save the original body to a permanent local file, noting that GitHub does not retain prior versions of release notes.
4. Upload the complete file using `gh release edit --notes-file <file>`
5. Verify the result by fetching the body again and checking that line count and structure are intact

### Common Pitfalls

| Problem | Cause | Prevention |
|---------|-------|------------|
| Body collapses to a single line | PowerShell string manipulation strips newlines during round-trip (variable assignment → `.Replace()` → `WriteAllText`) | Always recreate the full body as a file rather than manipulating in-memory strings |
| UTF-8 BOM / garbage characters | `[System.IO.File]::WriteAllText()` with default encoding adds BOM | Use `UTF8Encoding($false)` or write via a tool that produces BOM-less UTF-8 |
| Stray unicode characters | Encoding mismatch between shell output and file write | Avoid piping `gh` output through PowerShell variable assignment for content that will be written back |

### Verification Checklist

After every release body update:

- [ ] Line count matches expected structure (~80+ lines for a typical release)
- [ ] Section headings (`## Breaking Changes`, `## What's Changed`, etc.) each appear on their own line
- [ ] Bullet entries are each on their own line
- [ ] No stray characters at the start of the body
- [ ] Preview the release on GitHub to confirm rendering
