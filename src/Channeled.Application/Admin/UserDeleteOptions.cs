namespace Channeled.Application.Admin;

public sealed class UserDeleteOptions
{
    public const string SectionName = "UserDelete";

    public int SweepIntervalSeconds { get; set; } = 60;

    /// <summary>Minimum age of a late-phase delete before the sweep may advance it.</summary>
    public int LatePhaseGraceSeconds { get; set; } = 30;
}
