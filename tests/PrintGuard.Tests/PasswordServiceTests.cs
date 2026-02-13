using PrintGuard.Core.Configuration;
using PrintGuard.Core.Logging;
using PrintGuard.Core.Security;

namespace PrintGuard.Tests;

public sealed class PasswordServiceTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(
        Path.GetTempPath(),
        "PrintGuardTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task VerifyAsync_ReturnsSuccess_ForCorrectPassword()
    {
        var configService = new JsonConfigService(_tempPath);
        await configService.LoadAsync();
        var logger = new FileLogger(_tempPath);
        var passwordService = new PasswordService(configService, logger);

        await passwordService.SetPasswordAsync("correct-horse");

        var good = await passwordService.VerifyAsync("correct-horse");
        var bad = await passwordService.VerifyAsync("wrong-password");

        Assert.True(good.Succeeded);
        Assert.False(good.LockedOut);
        Assert.False(bad.Succeeded);
    }

    [Fact]
    public async Task VerifyAsync_EnforcesLockout_AfterConfiguredFailures()
    {
        var configService = new JsonConfigService(_tempPath);
        await configService.LoadAsync();
        await configService.UpdateAsync(cfg =>
        {
            cfg.MaxFailedAttempts = 2;
            cfg.LockoutSeconds = 5;
        });

        var logger = new FileLogger(_tempPath);
        var passwordService = new PasswordService(configService, logger);
        await passwordService.SetPasswordAsync("admin123");

        var first = await passwordService.VerifyAsync("wrong");
        var second = await passwordService.VerifyAsync("wrong");
        var locked = await passwordService.VerifyAsync("admin123");

        Assert.False(first.LockedOut);
        Assert.True(second.LockedOut);
        Assert.True(locked.LockedOut);

        await Task.Delay(TimeSpan.FromSeconds(configService.Current.LockoutSeconds + 1));
        var afterWait = await passwordService.VerifyAsync("admin123");
        Assert.True(afterWait.Succeeded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, recursive: true);
        }
    }
}
