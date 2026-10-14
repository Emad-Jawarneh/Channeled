namespace Channeled.Application.Identity;

public interface ISelfServiceMail
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    Task<DateTimeOffset?> LastSmtpProbeAsync(CancellationToken ct = default);
}
