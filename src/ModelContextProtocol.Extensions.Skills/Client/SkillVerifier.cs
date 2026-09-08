using ModelContextProtocol.Protocol;
using System.Security.Cryptography;
using System.Text;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Verifies skill file content against a skill's manifest, and computes manifest digests.
/// </summary>
/// <remarks>
/// <para>
/// When a host retrieves a file listed in a skill's manifest, it must verify the content against that entry's
/// digest and size, and must treat a read of a file the manifest does not list as a verification failure. These
/// helpers implement those checks. Frontmatter verification (re-parsing the fetched <c>SKILL.md</c> and comparing
/// its YAML frontmatter against the entry) is not implemented here, since this package does not parse YAML.
/// </para>
/// <para>
/// Digests are unsigned and supplied by the same server that supplies the content. A match proves the manifest
/// and the content are consistent, not that either is trustworthy.
/// </para>
/// </remarks>
public static class SkillVerifier
{
    /// <summary>
    /// Computes the manifest digest of content: <c>sha256:</c> followed by the lowercase hexadecimal SHA-256 of the bytes.
    /// </summary>
    /// <param name="content">The raw bytes.</param>
    /// <returns>The digest.</returns>
    public static string ComputeDigest(ReadOnlySpan<byte> content)
    {
#if NET
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(content, hash);
#else
        byte[] hash;
        using (var sha256 = SHA256.Create())
        {
            hash = sha256.ComputeHash(content.ToArray());
        }
#endif

        var builder = new StringBuilder(SkillsProtocol.DigestPrefix, SkillsProtocol.DigestPrefix.Length + (hash.Length * 2));
        foreach (byte b in hash)
        {
            builder.Append(ToHexChar(b >> 4)).Append(ToHexChar(b & 0xF));
        }

        return builder.ToString();

        static char ToHexChar(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'a' + (nibble - 10));
    }

    /// <summary>
    /// Verifies raw content against a manifest entry.
    /// </summary>
    /// <param name="expected">The manifest entry.</param>
    /// <param name="content">The bytes that were read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="expected"/> is <see langword="null"/>.</exception>
    /// <exception cref="SkillVerificationException">The size or digest of <paramref name="content"/> does not match <paramref name="expected"/>.</exception>
    public static void Verify(SkillResource expected, ReadOnlySpan<byte> content)
    {
#if NET
        ArgumentNullException.ThrowIfNull(expected);
#else
        if (expected is null) throw new ArgumentNullException(nameof(expected));
#endif

        if (content.Length != expected.Size)
        {
            throw new SkillVerificationException(
                $"The content of '{expected.Uri}' is {content.Length} bytes, but its manifest entry declares {expected.Size} bytes.");
        }

        string actualDigest = ComputeDigest(content);
        if (!string.Equals(actualDigest, expected.Digest, StringComparison.OrdinalIgnoreCase))
        {
            throw new SkillVerificationException(
                $"The content of '{expected.Uri}' has digest '{actualDigest}', but its manifest entry declares '{expected.Digest}'.");
        }
    }

    /// <summary>
    /// Verifies the contents returned by <c>resources/read</c> against a manifest entry.
    /// </summary>
    /// <param name="expected">The manifest entry.</param>
    /// <param name="contents">The contents that were read.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="SkillVerificationException">
    /// The contents are not for <paramref name="expected"/>'s URI, are neither text nor a blob, or their size or
    /// digest does not match <paramref name="expected"/>.
    /// </exception>
    /// <remarks>
    /// Text contents are hashed as their UTF-8 encoding; blob contents are hashed as their decoded bytes.
    /// </remarks>
    public static void Verify(SkillResource expected, ResourceContents contents)
    {
#if NET
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(contents);
#else
        if (expected is null) throw new ArgumentNullException(nameof(expected));
        if (contents is null) throw new ArgumentNullException(nameof(contents));
#endif

        if (!string.Equals(contents.Uri, expected.Uri, StringComparison.Ordinal))
        {
            throw new SkillVerificationException(
                $"The contents are for '{contents.Uri}', but verification was requested against the manifest entry for '{expected.Uri}'.");
        }

        switch (contents)
        {
            case TextResourceContents text:
                Verify(expected, Encoding.UTF8.GetBytes(text.Text));
                break;

            case BlobResourceContents blob:
                ReadOnlyMemory<byte> decoded;
                try
                {
                    decoded = blob.DecodedData;
                }
                catch (FormatException e)
                {
                    throw new SkillVerificationException($"The blob contents of '{contents.Uri}' are not valid Base64.", e);
                }

                Verify(expected, decoded.Span);
                break;

            default:
                throw new SkillVerificationException($"The contents of '{contents.Uri}' are neither text nor a blob and cannot be verified.");
        }
    }

    /// <summary>
    /// Verifies the result of a <c>resources/read</c> for one of a skill's files against the skill's manifest.
    /// </summary>
    /// <param name="skill">The skill entry being acted on.</param>
    /// <param name="result">The result of reading a file of the skill.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="skill"/>'s manifest is <see cref="SkillResources.Dynamic"/>, which cannot be verified.</exception>
    /// <exception cref="SkillVerificationException">
    /// The result is empty, contains contents for a URI the manifest does not list, or contains contents whose
    /// size or digest does not match the manifest.
    /// </exception>
    public static void Verify(Skill skill, ReadResourceResult result)
    {
#if NET
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(result);
#else
        if (skill is null) throw new ArgumentNullException(nameof(skill));
        if (result is null) throw new ArgumentNullException(nameof(result));
#endif

        if (skill.Resources.IsDynamic)
        {
            throw new InvalidOperationException(
                $"Skill '{skill.Uri}' declares dynamic resources, which carry no digests and cannot be verified.");
        }

        if (result.Contents is not { Count: > 0 })
        {
            throw new SkillVerificationException($"The read returned no contents to verify against skill '{skill.Uri}'.");
        }

        foreach (var contents in result.Contents)
        {
            var expected = FindResource(skill, contents.Uri) ??
                throw new SkillVerificationException(
                    $"'{contents.Uri}' is not listed in the manifest of skill '{skill.Uri}'. An unlisted file is a change to the skill; " +
                    "refresh the entry with skills/get before reading it.");

            Verify(expected, contents);
        }
    }

    /// <summary>
    /// Verifies the result of reading a specific file of a skill through <c>resources/read</c> against the skill's
    /// manifest, additionally requiring that the result actually contains the requested file.
    /// </summary>
    /// <param name="skill">The skill entry being acted on.</param>
    /// <param name="uri">The URI that was requested. It must be listed in <paramref name="skill"/>'s manifest.</param>
    /// <param name="result">The result of the read.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="skill"/>'s manifest is <see cref="SkillResources.Dynamic"/>, which cannot be verified.</exception>
    /// <exception cref="SkillVerificationException">
    /// <paramref name="uri"/> is not listed in the manifest, the result does not contain contents for
    /// <paramref name="uri"/>, or any contents in the result fail verification per <see cref="Verify(Skill, ReadResourceResult)"/>.
    /// </exception>
    /// <remarks>
    /// Checking every returned content against the manifest is not enough on its own: a server could answer a read
    /// of one file with another, correctly digested, file of the same skill. Binding the result to the requested
    /// URI closes that gap.
    /// </remarks>
    public static void Verify(Skill skill, string uri, ReadResourceResult result)
    {
#if NET
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(result);
#else
        if (skill is null) throw new ArgumentNullException(nameof(skill));
        if (uri is null) throw new ArgumentNullException(nameof(uri));
        if (result is null) throw new ArgumentNullException(nameof(result));
#endif

        if (!skill.Resources.IsDynamic && FindResource(skill, uri) is null)
        {
            throw new SkillVerificationException(
                $"'{uri}' is not listed in the manifest of skill '{skill.Uri}'. An unlisted file is a change to the skill; " +
                "refresh the entry with skills/get before reading it.");
        }

        Verify(skill, result);

        bool found = false;
        foreach (var contents in result.Contents)
        {
            found |= string.Equals(contents.Uri, uri, StringComparison.Ordinal);
        }

        if (!found)
        {
            throw new SkillVerificationException(
                $"The read of '{uri}' returned no contents for that URI. The server answered with a different file of skill '{skill.Uri}'.");
        }
    }

    internal static SkillResource? FindResource(Skill skill, string uri)
    {
        var resources = skill.Resources.Resources;
        if (resources is null)
        {
            return null;
        }

        foreach (var resource in resources)
        {
            if (string.Equals(resource.Uri, uri, StringComparison.Ordinal))
            {
                return resource;
            }
        }

        return null;
    }
}
