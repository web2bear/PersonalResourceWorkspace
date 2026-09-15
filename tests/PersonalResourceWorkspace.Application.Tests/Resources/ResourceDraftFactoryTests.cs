using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Resources;

namespace PersonalResourceWorkspace.Application.Tests.Resources;

public sealed class ResourceDraftFactoryTests
{
    [Theory]
    [InlineData("https://example.com", "example.com", "https://example.com/", StoredResourceKinds.Web)]
    [InlineData("http://example.com/path?q=1", "example.com", "http://example.com/path?q=1", StoredResourceKinds.Web)]
    [InlineData("  HTTPS://EXAMPLE.COM/docs  ", "example.com", "https://example.com/docs", StoredResourceKinds.Web)]
    [InlineData("example.com", "example.com", "https://example.com/", StoredResourceKinds.Web)]
    [InlineData("\"https://example.com/a\"", "example.com", "https://example.com/a", StoredResourceKinds.Web)]
    [InlineData("See https://example.com/docs for details", "example.com", "https://example.com/docs", StoredResourceKinds.Web)]
    public void TryParseTextFillsWebAddressAndDerivedFields(
        string text,
        string expectedTitle,
        string expectedAddress,
        string expectedKind)
    {
        var draft = ResourceDraftFactory.TryParseText(text);

        Assert.NotNull(draft);
        Assert.Equal(expectedTitle, draft.Title);
        Assert.Equal(expectedAddress, draft.Address);
        Assert.Equal(expectedKind, draft.Kind);
        Assert.Equal(expectedAddress, draft.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("просто заметка без адреса")]
    [InlineData("javascript:alert(1)")]
    [InlineData("please open file.txt now")]
    public void TryParseTextIgnoresContentThatIsNotAResourceAddress(string? text)
    {
        Assert.Null(ResourceDraftFactory.TryParseText(text));
    }

    [Fact]
    public void TryCreateUsesExistingFilePath()
    {
        var file = Path.Combine(Path.GetTempPath(), $"prw-clipboard-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(file, "sample");
        try
        {
            var draft = ResourceDraftFactory.TryCreate(new ClipboardSnapshot { Text = $"\"{file}\"" });

            Assert.NotNull(draft);
            Assert.Equal(Path.GetFileName(file), draft.Title);
            Assert.Equal(Path.GetFullPath(file), draft.Address);
            Assert.Equal(StoredResourceKinds.File, draft.Kind);
            Assert.Equal("PDF · файл из Проводника", draft.Description);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void TryCreateUsesExistingFolderPath()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prw-clipboard-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var draft = ResourceDraftFactory.TryParseText(folder);

            Assert.NotNull(draft);
            Assert.Equal(Path.GetFileName(folder), draft.Title);
            Assert.Equal(Path.GetFullPath(folder), draft.Address);
            Assert.Equal(StoredResourceKinds.Folder, draft.Kind);
            Assert.Equal("Папка из файловой системы", draft.Description);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void TryCreatePrefersCopiedFilePathOverText()
    {
        var file = Path.Combine(Path.GetTempPath(), $"prw-clipboard-item-{Guid.NewGuid():N}.md");
        File.WriteAllText(file, "note");
        try
        {
            var draft = ResourceDraftFactory.TryCreate(new ClipboardSnapshot
            {
                Text = "https://example.com",
                Paths = [file],
            });

            Assert.NotNull(draft);
            Assert.Equal(StoredResourceKinds.File, draft.Kind);
            Assert.Equal(Path.GetFullPath(file), draft.Address);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void TryCreateUsesClipboardUri()
    {
        var draft = ResourceDraftFactory.TryCreate(new ClipboardSnapshot
        {
            Uri = new Uri("https://learn.microsoft.com/windows/apps/winui/"),
        });

        Assert.NotNull(draft);
        Assert.Equal("learn.microsoft.com", draft.Title);
        Assert.Equal("https://learn.microsoft.com/windows/apps/winui/", draft.Address);
        Assert.Equal(StoredResourceKinds.Web, draft.Kind);
    }

    [Fact]
    public void TryCreateTakesTitleFromHtmlAnchor()
    {
        var html = """
            Version:0.9
            StartHTML:0000000100
            <html><body><!--StartFragment-->
            <a href="https://example.com/docs">Документация WinUI</a>
            <!--EndFragment--></body></html>
            """;

        var draft = ResourceDraftFactory.TryCreate(new ClipboardSnapshot
        {
            Text = "random copied label",
            Html = html,
        });

        Assert.NotNull(draft);
        Assert.Equal("Документация WinUI", draft.Title);
        Assert.Equal("https://example.com/docs", draft.Address);
        Assert.Equal(StoredResourceKinds.Web, draft.Kind);
    }

    [Fact]
    public void TryCreateKeepsHostWhenHtmlTitleIsTheUrl()
    {
        var draft = ResourceDraftFactory.TryCreate(new ClipboardSnapshot
        {
            Text = "https://example.com/docs",
            Html = """<a href="https://example.com/docs">https://example.com/docs</a>""",
        });

        Assert.NotNull(draft);
        Assert.Equal("example.com", draft.Title);
        Assert.Equal("https://example.com/docs", draft.Address);
    }

    [Fact]
    public void FromUriCreatesWebDraft()
    {
        var draft = ResourceDraftFactory.FromUri(new Uri("https://example.com/a"));

        Assert.Equal("example.com", draft.Title);
        Assert.Equal("https://example.com/a", draft.Address);
        Assert.Equal(StoredResourceKinds.Web, draft.Kind);
    }

    [Theory]
    [InlineData("photo.png", StoredResourceKinds.Image)]
    [InlineData("tool.exe", StoredResourceKinds.Application)]
    [InlineData("document.pdf", StoredResourceKinds.File)]
    public void FromPathClassifiesVisualAndExecutableResources(string fileName, string expectedKind)
    {
        var draft = ResourceDraftFactory.FromPath(Path.Combine(Path.GetTempPath(), fileName));

        Assert.Equal(expectedKind, draft.Kind);
    }

    [Fact]
    public void TryCreateTurnsPlainClipboardTextIntoTextSnippet()
    {
        var draft = ResourceDraftFactory.TryCreate(new ClipboardSnapshot
        {
            Text = "Первая строка заметки\nВторая строка",
        });

        Assert.NotNull(draft);
        Assert.Equal("Первая строка заметки", draft.Title);
        Assert.Equal(StoredResourceKinds.Note, draft.Kind);
        Assert.Equal("Первая строка заметки\nВторая строка", draft.Address);
    }
}
