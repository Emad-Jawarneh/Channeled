using System.Text.Json;
using System.Text.Json.Serialization;
using Channeled.Domain.Identity;
using Channeled.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace Channeled.Infrastructure.Jobs;

public static class EmailSendJobTypes
{
    public const string Type = "email_send";
    public const string DeadReasonDecrypt = "payload decryption failed; Data Protection keys missing or rotated";
}

public sealed record EmailSendPlainPayload(string To, string Subject, string Body, DateTimeOffset? TokenExpiresAt);

public static class EmailSendOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Enqueue(
        ApplicationDbContext db,
        IDataProtectionProvider dataProtection,
        DateTimeOffset now,
        EmailSendPlainPayload payload)
    {
        var protector = dataProtection.CreateProtector("Channeled.EmailSend.v1");
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var protectedPayload = protector.Protect(json);

        db.Jobs.Add(new JobEntity
        {
            Id = UuidV7.NewGuid(),
            Type = EmailSendJobTypes.Type,
            State = "pending",
            DetailsJson = JsonSerializer.Serialize(new { protectedPayload }, JsonOptions),
            CreatedAt = now,
            Attempts = 0,
            AvailableAt = now,
        });
    }

    public static bool TryUnprotect(
        IDataProtectionProvider dataProtection,
        string? detailsJson,
        out EmailSendPlainPayload? payload,
        out bool decryptFailed)
    {
        payload = null;
        decryptFailed = false;
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(detailsJson);
            if (!doc.RootElement.TryGetProperty("protectedPayload", out var prop))
            {
                return false;
            }

            var protector = dataProtection.CreateProtector("Channeled.EmailSend.v1");
            var json = protector.Unprotect(prop.GetString() ?? "");
            payload = JsonSerializer.Deserialize<EmailSendPlainPayload>(json, JsonOptions);
            return payload is not null;
        }
        catch (Exception)
        {
            decryptFailed = true;
            return false;
        }
    }
}
