namespace Channeled.Application.Teams;

/// <summary>Team join / Open Join conversion tunables (config section <c>TeamJoin</c>); overridable in tests.</summary>
public sealed class TeamJoinOptions
{
    public const string SectionName = "TeamJoin";

    /// <summary>Production default for <see cref="MaxOpenJoinPendingApprove"/>.</summary>
    public const int DefaultMaxOpenJoinPendingApprove = 500;

    /// <summary>
    /// RequestToJoin → OpenJoin approves every pending request in ONE transaction. If more than this many are pending
    /// the conversion fails with <c>open_join_too_many_pending</c> (nothing is applied) and the Owner must clear the queue first.
    /// </summary>
    public int MaxOpenJoinPendingApprove { get; set; } = DefaultMaxOpenJoinPendingApprove;
}
