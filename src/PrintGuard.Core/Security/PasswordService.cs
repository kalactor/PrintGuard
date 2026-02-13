using System.Security.Cryptography;
using PrintGuard.Core.Configuration;
using PrintGuard.Core.Logging;
using PrintGuard.Core.Models;

namespace PrintGuard.Core.Security;

public sealed class PasswordService : IPasswordService
{
    private readonly IConfigService _configService;
    private readonly ILoggerService _logger;
    private readonly object _lock = new();

    private int _failedAttempts;
    private DateTimeOffset? _lockedOutUntilUtc;

    public PasswordService(IConfigService configService, ILoggerService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public bool HasPasswordConfigured => _configService.Current.HasPasswordConfigured;

    public PasswordVerificationResult GetLockoutStatus()
    {
        lock (_lock)
        {
            if (_lockedOutUntilUtc is { } until && until > DateTimeOffset.UtcNow)
            {
                return new PasswordVerificationResult(
                    Succeeded: false,
                    LockedOut: true,
                    LockoutRemaining: until - DateTimeOffset.UtcNow,
                    FailedAttempts: _failedAttempts,
                    RemainingAttempts: 0);
            }

            return new PasswordVerificationResult(
                Succeeded: false,
                LockedOut: false,
                LockoutRemaining: TimeSpan.Zero,
                FailedAttempts: _failedAttempts,
                RemainingAttempts: Math.Max(0, _configService.Current.MaxFailedAttempts - _failedAttempts));
        }
    }

    public async Task SetPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        var config = _configService.Current;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(password, salt, config.PasswordIterations);

        await _configService.UpdateAsync(cfg =>
        {
            cfg.PasswordAlgorithm = "PBKDF2-SHA256";
            cfg.PasswordIterations = Math.Max(100_000, cfg.PasswordIterations);
            cfg.PasswordSaltBase64 = Convert.ToBase64String(salt);
            cfg.PasswordHashBase64 = Convert.ToBase64String(hash);
        }, cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            _failedAttempts = 0;
            _lockedOutUntilUtc = null;
        }

        _logger.Info("Admin password configured.");
    }

    public Task<PasswordVerificationResult> VerifyAsync(string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!HasPasswordConfigured)
        {
            return Task.FromResult(new PasswordVerificationResult(
                Succeeded: false,
                LockedOut: false,
                LockoutRemaining: TimeSpan.Zero,
                FailedAttempts: 0,
                RemainingAttempts: 0));
        }

        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(OnFailedAttempt());
        }

        lock (_lock)
        {
            if (_lockedOutUntilUtc is { } lockedUntil && lockedUntil > DateTimeOffset.UtcNow)
            {
                return Task.FromResult(new PasswordVerificationResult(
                    Succeeded: false,
                    LockedOut: true,
                    LockoutRemaining: lockedUntil - DateTimeOffset.UtcNow,
                    FailedAttempts: _failedAttempts,
                    RemainingAttempts: 0));
            }
        }

        try
        {
            var config = _configService.Current;
            var salt = Convert.FromBase64String(config.PasswordSaltBase64!);
            var expectedHash = Convert.FromBase64String(config.PasswordHashBase64!);
            var actualHash = HashPassword(password, salt, config.PasswordIterations);
            var matched = CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);

            if (matched)
            {
                lock (_lock)
                {
                    _failedAttempts = 0;
                    _lockedOutUntilUtc = null;
                }

                return Task.FromResult(new PasswordVerificationResult(
                    Succeeded: true,
                    LockedOut: false,
                    LockoutRemaining: TimeSpan.Zero,
                    FailedAttempts: 0,
                    RemainingAttempts: config.MaxFailedAttempts));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Password verification failed due to invalid stored hash format.", ex);
            return Task.FromResult(new PasswordVerificationResult(
                Succeeded: false,
                LockedOut: false,
                LockoutRemaining: TimeSpan.Zero,
                FailedAttempts: 0,
                RemainingAttempts: 0));
        }

        return Task.FromResult(OnFailedAttempt());
    }

    private PasswordVerificationResult OnFailedAttempt()
    {
        lock (_lock)
        {
            var config = _configService.Current;
            _failedAttempts++;

            if (_failedAttempts >= config.MaxFailedAttempts)
            {
                _lockedOutUntilUtc = DateTimeOffset.UtcNow.AddSeconds(config.LockoutSeconds);
                _logger.Warning($"Password lockout triggered for {config.LockoutSeconds} seconds.");

                return new PasswordVerificationResult(
                    Succeeded: false,
                    LockedOut: true,
                    LockoutRemaining: TimeSpan.FromSeconds(config.LockoutSeconds),
                    FailedAttempts: _failedAttempts,
                    RemainingAttempts: 0);
            }

            return new PasswordVerificationResult(
                Succeeded: false,
                LockedOut: false,
                LockoutRemaining: TimeSpan.Zero,
                FailedAttempts: _failedAttempts,
                RemainingAttempts: Math.Max(0, config.MaxFailedAttempts - _failedAttempts));
        }
    }

    private static byte[] HashPassword(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
}
