# Commit message style

```
<type>: <subject>

<body>
```

- `<type>` is one of `feat`, `fix`, `docs`, `refactor`, `test`, or `chore`.
- `<subject>` is imperative ("Add", not "Added" or "Adds") and does not end with a period.
- The body explains what changed and why, wrapped at 72 columns. Reference issues as `Fixes #123`.

## Examples

```
feat: Add keyset pagination to the skills catalog

Offset cursors skipped entries when a skill was removed between pages.
Encode the last URI of each page instead. Fixes #42.
```

```
docs: Clarify that frontmatter must match SKILL.md verbatim
```
