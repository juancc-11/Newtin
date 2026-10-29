namespace Newtin.Server.Tests;

public class WebAssetsTests
{
    private static string WebRoot => Path.Combine(AppContext.BaseDirectory, "wwwroot");

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(WebRoot, relativePath));

    [Theory]
    [InlineData("/")]
    [InlineData("/css/styles.css")]
    [InlineData("/js/connection.js")]
    [InlineData("/js/whiteboard.js")]
    [InlineData("/js/app.js")]
    [InlineData("/js/signalr.min.js")]
    [InlineData("/assets/logo.png")]
    [InlineData("/js/export.js")]
    public async Task EveryFileOfThePage_IsServedLocally(string path)
    {
        await using var server = await TestServer.StartAsync();
        using var http = new HttpClient();

        var response = await http.GetAsync(server.BaseUrl + path);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Index_HasNoInlineScriptsOrStyles()
    {
        // La política de seguridad del servidor los bloquearía.
        var html = Read("index.html");

        Assert.DoesNotMatch(@"<script(?![^>]*\bsrc\s*=)", html);
        Assert.DoesNotMatch(@"<style", html);
        Assert.DoesNotMatch(@"\sstyle\s*=", html);
        Assert.DoesNotMatch(@"\son[a-z]+\s*=", html);
    }

    [Theory]
    [InlineData("js/app.js")]
    [InlineData("js/whiteboard.js")]
    [InlineData("js/connection.js")]
    [InlineData("js/export.js")]
    public void ClientScripts_AvoidDangerousApis(string file)
    {
        var script = Read(file);

        foreach (var forbidden in new[]
                 {
                     "innerHTML", "outerHTML", "insertAdjacentHTML", "eval(", "document.write",
                     "new Function", "setAttribute(\"style\""
                 })
            Assert.DoesNotContain(forbidden, script);
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData("css/styles.css")]
    [InlineData("js/app.js")]
    [InlineData("js/whiteboard.js")]
    [InlineData("js/connection.js")]
    public void PageFiles_DoNotReferenceExternalServers(string file) =>
        Assert.DoesNotMatch(@"https?://", Read(file));
}