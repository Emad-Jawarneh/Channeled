namespace Channeled.Domain.Storage;

/// <summary>
/// Opaque object-store key: objects/&lt;2 hex&gt;/&lt;2 hex&gt;/&lt;file-uuid&gt;.
/// Names never appear in the store.
/// </summary>
public static class ObjectKey
{
    public static string ForFile(Guid fileId)
    {
        var hex = fileId.ToString("N");
        return $"objects/{hex[..2]}/{hex[2..4]}/{fileId:D}";
    }

    public static string ForTeamLogo(Guid teamId)
    {
        var hex = teamId.ToString("N");
        return $"team-logos/{hex[..2]}/{hex[2..4]}/{teamId:D}";
    }
}
