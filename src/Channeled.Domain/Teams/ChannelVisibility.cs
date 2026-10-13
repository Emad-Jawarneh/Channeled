namespace Channeled.Domain.Teams;

/// <summary>
/// Numeric values are persisted; Everyone was formerly Open (0), Restricted was formerly Private (1).
/// </summary>
public enum ChannelVisibility
{
    Everyone = 0,
    Restricted = 1,
}
