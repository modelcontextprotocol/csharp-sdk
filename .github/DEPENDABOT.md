# Dependabot Configuration

This repository uses [GitHub Dependabot](https://docs.github.com/en/code-security/dependabot) for automated dependency updates.

## Configuration

The Dependabot configuration is defined in [`.github/dependabot.yml`](.github/dependabot.yml) and monitors:

### NuGet Packages
- **Schedule**: Weekly updates on Monday at 06:00 UTC
- **Target**: All packages defined in `Directory.Packages.props` (Central Package Management)
- **Grouping**: Related packages are grouped together to reduce PR noise:
  - `microsoft-extensions`: Microsoft.Extensions.* packages (15 packages)
  - `microsoft-aspnetcore`: Microsoft.AspNetCore.* packages (2 packages)
  - `microsoft-identity`: Microsoft.IdentityModel.* packages
  - `microsoft-build-tools`: Build and testing Microsoft packages
  - `system-packages`: System.* packages (9 packages)
  - `opentelemetry`: OpenTelemetry.* packages (5 packages)
  - `serilog`: Serilog.* packages (5 packages)
  - `testing`: Testing frameworks (xunit, Moq, coverlet, etc.)

### GitHub Actions
- **Schedule**: Weekly updates on Monday at 06:00 UTC  
- **Target**: All workflow files in `.github/workflows/`
- **Limit**: Maximum 5 concurrent pull requests

## How It Works

1. **Dependency Detection**: Dependabot scans `Directory.Packages.props` for NuGet package versions and `.github/workflows/*.yml` for GitHub Actions
2. **Update Checks**: Every Monday at 06:00 UTC, Dependabot checks for newer versions
3. **Grouped Updates**: Related packages are updated together in single PRs to reduce maintenance overhead
4. **Pull Request Creation**: Dependabot creates PRs with:
   - Descriptive titles and changelogs
   - Labels: `dependencies`, `nuget` or `github-actions`
   - Automatic conflict resolution when possible

## Verification

After configuration deployment, you can verify Dependabot is working by:

1. **Check Insights**: Go to repository → Insights → Dependency graph → Dependabot
2. **Monitor PRs**: Watch for PRs from `dependabot[bot]` with `dependencies` label
3. **Review Logs**: Check the Dependabot tab in repository settings for update logs

## Maintenance

- **Adding New Groups**: Update the `groups` section in `dependabot.yml` for new package families
- **Changing Schedule**: Modify the `schedule` section to adjust update frequency
- **Adjusting Limits**: Change `open-pull-requests-limit` to control concurrent PRs

## Troubleshooting

If Dependabot isn't creating updates:
1. Check the repository has Dependabot enabled in Settings → Security & analysis
2. Verify the configuration syntax using a YAML validator
3. Review Dependabot logs in repository Settings → Insights → Dependency graph → Dependabot
4. Ensure the target directories and files exist and are accessible

For more information, see the [official Dependabot documentation](https://docs.github.com/en/code-security/dependabot).