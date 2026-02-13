using PrintGuard.Core.Configuration;

namespace PrintGuard.Tests;

public sealed class JsonConfigServiceTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(
        Path.GetTempPath(),
        "PrintGuardTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadAsync_CreatesConfigFile_WhenMissing()
    {
        var service = new JsonConfigService(_tempPath);

        var config = await service.LoadAsync();

        Assert.True(File.Exists(service.ConfigPath));
        Assert.True(config.IsProtectionEnabled);
        Assert.Equal(500, config.PollingIntervalMs);
    }

    [Fact]
    public async Task UpdateAsync_PersistsConfigValues()
    {
        var service = new JsonConfigService(_tempPath);
        await service.LoadAsync();

        await service.UpdateAsync(cfg =>
        {
            cfg.PollingIntervalMs = 999;
            cfg.ProtectedPrinters = ["PrinterA", "PrinterA", "PrinterB"];
            cfg.ProtectAllPrinters = false;
        });

        var reloaded = new JsonConfigService(_tempPath);
        var config = await reloaded.LoadAsync();

        Assert.Equal(999, config.PollingIntervalMs);
        Assert.Equal(2, config.ProtectedPrinters.Count);
        Assert.Contains("PrinterA", config.ProtectedPrinters);
        Assert.Contains("PrinterB", config.ProtectedPrinters);
        Assert.False(config.ProtectAllPrinters);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, recursive: true);
        }
    }
}
