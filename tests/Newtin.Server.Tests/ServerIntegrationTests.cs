using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.IO;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Newtin.Server.Hosting;
using System.Text;
using Newtin.Server.Persistence;
using System.Xml.Linq;

namespace Newtin.Server.Tests;

public class ServerIntegrationTests
{
    private static object NewNote(string id, string text = "hola") =>
        new { id, type = "note", x = 10, y = 20, text, color = "#F2B544" };

    [Fact]
    public async Task Index_IsServedLocally_WithSecurityHeaders()
    {
        await using var server = await TestServer.StartAsync();
        using var http = new HttpClient();

        var response = await http.GetAsync(server.BaseUrl + "/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Newtin", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Hub_RequiresTheAccessCode()
    {
        await using var server = await TestServer.StartAsync();
        using var http = new HttpClient();
        var url = $"{server.BaseUrl}{WhiteboardServerHost.HubPath}/negotiate?negotiateVersion=1";

        var without = await http.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.Unauthorized, without.StatusCode);

        using var withCode = new HttpRequestMessage(HttpMethod.Post, url);
        withCode.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.Code);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(withCode)).StatusCode);
    }

    [Fact]
    public async Task Client_WithAWrongCode_CannotConnect()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = server.CreateClient("INCORRECTO");

        await Assert.ThrowsAnyAsync<Exception>(() => client.StartAsync());
    }

    [Fact]
    public async Task TooManyWrongCodes_BlockTheClientEvenWithTheRightOne()
    {
        await using var server = await TestServer.StartAsync();
        using var http = new HttpClient();
        var url = $"{server.BaseUrl}{WhiteboardServerHost.HubPath}/negotiate?negotiateVersion=1";

        HttpResponseMessage last = null!;
        for (var i = 0; i < 10; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "MALO");
            last = await http.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);

        using var correct = new HttpRequestMessage(HttpMethod.Post, url);
        correct.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.Code);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.SendAsync(correct)).StatusCode);
    }

    [Fact]
    public async Task Commands_BeforeJoining_AreRejected()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = server.CreateClient();
        await client.StartAsync();

        var error = await Assert.ThrowsAsync<HubException>(() => client.InvokeAsync("CreateElement", NewNote("n1")));

        Assert.Contains("identificar", error.Message);
    }

    [Fact]
    public async Task InvalidNames_AreRejected()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = server.CreateClient();
        await client.StartAsync();

        await Assert.ThrowsAsync<HubException>(() => client.InvokeAsync<JsonElement>("Join", "<script>"));
    }

    [Fact]
    public async Task ANoteCreatedByOneClient_ReachesTheOther()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");
        await using var beto = await server.JoinAsync("Beto");

        var received = new EventLog();
        beto.On<JsonElement>("ElementCreated", received.Add);

        await ana.InvokeAsync("CreateElement", NewNote("n1", "Hola desde Ana"));

        var element = await received.NextAsync();
        Assert.Equal("n1", element.GetProperty("id").GetString());
        Assert.Equal("Hola desde Ana", element.GetProperty("text").GetString());
        Assert.Equal("Ana", element.GetProperty("author").GetString());
        Assert.Equal("note", element.GetProperty("type").GetString());
        Assert.Equal(1, element.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task TheAuthorAlsoReceivesTheOfficialElement()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");

        var received = new EventLog();
        ana.On<JsonElement>("ElementCreated", received.Add);

        await ana.InvokeAsync("CreateElement", NewNote("n1"));

        Assert.Equal("n1", (await received.NextAsync()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task ALateJoiner_ReceivesTheExistingState()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");
        await ana.InvokeAsync("CreateElement", NewNote("n1"));

        await using var beto = server.CreateClient();
        await beto.StartAsync();
        var join = await beto.InvokeAsync<JsonElement>("Join", "Beto");

        var elements = join.GetProperty("snapshot").GetProperty("elements");
        Assert.Equal(1, elements.GetArrayLength());
        Assert.Equal("n1", elements[0].GetProperty("id").GetString());
        Assert.Equal(2, join.GetProperty("clients").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task InvalidElements_AreRejectedWithAMessage()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");

        await Assert.ThrowsAsync<HubException>(() => ana.InvokeAsync("CreateElement",
            new { id = "n1", type = "note", x = 1, y = 1, color = "rojo" }));
        await Assert.ThrowsAsync<HubException>(() => ana.InvokeAsync("CreateElement",
            new { id = "n 1", type = "note", x = 1, y = 1 }));
        await Assert.ThrowsAsync<HubException>(() => ana.InvokeAsync("CreateElement",
            new { id = "n1", type = "note", x = 1e9, y = 1 }));
        await Assert.ThrowsAsync<HubException>(() => ana.InvokeAsync("CreateElement",
            new { id = "n1", type = "dibujo", x = 1, y = 1 }));

        await ana.InvokeAsync("CreateElement", NewNote("n1"));
        await Assert.ThrowsAsync<HubException>(() => ana.InvokeAsync("CreateElement", NewNote("n1")));
    }

    [Fact]
    public async Task MovingAndDeleting_AreBroadcast()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");
        await using var beto = await server.JoinAsync("Beto");

        var moved = new EventLog();
        var deleted = new EventLog();
        beto.On<JsonElement>("ElementMoved", moved.Add);
        beto.On<JsonElement>("ElementDeleted", deleted.Add);

        await ana.InvokeAsync("CreateElement", NewNote("n1"));
        await ana.InvokeAsync("MoveElement", "n1", 300.5, 400.5);
        await ana.InvokeAsync("DeleteElement", "n1");

        var element = await moved.NextAsync();
        Assert.Equal(300.5, element.GetProperty("x").GetDouble());
        Assert.Equal(2, element.GetProperty("version").GetInt32());
        Assert.Equal("n1", (await deleted.NextAsync()).GetString());
    }

    [Fact]
    public async Task Categories_AreSynchronizedAndClearedFromElements()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");
        await using var beto = await server.JoinAsync("Beto");

        var created = new EventLog();
        var updated = new EventLog();
        var removed = new EventLog();
        beto.On<JsonElement>("CategoryCreated", created.Add);
        beto.On<JsonElement>("ElementUpdated", updated.Add);
        beto.On<JsonElement>("CategoryDeleted", removed.Add);

        await ana.InvokeAsync("CreateCategory", new { id = "c1", name = "Ideas", color = "#112233" });
        Assert.Equal("Ideas", (await created.NextAsync()).GetProperty("name").GetString());

        await ana.InvokeAsync("CreateElement",
            new { id = "n1", type = "note", x = 1, y = 1, text = "x", categoryId = "c1" });
        await ana.InvokeAsync("DeleteCategory", "c1");

        Assert.Equal("c1", (await removed.NextAsync()).GetString());
        var element = await updated.NextAsync(e => e.GetProperty("id").GetString() == "n1"
                                                   && !e.TryGetProperty("categoryId", out _));
        Assert.Equal("n1", element.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ClearingTheBoard_IsBroadcast()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");
        await using var beto = await server.JoinAsync("Beto");

        var cleared = new TaskCompletionSource();
        beto.On("BoardCleared", () => cleared.TrySetResult());

        await ana.InvokeAsync("CreateElement", NewNote("n1"));
        await ana.InvokeAsync("ClearBoard");

        await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, server.Host.ElementCount);
    }

    [Fact]
    public async Task StrokeProgress_ReachesOthersButIsNotStored()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");
        await using var beto = await server.JoinAsync("Beto");

        var progress = new EventLog();
        beto.On<JsonElement>("StrokeProgress", progress.Add);

        await ana.InvokeAsync("StrokeProgress",
            new { id = "s1", color = "#FFFFFF", width = 3, points = new[] { new { x = 1, y = 1 }, new { x = 2, y = 2 } } });

        var received = await progress.NextAsync();
        Assert.Equal("Ana", received.GetProperty("author").GetString());
        Assert.Equal(2, received.GetProperty("points").GetArrayLength());
        Assert.Equal(0, server.Host.ElementCount);
    }

    [Fact]
    public async Task ClientsChanged_ReportsWhoIsConnected()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");

        var changes = new EventLog();
        ana.On<JsonElement>("ClientsChanged", changes.Add);

        var beto = await server.JoinAsync("Beto");
        var two = await changes.NextAsync(e => e.GetProperty("count").GetInt32() == 2);
        Assert.Equal(["Ana", "Beto"], two.GetProperty("names").EnumerateArray().Select(n => n.GetString()));

        await beto.StopAsync();
        await beto.DisposeAsync();

        var one = await changes.NextAsync(e => e.GetProperty("count").GetInt32() == 1);
        Assert.Equal("Ana", one.GetProperty("names")[0].GetString());
    }

    [Fact]
    public async Task StoppingAndRestarting_ReleasesThePort_AndKeepsTheBoard()
    {
        await using var server = await TestServer.StartAsync();
        await using (var ana = await server.JoinAsync("Ana"))
            await ana.InvokeAsync("CreateElement", NewNote("n1"));

        var firstCode = server.Code;
        await server.Host.StopAsync();

        Assert.Equal(ServerState.Stopped, server.Host.State);
        Assert.Null(server.Host.AccessCode);
        using (var http = new HttpClient())
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetAsync(server.BaseUrl + "/"));

        await server.Host.StartAsync(server.Options);

        Assert.Equal(ServerState.Running, server.Host.State);
        Assert.NotEqual(firstCode, server.Code);

        await using var beto = server.CreateClient();
        await beto.StartAsync();
        var join = await beto.InvokeAsync<JsonElement>("Join", "Beto");
        Assert.Equal(1, join.GetProperty("snapshot").GetProperty("elements").GetArrayLength());
    }

    [Fact]
    public async Task StartingTwice_IsRejected()
    {
        await using var server = await TestServer.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => server.Host.StartAsync(server.Options));
        Assert.Equal(ServerState.Running, server.Host.State);
    }

    [Fact]
    public async Task FreeText_IsBroadcast_AndValidated()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");
        await using var beto = await server.JoinAsync("Beto");

        var created = new EventLog();
        var updated = new EventLog();
        beto.On<JsonElement>("ElementCreated", created.Add);
        beto.On<JsonElement>("ElementUpdated", updated.Add);

        await ana.InvokeAsync("CreateElement",
            new { id = "t1", type = "text", x = 40, y = 50, text = "Hola\nmundo", color = "#17131F", width = 28 });

        var element = await created.NextAsync();
        Assert.Equal("text", element.GetProperty("type").GetString());
        Assert.Equal("Hola\nmundo", element.GetProperty("text").GetString());
        Assert.Equal(28, element.GetProperty("width").GetDouble());

        await ana.InvokeAsync("UpdateElement",
            new { id = "t1", type = "text", x = 40, y = 50, text = "Cambiado", width = 40 });

        var changed = await updated.NextAsync();
        Assert.Equal("Cambiado", changed.GetProperty("text").GetString());
        Assert.Equal(2, changed.GetProperty("version").GetInt32());

        await Assert.ThrowsAsync<HubException>(() => ana.InvokeAsync("CreateElement",
            new { id = "t2", type = "text", x = 1, y = 1, text = "   ", width = 20 }));
        await Assert.ThrowsAsync<HubException>(() => ana.InvokeAsync("CreateElement",
            new { id = "t3", type = "text", x = 1, y = 1, text = "x", width = 500 }));
    }

        [Fact]
    public async Task ImportingWhileRunning_ReplacesTheBoardOnEveryClient()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");

        var replaced = new EventLog();
        ana.On<JsonElement>("BoardReplaced", replaced.Add);

        var plan = MarkdownFormat.Read(Encoding.UTF8.GetBytes("## Ideas\n- uno\n- dos")).Plan!;
        var outcome = await server.Host.ImportAsync(plan, ImportMode.Replace);

        Assert.Equal(2, outcome.Elements);
        var snapshot = await replaced.NextAsync();
        Assert.Equal(2, snapshot.GetProperty("elements").GetArrayLength());
        Assert.Equal("Ideas", snapshot.GetProperty("categories")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ClearingTheBoardFromTheApp_EmptiesEveryClient()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");
        await ana.InvokeAsync("CreateElement", NewNote("n1"));

        var replaced = new EventLog();
        ana.On<JsonElement>("BoardReplaced", replaced.Add);

        await server.Host.ClearBoardAsync();

        var snapshot = await replaced.NextAsync();
        Assert.Equal(0, snapshot.GetProperty("elements").GetArrayLength());
        Assert.Equal(0, server.Host.ElementCount);
    }

    [Fact]
    public async Task ChangesMadeByClients_RaiseBoardChanged()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");

        var changed = new TaskCompletionSource();
        server.Host.BoardChanged += (_, _) => changed.TrySetResult();

        await ana.InvokeAsync("CreateElement", NewNote("n1"));

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

        [Fact]
    public async Task Export_RequiresTheAccessCode()
    {
        await using var server = await TestServer.StartAsync();
        using var http = new HttpClient();

        var response = await http.GetAsync($"{server.BaseUrl}/api/export/svg");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Export_ReturnsEveryFormat_FromTheLiveBoard()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");
        await ana.InvokeAsync("CreateElement", NewNote("n1", "Hola <b>mundo</b>"));

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Code);

        var svg = await http.GetStringAsync($"{server.BaseUrl}/api/export/svg");
        Assert.NotNull(XDocument.Parse(svg).Root);
        Assert.Contains("Hola", svg);

        var markdown = await http.GetStringAsync($"{server.BaseUrl}/api/export/markdown");
        Assert.Contains("Hola &lt;b&gt;mundo", markdown);

        var csv = await http.GetByteArrayAsync($"{server.BaseUrl}/api/export/csv");
        Assert.Equal([0xEF, 0xBB, 0xBF], csv.Take(3));

        var native = await http.GetByteArrayAsync($"{server.BaseUrl}/api/export/newtin");
        Assert.Single(NativeFormat.Read(native).Plan!.Elements);
    }

    [Fact]
    public async Task Export_OfAnEmptyBoard_IsAConflict()
    {
        await using var server = await TestServer.StartAsync();
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Code);

        var response = await http.GetAsync($"{server.BaseUrl}/api/export/svg");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Export_UnknownFormat_IsNotFound_AndNothingIsCached()
    {
        await using var server = await TestServer.StartAsync();
        await using var ana = await server.JoinAsync("Ana");
        await ana.InvokeAsync("CreateElement", NewNote("n1"));

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Code);

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"{server.BaseUrl}/api/export/exe")).StatusCode);

        var ok = await http.GetAsync($"{server.BaseUrl}/api/export/svg");
        Assert.Equal("no-store", ok.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ExportRoute_SharesTheBruteForceProtection()
    {
        await using var server = await TestServer.StartAsync();
        using var http = new HttpClient();

        HttpResponseMessage last = null!;
        for (var i = 0; i < 10; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUrl}/api/export/svg");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "MALO");
            last = await http.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
    }
}