using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Validates skill entries against the structural requirements of the Skills extension specification, so that
/// a server never publishes an entry a conforming host would refuse to load.
/// </summary>
internal static class SkillValidation
{
    private const string SkillFileSuffix = "/" + SkillsProtocol.SkillFileName;

    /// <summary>
    /// Returns the skill root (the <c>SKILL.md</c> URI with its <c>/SKILL.md</c> suffix removed), or throws if
    /// <paramref name="skillUri"/> does not end in <c>/SKILL.md</c>.
    /// </summary>
    public static string GetSkillRoot(string skillUri, string paramName)
    {
        if (string.IsNullOrEmpty(skillUri))
        {
            throw new ArgumentException("A skill URI must not be null or empty.", paramName);
        }

        if (!skillUri.EndsWith(SkillFileSuffix, StringComparison.Ordinal) || skillUri.Length == SkillFileSuffix.Length)
        {
            throw new ArgumentException(
                $"The skill URI '{skillUri}' must be the URI of the skill's {SkillsProtocol.SkillFileName}, ending in '{SkillFileSuffix}'.",
                paramName);
        }

        return skillUri.Substring(0, skillUri.Length - SkillFileSuffix.Length);
    }

    /// <summary>
    /// Returns the final path segment of a skill root, which the specification requires to equal the skill's name.
    /// </summary>
    public static string GetNameSegment(string skillRoot)
    {
        int slash = skillRoot.LastIndexOf('/');
        return slash < 0 ? skillRoot : skillRoot.Substring(slash + 1);
    }

    /// <summary>
    /// Returns whether <paramref name="name"/> satisfies the Agent Skills naming rules: 1 to 64 characters,
    /// lowercase letters, digits, and hyphens, with no leading, trailing, or consecutive hyphens.
    /// </summary>
    public static bool IsValidSkillName(string name)
    {
        if (name.Length is 0 or > 64)
        {
            return false;
        }

        char previous = '-';
        foreach (char c in name)
        {
            bool isAlphanumeric = c is (>= 'a' and <= 'z') or (>= '0' and <= '9');
            if (!isAlphanumeric && c != '-')
            {
                return false;
            }

            if (c == '-' && previous == '-')
            {
                return false;
            }

            previous = c;
        }

        return previous != '-';
    }

    /// <summary>
    /// Returns whether <paramref name="digest"/> has the form <c>sha256:{hex}</c> with 64 lowercase hexadecimal digits.
    /// </summary>
    public static bool IsValidDigest(string? digest)
    {
        const int HexLength = 64;
        if (digest is null ||
            digest.Length != SkillsProtocol.DigestPrefix.Length + HexLength ||
            !digest.StartsWith(SkillsProtocol.DigestPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (int i = SkillsProtocol.DigestPrefix.Length; i < digest.Length; i++)
        {
            if (digest[i] is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks that <paramref name="uri"/> is an absolute URI whose path has no empty, <c>.</c>, or <c>..</c>
    /// segments and no query or fragment, so that a prefix comparison against a skill root is meaningful.
    /// </summary>
    public static void ValidateUriShape(string uri, string what, string paramName)
    {
        int schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
        {
            throw new ArgumentException($"{what} '{uri}' must be an absolute URI with a scheme, such as skill://name/SKILL.md.", paramName);
        }

        if (uri.IndexOf('?') >= 0 || uri.IndexOf('#') >= 0)
        {
            throw new ArgumentException($"{what} '{uri}' must not contain a query or fragment.", paramName);
        }

        string path = uri.Substring(schemeEnd + 3);
        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                throw new ArgumentException($"{what} '{uri}' must not contain empty, '.', or '..' path segments.", paramName);
            }
        }
    }

    /// <summary>
    /// Returns a copy of <paramref name="skill"/> that shares no mutable state with it.
    /// </summary>
    public static Skill Snapshot(Skill skill) => new()
    {
        Uri = skill.Uri,
        Frontmatter = (JsonObject)skill.Frontmatter.DeepClone(),
        Resources = skill.Resources.IsDynamic
            ? SkillResources.Dynamic
            : SkillResources.FromResources(skill.Resources.Resources!.Select(static r => new SkillResource { Uri = r.Uri, Digest = r.Digest, Size = r.Size })),
    };

    /// <summary>
    /// Validates a complete skill entry, throwing <see cref="ArgumentException"/> describing the first violation found.
    /// </summary>
    public static void Validate(Skill skill, string paramName)
    {
        if (skill is null)
        {
            throw new ArgumentNullException(paramName);
        }

        string root = GetSkillRoot(skill.Uri, paramName);
        ValidateUriShape(skill.Uri, "The skill URI", paramName);
        string nameSegment = GetNameSegment(root);

        if (skill.Frontmatter is null)
        {
            throw new ArgumentException($"Skill '{skill.Uri}' has no frontmatter.", paramName);
        }

        string? name = skill.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException($"Skill '{skill.Uri}' must declare a non-empty string 'name' in its frontmatter.", paramName);
        }

        if (!IsValidSkillName(name!))
        {
            throw new ArgumentException(
                $"Skill '{skill.Uri}' has the name '{name}', which does not satisfy the Agent Skills naming rules " +
                "(1 to 64 lowercase letters, digits, and single hyphens, not starting or ending with a hyphen).",
                paramName);
        }

        if (!string.Equals(name, nameSegment, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Skill '{skill.Uri}' has the name '{name}', but the path segment preceding /{SkillsProtocol.SkillFileName} is '{nameSegment}'. " +
                "The specification requires them to be equal.",
                paramName);
        }

        string? description = skill.Description;
        if (string.IsNullOrEmpty(description))
        {
            throw new ArgumentException($"Skill '{skill.Uri}' must declare a non-empty string 'description' in its frontmatter.", paramName);
        }

        if (description!.Length > MaxDescriptionLength)
        {
            throw new ArgumentException(
                $"Skill '{skill.Uri}' has a description of {description.Length} characters; the Agent Skills specification allows at most {MaxDescriptionLength}.",
                paramName);
        }

        ValidateOptionalString(skill, "license", maxLength: null, paramName);
        ValidateOptionalString(skill, "compatibility", MaxCompatibilityLength, paramName);
        ValidateOptionalString(skill, "allowed-tools", maxLength: null, paramName);

        if (skill.Frontmatter.TryGetPropertyValue("metadata", out var metadataNode) && metadataNode is not null)
        {
            if (metadataNode is not JsonObject metadata)
            {
                throw new ArgumentException($"Skill '{skill.Uri}' has a 'metadata' frontmatter field that is not a mapping; the Agent Skills specification requires a map from string keys to string values.", paramName);
            }

            foreach (var entry in metadata)
            {
                if (entry.Value is not JsonValue value || !value.TryGetValue(out string? _))
                {
                    throw new ArgumentException(
                        $"Skill '{skill.Uri}' has a 'metadata.{entry.Key}' frontmatter value that is not a string; the Agent Skills specification requires string values. Quote it in SKILL.md if it is meant literally.",
                        paramName);
                }
            }
        }

        if (skill.Resources is null)
        {
            throw new ArgumentException($"Skill '{skill.Uri}' has no resources manifest. Use SkillResources.Dynamic for generated content.", paramName);
        }

        if (skill.Resources.IsDynamic)
        {
            return;
        }

        var resources = skill.Resources.Resources!;
        if (resources.Count == 0)
        {
            throw new ArgumentException(
                $"Skill '{skill.Uri}' has an empty resources manifest. A manifest must list every file of the skill, {SkillsProtocol.SkillFileName} included.",
                paramName);
        }

        if (resources.Count > SkillsProtocol.MaxResourcesPerSkill)
        {
            throw new ArgumentException(
                $"Skill '{skill.Uri}' lists {resources.Count} files, exceeding the limit of {SkillsProtocol.MaxResourcesPerSkill} per skill.",
                paramName);
        }

        string rootPrefix = root + "/";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool hasSkillFile = false;
        long totalSize = 0;

        foreach (var resource in resources)
        {
            if (resource is null)
            {
                throw new ArgumentException($"Skill '{skill.Uri}' has a null entry in its resources manifest.", paramName);
            }

            if (string.IsNullOrEmpty(resource.Uri))
            {
                throw new ArgumentException($"Skill '{skill.Uri}' has a resource with a null or empty URI.", paramName);
            }

            ValidateUriShape(resource.Uri, $"Skill '{skill.Uri}' lists the resource", paramName);
            if (!resource.Uri.StartsWith(rootPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Skill '{skill.Uri}' lists the resource '{resource.Uri}', which is not within the skill's directory '{root}'.",
                    paramName);
            }

            if (!seen.Add(resource.Uri))
            {
                throw new ArgumentException($"Skill '{skill.Uri}' lists the resource '{resource.Uri}' more than once.", paramName);
            }

            if (!IsValidDigest(resource.Digest))
            {
                throw new ArgumentException(
                    $"Skill '{skill.Uri}' lists the resource '{resource.Uri}' with the digest '{resource.Digest}', " +
                    "which is not of the form 'sha256:' followed by 64 lowercase hexadecimal digits.",
                    paramName);
            }

            if (resource.Size < 0)
            {
                throw new ArgumentException($"Skill '{skill.Uri}' lists the resource '{resource.Uri}' with a negative size.", paramName);
            }

            if (resource.Size > SkillsProtocol.MaxTotalSizeBytes - totalSize)
            {
                throw new ArgumentException(
                    $"Skill '{skill.Uri}' totals more than the limit of {SkillsProtocol.MaxTotalSizeBytes} bytes per skill.",
                    paramName);
            }

            totalSize += resource.Size;
            hasSkillFile |= string.Equals(resource.Uri, skill.Uri, StringComparison.Ordinal);
        }

        if (!hasSkillFile)
        {
            throw new ArgumentException(
                $"Skill '{skill.Uri}' does not list its own {SkillsProtocol.SkillFileName} in its resources manifest.",
                paramName);
        }

    }

    private const int MaxDescriptionLength = 1024;
    private const int MaxCompatibilityLength = 500;

    private static void ValidateOptionalString(Skill skill, string key, int? maxLength, string paramName)
    {
        if (!skill.Frontmatter.TryGetPropertyValue(key, out var node) || node is null)
        {
            return;
        }

        if (node is not JsonValue value || !value.TryGetValue(out string? text))
        {
            throw new ArgumentException($"Skill '{skill.Uri}' has a '{key}' frontmatter field that is not a string.", paramName);
        }

        if (text!.Length == 0 || (maxLength is { } max && text.Length > max))
        {
            throw new ArgumentException(
                $"Skill '{skill.Uri}' has a '{key}' frontmatter field of {text.Length} characters; the Agent Skills specification requires 1 to {maxLength ?? int.MaxValue}.",
                paramName);
        }
    }
}
