using PrintGuard.Core.Models;

namespace PrintGuard.Core.Security;

public interface IPasswordService
{
    bool HasPasswordConfigured { get; }

    PasswordVerificationResult GetLockoutStatus();
    Task SetPasswordAsync(string password, CancellationToken cancellationToken = default);
    Task<PasswordVerificationResult> VerifyAsync(string password, CancellationToken cancellationToken = default);
}
