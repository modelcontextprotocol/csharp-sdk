using ModelContextProtocol.Server;

namespace ModelContextProtocol.Extensions.Skills;

internal sealed class McpServerSkillCollection : McpServerPrimitiveCollection<McpServerSkill>
{
    private readonly object _mutationLock = new();
    private McpServerSkill[]? _sortedSnapshot;

    public override bool TryAdd(McpServerSkill primitive)
    {
        if (primitive is null) throw new ArgumentNullException(nameof(primitive));
        // Keep validation and insertion atomic without invoking application Changed handlers under the lock.
        using var changes = DeferChangedEvents();
        lock (_mutationLock)
        {
            if (TryGetPrimitive(primitive.Id, out _))
            {
                return false;
            }
            ValidateSharedFiles(primitive.Entry);
            bool added = base.TryAdd(primitive);
            if (added)
            {
                _sortedSnapshot = null;
            }
            return added;
        }
    }

    public override bool Remove(McpServerSkill primitive)
    {
        using var changes = DeferChangedEvents();
        lock (_mutationLock)
        {
            bool removed = base.Remove(primitive);
            if (removed)
            {
                _sortedSnapshot = null;
            }
            return removed;
        }
    }

    public override void Clear()
    {
        using var changes = DeferChangedEvents();
        lock (_mutationLock)
        {
            base.Clear();
            _sortedSnapshot = null;
        }
    }

    internal McpServerSkill[] GetSortedSnapshot()
    {
        lock (_mutationLock)
        {
            if (_sortedSnapshot is null)
            {
                _sortedSnapshot = base.ToArray();
                Array.Sort(_sortedSnapshot, static (left, right) => string.CompareOrdinal(left.Id, right.Id));
            }
            return _sortedSnapshot;
        }
    }

    private void ValidateSharedFiles(Skill candidate)
    {
        if (candidate.Resources.Resources is null)
        {
            return;
        }
        var files = new Dictionary<Uri, SkillResource>();
        AddFiles(files, candidate);
        foreach (var primitive in this)
        {
            if (primitive.Entry.Resources.Resources is not { } registered)
            {
                continue;
            }
            foreach (var file in registered)
            {
                if (files.TryGetValue(new Uri(file.Uri, UriKind.Absolute), out var proposed) && !SameFile(file, proposed))
                {
                    ThrowConflict(file, proposed);
                }
            }
        }
    }

    internal static void AddFiles(Dictionary<Uri, SkillResource> files, Skill entry)
    {
        if (entry.Resources.Resources is not { } manifest)
        {
            return;
        }
        foreach (var file in manifest)
        {
            var key = new Uri(file.Uri, UriKind.Absolute);
            if (files.TryGetValue(key, out var existing) && !SameFile(existing, file))
            {
                ThrowConflict(existing, file);
            }
            files[key] = file;
        }
    }

    internal static bool SameFile(SkillResource left, SkillResource right) =>
        string.Equals(left.Uri, right.Uri, StringComparison.Ordinal) &&
        string.Equals(left.Digest, right.Digest, StringComparison.Ordinal) &&
        left.Size == right.Size;

    private static void ThrowConflict(SkillResource existing, SkillResource proposed) =>
        throw new ArgumentException(
            $"The file '{proposed.Uri}' conflicts with the equivalent URI '{existing.Uri}' listed by another skill. " +
            "Shared files must have identical URI text, digest, and size.", "primitive");
}
