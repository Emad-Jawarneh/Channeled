using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Channeled.Infrastructure.Security;

/// <summary>
/// Encrypts the raw team join token at rest (so Admin+ can re-display the link) using ASP.NET Core Data Protection.
/// The database also stores a SHA-256 hash for lookups; the raw token is never stored in clear text.
/// </summary>
public sealed class TeamJoinLinkProtector(IDataProtectionProvider provider)
{
    public const string Purpose = "Channeled.TeamJoinLink.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string rawToken) => _protector.Protect(rawToken);

    /// <summary>Returns false when the ciphertext is corrupt or the key ring can no longer decrypt it.</summary>
    public bool TryUnprotect(string ciphertext, out string raw)
    {
        raw = "";
        if (string.IsNullOrEmpty(ciphertext))
        {
            return false;
        }

        try
        {
            raw = _protector.Unprotect(ciphertext);
            return true;
        }
        catch (CryptographicException)
        {
            raw = "";
            return false;
        }
    }
}
