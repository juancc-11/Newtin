using Newtin.Diagnostics.Hardware;
using Newtin.Diagnostics.Network;
using Newtin.Diagnostics.Storage;
using Newtin.Diagnostics.SystemInfo;

namespace Newtin.Diagnostics.Tests;

public class ProvidersTests
{
    [Fact]
    public async Task SystemInfo_ReportsRealMachineName()
    {
        var sections = await new SystemInfoProvider().CollectAsync();
        var item = sections.SelectMany(s => s.Items).Single(i => i.Label == "Nombre del equipo");
        Assert.Equal(Environment.MachineName, item.Value);
    }

    [Fact]
    public async Task Hardware_ReportsProcessorAndMemory()
    {
        var sections = await new HardwareInfoProvider().CollectAsync();
        Assert.Contains(sections, s => s.Title == "Procesador");
        Assert.Contains(sections, s => s.Title == "Memoria RAM");

        var logical = sections.SelectMany(s => s.Items).Single(i => i.Label == "Procesadores lógicos");
        Assert.True(int.Parse(logical.Value) > 0);
    }

    [Fact]
    public async Task Battery_ReportsPowerStatus()
    {
        var sections = await new BatteryInfoProvider().CollectAsync();

        var section = Assert.Single(sections);
        Assert.Contains(section.Items, item => item.Label is "Disponibilidad" or "Alimentación");
    }

    [Fact]
    public async Task Storage_PercentagesStayInRange()
    {
        var sections = await new StorageInfoProvider().CollectAsync();
        Assert.NotEmpty(sections);
        foreach (var percent in sections.SelectMany(s => s.Items).Where(i => i.Percent.HasValue))
            Assert.InRange(percent.Percent!.Value, 0, 100);
    }

    [Fact]
    public async Task Network_ReturnsAtLeastOneSection()
    {
        var sections = await new NetworkInfoProvider().CollectAsync();
        Assert.NotEmpty(sections);
    }

    [Fact]
    public async Task CancelledToken_StopsTheQuery()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new StorageInfoProvider().CollectAsync(cts.Token));
    }
}