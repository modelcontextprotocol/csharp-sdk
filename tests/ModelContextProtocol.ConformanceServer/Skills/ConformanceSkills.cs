using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Server;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.ConformanceServer.Skills;

/// <summary>
/// Builds the SEP-2640 skill fixture the conformance scenarios run against.
/// </summary>
/// <remarks>
/// <para>
/// Digests and sizes are computed from the same byte buffers the resources serve, so a manifest can never
/// disagree with the content a host reads back. Skill files are registered as concrete resources rather
/// than through a URI template so that they appear in <c>resources/list</c> carrying the name, description,
/// and MIME type SEP-2640 asks for.
/// </para>
/// </remarks>
public sealed class ConformanceSkills
{
    private const string GitWorkflowManifest =
        "---\nname: git-workflow\ndescription: Follow this team's Git conventions for branching and commits\n---\n\n# Git workflow\n";

    private const string PdfProcessingManifest =
        "---\nname: pdf-processing\ndescription: Extract, fill, and assemble PDF documents\n---\n\n# PDF processing\n";

    private const string RefundsManifest =
        "---\nname: refunds\ndescription: Process customer refund requests per company policy\n---\n\n# Refunds\n";

    private const string GeneratedReportManifest =
        "---\nname: generated-report\ndescription: Assemble a report from live data\n---\n\n# Generated report\n";

    private readonly Dictionary<string, byte[]> _content = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Name, string Description)> _manifestMetadata = new(StringComparer.Ordinal);
    private readonly List<SkillEntry> _skills = [];

    public ConformanceSkills()
    {
        AddSkill(
            "git-workflow",
            "Follow this team's Git conventions for branching and commits",
            [("SKILL.md", GitWorkflowManifest)]);

        AddSkill(
            "pdf-processing",
            "Extract, fill, and assemble PDF documents",
            [
                ("SKILL.md", PdfProcessingManifest),
                ("references/FORMS.md", "# Forms\n\nField reference for PDF form filling.\n"),
                ("scripts/extract.py", "import sys\n\nprint('extract')\n"),
                ("templates/invoice.md", "# Invoice\n"),
                ("templates/regional/eu-invoice.md", "# EU Invoice\n"),
            ]);

        AddSkill(
            "acme/billing/refunds",
            "Process customer refund requests per company policy",
            [
                ("SKILL.md", RefundsManifest),
                ("examples/email.md", "Subject: Your refund\n"),
            ]);

        // A deliberately unenumerable skill: it is served and answerable through skills/get, but carries
        // no digests and so cannot be content-bound.
        _skills.Add(new SkillEntry
        {
            Uri = "skill://generated-report/SKILL.md",
            Frontmatter = new JsonObject
            {
                ["name"] = "generated-report",
                ["description"] = "Assemble a report from live data",
            },
            Resources = SkillResources.Dynamic,
        });
        _content["skill://generated-report/SKILL.md"] = Encoding.UTF8.GetBytes(GeneratedReportManifest);
        _manifestMetadata["skill://generated-report/SKILL.md"] =
            ("generated-report", "Assemble a report from live data");
    }

    /// <summary>
    /// Creates the catalog backing <c>skills/list</c> and <c>skills/get</c>.
    /// </summary>
    /// <remarks>
    /// The page size is deliberately small so the conformance scenarios exercise cursor pagination.
    /// </remarks>
    public InMemoryMcpSkillCatalog CreateCatalog() => new(_skills, pageSize: 2);

    /// <summary>
    /// Creates one concrete resource per skill file, so the files are enumerable through
    /// <c>resources/list</c> and readable through <c>resources/read</c>.
    /// </summary>
    public IEnumerable<McpServerResource> CreateResources()
    {
        foreach (var entry in _content)
        {
            string uri = entry.Key;
            string text = Encoding.UTF8.GetString(entry.Value);
            bool isManifest = _manifestMetadata.TryGetValue(uri, out var metadata);

            yield return McpServerResource.Create(
                () => text,
                new McpServerResourceCreateOptions
                {
                    UriTemplate = uri,
                    // SEP-2640: a SKILL.md resource SHOULD carry the name and description from its own
                    // frontmatter, so a host can build its registry from resources/list alone.
                    Name = isManifest ? metadata.Name : uri[(uri.LastIndexOf('/') + 1)..],
                    Description = isManifest ? metadata.Description : "Skill supporting file",
                    MimeType = isManifest ? SkillsProtocol.SkillManifestMimeType : GuessMimeType(uri),
                });
        }
    }

    private void AddSkill(string skillPath, string description, (string Path, string Body)[] files)
    {
        string name = skillPath.Split('/')[^1];
        var resources = new List<SkillResource>();

        foreach (var (path, body) in files)
        {
            string uri = $"skill://{skillPath}/{path}";
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            _content[uri] = bytes;
            if (string.Equals(path, SkillsProtocol.SkillManifestFileName, StringComparison.Ordinal))
            {
                _manifestMetadata[uri] = (name, description);
            }

            resources.Add(new SkillResource
            {
                Uri = uri,
                Digest = ComputeDigest(bytes),
                Size = bytes.LongLength,
            });
        }

        _skills.Add(new SkillEntry
        {
            Uri = $"skill://{skillPath}/{SkillsProtocol.SkillManifestFileName}",
            Frontmatter = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
            },
            Resources = SkillResources.FromResources(resources),
        });
    }

    private string ComputeDigest(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);

        var builder = new StringBuilder("sha256:", 71);
        foreach (byte value in hash)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }

    private string GuessMimeType(string uri) => uri[(uri.LastIndexOf('.') + 1)..] switch
    {
        "md" => "text/markdown",
        "py" => "text/x-python",
        "json" => "application/json",
        _ => "text/plain",
    };
}
