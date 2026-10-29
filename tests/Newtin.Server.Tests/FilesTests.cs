using System.Text;
using Newtin.Server.Hosting;
using Newtin.Server.Persistence;

namespace Newtin.Server.Tests;

public sealed class FilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "newtin-files-" + Guid.NewGuid().ToString("N"));

    public FilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_dir)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition(), "No ocurrió a tiempo.");
    }

    // ---------- escritura segura ----------

    [Fact]
    public void Write_CreatesAndReplacesTheFile_WithoutLeavingTemporaries()
    {
        var path = Path.Combine(_dir, "a.newtin");

        Assert.True(AtomicFile.Write(path, [1, 2, 3]).Ok);
        Assert.True(AtomicFile.Write(path, [4, 5]).Ok);

        Assert.Equal([4, 5], File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    [Fact]
    public void Write_ToAMissingFolder_FailsWithAMessage_AndCreatesNothing()
    {
        var result = AtomicFile.Write(Path.Combine(_dir, "no-existe", "a.newtin"), [1]);

        Assert.False(result.Ok);
        Assert.NotEmpty(result.Error!);
        Assert.False(Directory.Exists(Path.Combine(_dir, "no-existe")));
    }

    [Fact]
    public void Write_CanCreateTheFolder_WhenAsked()
    {
        var path = Path.Combine(_dir, "nueva", "a.newtin");

        Assert.True(AtomicFile.Write(path, [1], createDirectory: true).Ok);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Write_UnderAFile_FailsInsteadOfThrowing()
    {
        var file = Path.Combine(_dir, "archivo.txt");
        File.WriteAllText(file, "x");

        Assert.False(AtomicFile.Write(Path.Combine(file, "hijo.newtin"), [1]).Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\carpeta\\")]
    public void Write_RejectsInvalidPaths(string path) => Assert.False(AtomicFile.Write(path, [1]).Ok);

    [Fact]
    public void Write_OverAReadOnlyFile_FailsAndKeepsTheOriginal()
    {
        var path = Path.Combine(_dir, "solo-lectura.newtin");
        File.WriteAllBytes(path, [9, 9]);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var result = AtomicFile.Write(path, [1, 2, 3]);

        Assert.False(result.Ok);
        Assert.Equal([9, 9], File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(_dir));   // sin archivo temporal
    }

    [Fact]
    public void Read_RejectsMissingAndOversizedFiles()
    {
        var path = Path.Combine(_dir, "grande.bin");
        File.WriteAllBytes(path, new byte[100]);

        Assert.Null(AtomicFile.Read(Path.Combine(_dir, "nada"), 1000).Data);
        Assert.Null(AtomicFile.Read(path, 50).Data);
        Assert.Equal(100, AtomicFile.Read(path, 1000).Data!.Length);
    }

    [Theory]
    [InlineData("a.newtin", BoardFormat.Native)]
    [InlineData("a.JSON", BoardFormat.Native)]
    [InlineData("a.png", BoardFormat.Png)]
    [InlineData("a.svg", BoardFormat.Svg)]
    [InlineData("a.MD", BoardFormat.Markdown)]
    [InlineData("a.csv", BoardFormat.Csv)]
    [InlineData("a.txt", BoardFormat.Text)]
    public void Formats_AreDetectedByExtension(string path, BoardFormat expected) =>
        Assert.Equal(expected, BoardFormats.FromPath(path));

    [Fact]
    public void Formats_UnknownExtensions_AreNull() => Assert.Null(BoardFormats.FromPath("a.exe"));

    // ---------- autoguardado ----------

    [Fact]
    public async Task Autosave_WritesTheBoard_ShortlyAfterAChange()
    {
        var path = Path.Combine(_dir, "autosave.newtin");
        var host = new WhiteboardServerHost(new SilentLog());
        using var autosave = new BoardAutosave(host, path, new SilentLog(), TimeSpan.FromMilliseconds(100));

        host.ApplyImport(MarkdownFormat.Read(Encoding.UTF8.GetBytes("- uno\n- dos")).Plan!, ImportMode.Replace);

        await WaitFor(() => File.Exists(path));
        Assert.Equal(2, NativeFormat.Read(File.ReadAllBytes(path)).Plan!.Elements.Count);
    }

    [Fact]
    public async Task Autosave_Restore_BringsTheBoardBack()
    {
        var path = Path.Combine(_dir, "autosave.newtin");
        File.WriteAllBytes(path, NativeFormat.Write(PersistenceTests.RichBoard().Snapshot(), DateTimeOffset.UtcNow));

        var host = new WhiteboardServerHost(new SilentLog());
        using var autosave = new BoardAutosave(host, path, new SilentLog(), TimeSpan.FromMilliseconds(100));

        var result = autosave.Restore();

        Assert.True(result.Restored);
        Assert.Equal(4, host.Snapshot().Elements.Count);
        Assert.Equal(2, host.Snapshot().Categories.Count);
        await Task.CompletedTask;
    }

    [Fact]
    public void Autosave_Restore_WithoutAFile_DoesNothing()
    {
        var host = new WhiteboardServerHost(new SilentLog());
        using var autosave = new BoardAutosave(host, Path.Combine(_dir, "no-hay.newtin"), new SilentLog());

        Assert.False(autosave.Restore().Restored);
        Assert.Empty(host.Snapshot().Elements);
    }

    [Fact]
    public void Autosave_Restore_SetsACorruptFileAside_InsteadOfOverwritingIt()
    {
        var path = Path.Combine(_dir, "autosave.newtin");
        File.WriteAllText(path, "{ esto está dañado");

        var host = new WhiteboardServerHost(new SilentLog());
        using var autosave = new BoardAutosave(host, path, new SilentLog());

        Assert.False(autosave.Restore().Restored);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_dir, "*.corrupt-*.newtin"));
        Assert.Empty(host.Snapshot().Elements);
    }

    [Fact]
    public void Autosave_Flush_WritesImmediately()
    {
        var path = Path.Combine(_dir, "autosave.newtin");
        var host = new WhiteboardServerHost(new SilentLog());
        using var autosave = new BoardAutosave(host, path, new SilentLog(), TimeSpan.FromHours(1));
        host.ApplyImport(MarkdownFormat.Read(Encoding.UTF8.GetBytes("- uno")).Plan!, ImportMode.Replace);

        autosave.Flush();

        Assert.Single(NativeFormat.Read(File.ReadAllBytes(path)).Plan!.Elements);
    }
}