namespace Channeled.Domain.Search;

public enum SearchScope
{
    Channel = 0,
    Team = 1,
    Personal = 2,
    /// <summary>All files the user can currently see across every team (live visibility + grants, no ACL cache).</summary>
    Accessible = 3,
}
