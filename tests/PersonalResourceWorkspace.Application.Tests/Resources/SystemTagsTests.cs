using PersonalResourceWorkspace.Application.Resources;

namespace PersonalResourceWorkspace.Application.Tests.Resources;

public sealed class SystemTagsTests
{
    [Fact]
    public void EmptySearchExcludesTrashAndCompositeTrashSearchIncludesOnlyDeletedMatches()
    {
        var project = Guid.NewGuid();
        Assert.True(SystemTags.Matches([project], [], []));
        Assert.False(SystemTags.Matches([project, SystemTags.TrashId], [], []));
        Assert.True(SystemTags.Matches([project, SystemTags.TrashId], [project, SystemTags.TrashId], []));
        Assert.False(SystemTags.Matches([SystemTags.TrashId], [project, SystemTags.TrashId], []));
    }

    [Fact]
    public void InboxTransitionHappensAtSpecificEvents()
    {
        var project = Guid.NewGuid();
        Assert.Equal([project], SystemTags.AfterAddingUserTags([SystemTags.InboxId], [project]));
        Assert.Equal([SystemTags.InboxId], SystemTags.AfterRemovingUserTags([project], [project], false));
        Assert.Empty(SystemTags.AfterRemovingUserTags([], [SystemTags.InboxId], false));
        Assert.Equal([SystemTags.TrashId], SystemTags.AfterRemovingUserTags([project, SystemTags.TrashId], [project], true));
        Assert.Equal([SystemTags.InboxId], SystemTags.AfterRestore([SystemTags.TrashId]));
    }
}
