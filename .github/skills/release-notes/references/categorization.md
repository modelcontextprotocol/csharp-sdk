# Categorization Guide

## Category Definitions

### What's Changed
Feature work, bug fixes, API improvements, performance enhancements, and any other user-facing changes. This includes:
- New API surface area (new types, methods, properties)
- Bug fixes that affect runtime behavior
- Performance improvements
- Breaking changes (these appear in both "Breaking Changes" and "What's Changed")
- Changes that span code + docs (categorize based on the primary intent)

### Documentation Updates
PRs whose **sole purpose** is documentation. Examples:
- Fixing typos in docs
- Adding or improving XML doc comments (when not part of a functional change)
- Updating conceptual documentation (e.g., files in `docs/`)
- README updates
- Adding CONTRIBUTING.md or similar guides

**Important**: A PR that changes code AND updates docs should go in "What's Changed" — only pure documentation PRs belong here. However, documentation PRs should still be studied during the breaking change audit, as they may document changes that were not properly flagged as breaking.

### Repository Infrastructure Updates
PRs that maintain the development environment but don't affect the shipped product or test coverage. Examples:
- Version bumps (`Bump version to X.Y.Z`)
- CI/CD workflow changes (GitHub Actions updates)
- Dependency updates from Dependabot
- Build system changes
- Dev container or codespace configuration
- Copilot instructions updates
- NuGet/package configuration changes

**Important**: PRs that touch the `tests/` folder should never be categorized as Infrastructure — they belong in either "Test Improvements" or "What's Changed" depending on whether product code was also modified.

### Test Improvements
PRs focused on test quality, coverage, or reliability. Examples:
- Adding new tests (unit, integration, regression, conformance)
- Fixing broken or incorrect tests
- Addressing flaky tests (timing, race conditions)
- Unskipping or skipping tests
- Test infrastructure improvements (new test helpers, test base classes)

**Important**: PRs that reference "MCP conformance tests" are not automatically test-only. Examine the PR body and file changes to determine whether product code was modified to achieve conformance — if so, the PR belongs in "What's Changed." Conformance PRs should never be placed in "Repository Infrastructure Updates."

## Entry Format

Use this simplified format (GitHub auto-links `#PR` and `@user`):
```
* Description #PR by @author
```

For PRs with co-authors (harvested from `Co-authored-by` commit trailers):
```
* Description #PR by @author (co-authored by @user1 @user2)
```

For Dependabot PRs, do not acknowledge @dependabot[bot]:
```
* Bump actions/checkout from 5.0.0 to 6.0.0 #1234
```

For direct commits without an associated PR (e.g., version bumps merged directly to the branch), use the commit description and `by @author` but omit the `#PR` reference:
```
* Bump version to v0.1.0-preview.12 by @halter73
```

For Copilot-authored PRs, identify who triggered Copilot using the `copilot_work_started` timeline event on the PR. That person becomes the primary author, and @Copilot becomes a co-author:
```
* Add trace-level logging for JSON-RPC payloads #1234 by @halter73 (co-authored by @Copilot)
```

## Sorting

Sort entries within each section by **merge date** (chronological order, oldest first).

## Examples from Past Releases

### What's Changed (format example)
```
* Add Trace-level logging for JSON-RPC payloads in transports #1191 by @halter73 (co-authored by @Copilot)
* Use Process.Kill(entireProcessTree: true) on .NET for faster process termination #1187 by @stephentoub
* Include response body in HttpRequestException for transport client errors #1193 by @halter73 (co-authored by @Copilot)
* Fix race condition in SSE GET request initialization #1212 by @stephentoub
* Fix keyset pagination with monotonic UUIDv7-like task IDs #1215 by @halter73 (co-authored by @Copilot)
* Add ILoggerFactory to StreamableHttpServerTransport #1213 by @halter73 (co-authored by @Copilot)
* Add support for message-level filters to McpServer #1207 by @halter73
* Add `DistributedCacheEventStreamStore` #1136 by @MackinnonBuck
```

### Documentation Updates (format example)
```
* Fix typo in elicitation.md #1186 by @ruyut
* Fix typo in progress.md #1189 by @ruyut
* Clarify McpMetaAttribute documentation scope #1242 by @stephentoub (co-authored by @Copilot)
```

### Repository Infrastructure Updates (format example)
```
* Bump version to 0.8.0-preview.1 #1181 by @stephentoub (co-authored by @Copilot)
* Bump actions/checkout from 6.0.1 to 6.0.2 #1173
* Bump the opentelemetry-testing group with 6 updates #1174
```

### Test Improvements (format example)
```
* Remove 10 second wait from docker tests #1188 by @stephentoub
* Fix Session_TracksActivities test #1200 by @stephentoub
* Add serialization roundtrip tests for all Protocol namespace types #1289 by @stephentoub (co-authored by @Copilot)
```

### Acknowledgements (format example)
```
* @ruyut made their first contribution in #1186
* @user submitted issue #1234 (resolved by #5678)
* @user1 @user2 @user3 reviewed pull requests
```
