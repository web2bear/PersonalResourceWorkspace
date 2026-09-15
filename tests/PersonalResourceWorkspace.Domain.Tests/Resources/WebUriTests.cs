using PersonalResourceWorkspace.Domain.Resources;

namespace PersonalResourceWorkspace.Domain.Tests.Resources;

public sealed class WebUriTests
{
    [Theory]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("http://example.com/path?q=1", "http://example.com/path?q=1")]
    [InlineData("  HTTPS://EXAMPLE.COM/docs  ", "https://example.com/docs")]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("www.example.com/a", "https://www.example.com/a")]
    [InlineData("localhost:8080", "https://localhost:8080/")]
    public void TryCreateAcceptsHttpAndHttps(string input, string expectedAbsoluteUri)
    {
        Assert.True(WebUri.TryCreate(input, out var webUri));
        Assert.Equal(expectedAbsoluteUri, webUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/temp/doc.txt")]
    [InlineData("ftp://example.com")]
    [InlineData("about:blank")]
    [InlineData("https://")]
    [InlineData("not a url")]
    [InlineData("C:\\docs\\file.txt")]
    public void TryCreateRejectsUnsupportedValues(string? input)
    {
        Assert.False(WebUri.TryCreate(input, out var webUri));
        Assert.Null(webUri);
    }
}
