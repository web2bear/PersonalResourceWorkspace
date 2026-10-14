namespace PersonalResourceWorkspace.Application.Resources;

public static class SystemTags
{
    public static readonly Guid InboxId = Guid.Parse("b6aa8d60-664b-434a-a75d-91e9a83935af");
    public static readonly Guid TrashId = Guid.Parse("7eb3d755-c8d8-4d5f-ad5c-1808b1febf6c");
    public static readonly Guid InboxSearchId = Guid.Parse("3e3889f2-e2a9-4a11-bc7a-90707b6ea2fb");
    public static readonly Guid TrashSearchId = Guid.Parse("8ee4d1a8-c7ea-4b4e-9a28-120554d495ed");

    public static IReadOnlyList<Guid> AfterAddingUserTags(IEnumerable<Guid> current, IEnumerable<Guid> added)
    {
        var result = current.Concat(added).Distinct().ToHashSet();
        if (added.Any(id => id != InboxId && id != TrashId))
        {
            result.Remove(InboxId);
        }
        return result.ToArray();
    }

    public static IReadOnlyList<Guid> AfterRemovingUserTags(IEnumerable<Guid> current, IEnumerable<Guid> removed, bool isDeleted)
    {
        var removedSet = removed.ToHashSet();
        var result = current.Where(id => !removedSet.Contains(id)).ToHashSet();
        if (!isDeleted && removedSet.Any(id => id != InboxId && id != TrashId) &&
            result.All(id => id == InboxId || id == TrashId))
        {
            result.Add(InboxId);
        }
        return result.ToArray();
    }

    public static IReadOnlyList<Guid> AfterRestore(IEnumerable<Guid> current)
    {
        var result = current.Where(id => id != TrashId).ToHashSet();
        if (result.All(id => id == InboxId)) result.Add(InboxId);
        return result.ToArray();
    }

    public static bool Matches(IEnumerable<Guid> resourceTagIds, IEnumerable<Guid> requiredTagIds, IEnumerable<Guid> temporaryTagIds)
    {
        var resource = resourceTagIds.ToHashSet();
        var required = requiredTagIds.Concat(temporaryTagIds).ToHashSet();
        return required.All(resource.Contains) && (required.Contains(TrashId) == resource.Contains(TrashId));
    }
}
