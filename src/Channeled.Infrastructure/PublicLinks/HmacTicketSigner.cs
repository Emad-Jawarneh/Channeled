using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Channeled.Application.PublicLinks;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.PublicLinks;

public sealed record ShareTicket(Guid LinkId, Guid TicketId, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// Stateless HMAC-SHA256 ticket signer with a fixed TTL (no sliding). Signs with
/// <c>HmacKey</c>; verifies against <c>HmacKey</c> then <c>PreviousHmacKey</c> (dual-key rotation).
/// Ticket format: <c>base64url(payload) "." base64url(mac)</c>.
/// </summary>
public sealed class HmacTicketSigner(IOptionsMonitor<PublicFileLinksOptions> options)
{
    private const string Version = "v1";

    public string Sign(ShareTicket ticket)
    {
        var key = options.CurrentValue.HmacKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("PublicFileLinks:HmacKey is not configured.");
        }

        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{Version}|{ticket.LinkId:N}|{ticket.TicketId:N}|{ticket.IssuedAt.ToUnixTimeSeconds()}|{ticket.ExpiresAt.ToUnixTimeSeconds()}");
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), payloadBytes);
        return Base64Url(payloadBytes) + "." + Base64Url(mac);
    }

    public bool TryVerify(string? ticket, DateTimeOffset now, out ShareTicket? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 512)
        {
            return false;
        }

        var dot = ticket.IndexOf('.');
        if (dot <= 0 || dot == ticket.Length - 1)
        {
            return false;
        }

        byte[] payloadBytes;
        byte[] mac;
        try
        {
            payloadBytes = FromBase64Url(ticket[..dot]);
            mac = FromBase64Url(ticket[(dot + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var opts = options.CurrentValue;
        var verified = false;
        foreach (var key in new[] { opts.HmacKey, opts.PreviousHmacKey })
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), payloadBytes);
            if (CryptographicOperations.FixedTimeEquals(expected, mac))
            {
                verified = true;
            }
        }

        if (!verified)
        {
            return false;
        }

        var parts = Encoding.UTF8.GetString(payloadBytes).Split('|');
        if (parts.Length != 5
            || parts[0] != Version
            || !Guid.TryParseExact(parts[1], "N", out var linkId)
            || !Guid.TryParseExact(parts[2], "N", out var ticketId)
            || !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var issued)
            || !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expires))
        {
            return false;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expires);
        if (now >= expiresAt)
        {
            return false;
        }

        result = new ShareTicket(linkId, ticketId, DateTimeOffset.FromUnixTimeSeconds(issued), expiresAt);
        return true;
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
        return Convert.FromBase64String(s);
    }
}
