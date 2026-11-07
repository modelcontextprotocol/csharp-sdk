namespace ModelContextProtocol.Extensions.Skills;

internal sealed class SkillUriComparer : IEqualityComparer<string>
{
    internal static SkillUriComparer Instance { get; } = new();

    public bool Equals(string? left, string? right) =>
        TryGetUri(left, out var first) && TryGetUri(right, out var second) ?
            first == second : string.Equals(left, right, StringComparison.Ordinal);

    public int GetHashCode(string value) =>
        TryGetUri(value, out var uri) ? uri!.GetHashCode() : StringComparer.Ordinal.GetHashCode(value);

    private static bool TryGetUri(string? value, out Uri? uri)
    {
        uri = null;
        if (value is null || value.IndexOf('{') >= 0)
        {
            return false;
        }

        return SkillValidation.TryValidateUriShape(value, out uri);
    }
}
