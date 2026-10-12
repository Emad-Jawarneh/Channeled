namespace Channeled.Application.Abstractions.Operations;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>Directory where backup packages are written.</summary>
    public string RootPath { get; set; } = ".data/backups";

    public int RetainDaily { get; set; } = 7;
    public int RetainWeekly { get; set; } = 4;
    public int RetainMonthly { get; set; } = 3;

    public string PgDumpPath { get; set; } = "pg_dump";
    public string PgRestorePath { get; set; } = "pg_restore";

    /// <summary>Operator email for backup failure / low-disk alerts.</summary>
    public string? AlertEmail { get; set; }

    /// <summary>Alert when free disk on backup or store volume falls below this many bytes.</summary>
    public long LowDiskBytes { get; set; } = 1L * 1024 * 1024 * 1024;

    /// <summary>
    /// When true, admin restore endpoint runs a full pg_restore + object restore.
    /// Disable in API test hosts that share a live database connection pool.
    /// </summary>
    public bool RestoreViaAdmin { get; set; } = true;
}

public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    public bool Enabled { get; set; }
}

public sealed class OperationsOptions
{
    public const string SectionName = "Operations";

    public string PublicUrl { get; set; } = "http://localhost:5048";
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpFrom { get; set; }
}
