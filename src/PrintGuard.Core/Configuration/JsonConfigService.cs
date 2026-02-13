using System.Text.Json;
using PrintGuard.Core.Models;

namespace PrintGuard.Core.Configuration;

public sealed class JsonConfigService : IConfigService
{
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true
    };

    private AppConfig _current = new();

    public JsonConfigService(string? baseDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintGuard")
            : baseDirectory;

        ConfigDirectory = root;
        ConfigPath = Path.Combine(ConfigDirectory, "config.json");
    }

    public AppConfig Current => _current;

    public string ConfigDirectory { get; }

    public string ConfigPath { get; }

    public event EventHandler<AppConfig>? ConfigUpdated;

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(ConfigDirectory);

            if (!File.Exists(ConfigPath))
            {
                _current = new AppConfig();
                _current.Normalize();
                await PersistCurrentNoLockAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var json = await File.ReadAllTextAsync(ConfigPath, cancellationToken).ConfigureAwait(false);
                var deserialized = JsonSerializer.Deserialize<AppConfig>(json, _serializerOptions) ?? new AppConfig();
                deserialized.Normalize();
                _current = deserialized;
            }

            return _current;
        }
        finally
        {
            _ioLock.Release();
            ConfigUpdated?.Invoke(this, _current);
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _current.Normalize();
            await PersistCurrentNoLockAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
            ConfigUpdated?.Invoke(this, _current);
        }
    }

    public async Task UpdateAsync(Action<AppConfig> updateAction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            updateAction(_current);
            _current.Normalize();
            await PersistCurrentNoLockAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
            ConfigUpdated?.Invoke(this, _current);
        }
    }

    private Task PersistCurrentNoLockAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var json = JsonSerializer.Serialize(_current, _serializerOptions);
        return File.WriteAllTextAsync(ConfigPath, json, cancellationToken);
    }
}
