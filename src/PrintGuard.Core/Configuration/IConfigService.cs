using PrintGuard.Core.Models;

namespace PrintGuard.Core.Configuration;

public interface IConfigService
{
    AppConfig Current { get; }
    string ConfigDirectory { get; }
    string ConfigPath { get; }
    event EventHandler<AppConfig>? ConfigUpdated;

    Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(Action<AppConfig> updateAction, CancellationToken cancellationToken = default);
}
