# README Content Checklist

This reference describes what to review and update in the shared embedded NuGet README
(`src/PACKAGE.md`) and the root repository README (`README.md`) as part of every release.

## The Shared Embedded README

All SDK packages embed the **same** README file: `src/PACKAGE.md`.

Each project packs it identically:

```xml
<None Include="..\PACKAGE.md" Pack="true" PackagePath="\README.md" />
<PackageReadmeFile>README.md</PackageReadmeFile>
```

Updating `src/PACKAGE.md` updates every package's nuget.org README at once.
There are no per-package README files; `src/ModelContextProtocol.Core/README.md` and
similar paths do not exist.

## Checklist

### 1. Package-list closure

Every shipping SDK package must be listed in the packages section of `src/PACKAGE.md`,
including packages introduced after the initial SDK launch and including the package
being viewed in its own embedded README on nuget.org.

Current packages to list:
- `ModelContextProtocol.Core`
- `ModelContextProtocol`
- `ModelContextProtocol.AspNetCore`
- `ModelContextProtocol.Extensions.Apps`

Avoid counting phrases such as "three main packages" -- they become stale when packages
are added. Use a non-counting closure such as "The SDK packages are:" instead.

When a new package is introduced, add it to the list in both `src/PACKAGE.md` and the
root `README.md` (see section below).

### 2. Badge strategy

Each package entry carries a nuget.org version badge. The correct badge endpoint depends
on the release type:

| Release type | Badge endpoint | Example |
|---|---|---|
| Prerelease series (e.g., `2.0.0-preview.*`) | `nuget/vpre/{package}` | `https://img.shields.io/nuget/vpre/ModelContextProtocol.svg` |
| Stable release | `nuget/v/{package}` | `https://img.shields.io/nuget/v/ModelContextProtocol.svg` |

`nuget/v` renders only the latest stable version and shows nothing (or a placeholder)
during a prerelease-only series. `nuget/vpre` renders the latest version including
prereleases. Switch all package badges together when the release type changes.

Verify every badge in `src/PACKAGE.md` uses the correct endpoint for this release.

### 3. Release-notes link

`src/PACKAGE.md` must contain one statement linking to the release notes for the
current version:

```markdown
See the [release notes](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v{version})
for what's new in this version.
```

Replace `{version}` with the exact version being released, including any prerelease
suffix (e.g., `2.0.0-preview.2`).

At prepare time the tag does not yet exist; the link is forward-referencing. The link
resolves once the GitHub release is published during the publish-release step.

Update this link for every release -- it must point to the tag being created, not a
prior release.

### 4. Root README.md sync

The root `README.md` (the GitHub repo readme, NOT packed into packages) has its own
package-list section. Keep it aligned with `src/PACKAGE.md`:
- Same set of packages listed
- Same non-counting closure phrasing
- Badge strategy in `README.md` may also be updated for consistency, but the root
  README is visible on GitHub (not nuget.org) so the badge choice is less critical

## Salient content to review

Beyond the structural checks above, read the current `src/PACKAGE.md` for any content
that has become stale due to changes in this release:

- Package descriptions (are they still accurate?)
- Getting-started links (do they resolve and describe the current API?)
- Code samples, if any (do they compile against the current SDK? see
  [readme-snippets.md](readme-snippets.md))
- Any version-specific notes from a prior release that should be removed or updated
