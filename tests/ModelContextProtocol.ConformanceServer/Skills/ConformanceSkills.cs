using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.ConformanceServer.Skills;

/// <summary>
/// Builds the SEP-2640 skill fixture the conformance scenarios run against.
/// </summary>
/// <remarks>
/// <para>
/// The three static skills are authored through <see cref="McpServerSkillPackage"/>, so their frontmatter is read from
/// their <c>SKILL.md</c> and their manifests are computed from the same bytes their resources serve. A fourth,
/// dynamically generated skill is included in the same collection: it is listed and answerable through
/// <c>skills/get</c>, but its dynamic manifest carries no digests and so cannot be content-bound.
/// </para>
/// <para>
/// Registered skills are returned on the initial page; application-owned sources may implement pagination.
/// </para>
/// </remarks>
public static class ConformanceSkills
{
    private const string GeneratedReportUri = "skill://generated-report/SKILL.md";

    private static readonly McpServerSkillPackage[] s_packages =
    [
        McpServerSkillPackage.Create(
            [
                SkillFile("git-workflow", "Follow this team's Git conventions for branching and commits", "# Git workflow\n"),
            ], new McpServerSkillPackageCreateOptions { Uri = "skill://git-workflow/SKILL.md" }),

        McpServerSkillPackage.Create(
            [
                SkillFile("pdf-processing", "Extract, fill, and assemble PDF documents", "# PDF processing\n"),
                McpServerSkillFile.FromText("references/FORMS.md", "# Forms\n\nField reference for PDF form filling.\n"),
                McpServerSkillFile.FromText("scripts/extract.py", "import sys\n\nprint('extract')\n"),
                McpServerSkillFile.FromText("templates/invoice.md", "# Invoice\n"),
                McpServerSkillFile.FromText("templates/regional/eu-invoice.md", "# EU Invoice\n"),
            ], new McpServerSkillPackageCreateOptions { Uri = "skill://pdf-processing/SKILL.md" }),

        McpServerSkillPackage.Create(
            [
                SkillFile("refunds", "Process customer refund requests per company policy", "# Refunds\n"),
                McpServerSkillFile.FromText("examples/email.md", "Subject: Your refund\n"),
            ], new McpServerSkillPackageCreateOptions { Uri = "skill://acme/billing/refunds/SKILL.md" }),
    ];

    private static readonly Skill s_generatedReport = new()
    {
        Uri = GeneratedReportUri,
        Frontmatter = Frontmatter("generated-report", "Assemble a report from live data"),
        Resources = SkillResources.Dynamic,
    };

    /// <summary>
    /// Creates the primitives backing <c>skills/list</c> and <c>skills/get</c>.
    /// </summary>
    public static IEnumerable<McpServerSkill> CreateSkills() => [.. s_packages.Select(package => package.Skill), McpServerSkill.Create(s_generatedReport)];

    /// <summary>
    /// Creates the resources serving every skill file, so the files are enumerable through <c>resources/list</c>
    /// and readable through <c>resources/read</c>.
    /// </summary>
    public static IEnumerable<McpServerResource> CreateResources()
    {
        foreach (var package in s_packages)
        {
            foreach (var resource in package.Resources)
            {
                yield return resource;
            }
        }

        yield return McpServerResource.Create(
            () => $"---\nname: generated-report\ndescription: Assemble a report from live data\n---\n\n# Generated report\n\nGenerated at {DateTimeOffset.UtcNow:O}.\n",
            new McpServerResourceCreateOptions
            {
                UriTemplate = GeneratedReportUri,
                Name = "generated-report",
                Description = "Assemble a report from live data",
                MimeType = "text/markdown",
            });
    }

    private static JsonObject Frontmatter(string name, string description) => new()
    {
        ["name"] = name,
        ["description"] = description,
    };

    private static McpServerSkillFile SkillFile(string name, string description, string body) =>
        McpServerSkillFile.FromText(SkillsProtocol.SkillFileName, $"---\nname: {name}\ndescription: {description}\n---\n\n{body}");
}
