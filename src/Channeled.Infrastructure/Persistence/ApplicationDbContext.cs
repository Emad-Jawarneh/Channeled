using Channeled.Domain.Audit;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Chat;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Jobs;
using Channeled.Infrastructure.Notifications;
using Channeled.Infrastructure.Operations;
using Channeled.Infrastructure.PublicLinks;
using Channeled.Infrastructure.Teams;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Persistence;

public sealed class ApplicationDbContext : IdentityDbContext<AppUser, AppRole, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<PlatformInvitation> PlatformInvitations => Set<PlatformInvitation>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<TeamEntity> Teams => Set<TeamEntity>();
    public DbSet<TeamMemberEntity> TeamMembers => Set<TeamMemberEntity>();
    public DbSet<ChannelEntity> Channels => Set<ChannelEntity>();
    public DbSet<ChannelMemberEntity> ChannelMembers => Set<ChannelMemberEntity>();
    public DbSet<ChannelJoinRequestEntity> ChannelJoinRequests => Set<ChannelJoinRequestEntity>();
    public DbSet<TeamJoinRequestEntity> TeamJoinRequests => Set<TeamJoinRequestEntity>();
    public DbSet<TeamInvitationEntity> TeamInvitations => Set<TeamInvitationEntity>();
    public DbSet<FolderEntity> Folders => Set<FolderEntity>();
    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<UploadSessionEntity> UploadSessions => Set<UploadSessionEntity>();
    public DbSet<UploadPartEntity> UploadParts => Set<UploadPartEntity>();
    public DbSet<FileStarEntity> FileStars => Set<FileStarEntity>();
    public DbSet<JobEntity> Jobs => Set<JobEntity>();
    public DbSet<BackupEntity> Backups => Set<BackupEntity>();
    public DbSet<FileShareLinkEntity> FileShareLinks => Set<FileShareLinkEntity>();
    public DbSet<ChannelMessageEntity> ChannelMessages => Set<ChannelMessageEntity>();
    public DbSet<MessageMentionEntity> MessageMentions => Set<MessageMentionEntity>();
    public DbSet<MessageFileRefEntity> MessageFileRefs => Set<MessageFileRefEntity>();
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();
    public DbSet<UsernameReservation> UsernameReservations => Set<UsernameReservation>();
    public DbSet<PendingEmailChange> PendingEmailChanges => Set<PendingEmailChange>();
    public DbSet<AddressRateCounter> AddressRateCounters => Set<AddressRateCounter>();
    public DbSet<OperationsSettingsEntity> OperationsSettings => Set<OperationsSettingsEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Bio).HasMaxLength(280).IsRequired();
            entity.Property(x => x.UsernameSlug).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => x.UsernameSlug).IsUnique();
            entity.Property(x => x.AvatarStorageKey).HasMaxLength(512);
            entity.Property(x => x.AvatarContentType).HasMaxLength(128);
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
            entity.Property(x => x.PersonalUsageBytes).HasDefaultValue(0L);
        });

        builder.Entity<UsernameReservation>(entity =>
        {
            entity.ToTable("username_reservations");
            entity.HasKey(x => x.NormalizedUsername);
            entity.Property(x => x.NormalizedUsername).HasMaxLength(30);
            entity.Property(x => x.Reason).HasMaxLength(64);
            entity.HasIndex(x => x.ExpiresAt);
        });

        builder.Entity<PendingEmailChange>(entity =>
        {
            entity.ToTable("pending_email_changes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.NewEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.NewEmailNormalized).HasMaxLength(320).IsRequired();
            entity.Property(x => x.ConfirmTokenHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CancelTokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.NewEmailNormalized);
            entity.HasIndex(x => x.ConfirmTokenHash).IsUnique();
            entity.HasIndex(x => x.CancelTokenHash).IsUnique();
        });

        builder.Entity<AddressRateCounter>(entity =>
        {
            entity.ToTable("address_rate_counters");
            entity.HasKey(x => x.AddressKeyHash);
            entity.Property(x => x.AddressKeyHash).HasMaxLength(64);
            entity.HasIndex(x => x.ExpiresAt);
        });

        builder.Entity<OperationsSettingsEntity>(entity =>
        {
            entity.ToTable("operations_settings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SmtpSettingsFingerprint).HasMaxLength(128);
        });

        builder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TargetType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TargetId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DetailsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Ip).HasMaxLength(64);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.HasIndex(x => x.OccurredAt);
            entity.HasIndex(x => x.ActorId);
        });

        builder.Entity<UserSession>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserId);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        builder.Entity<PlatformInvitation>(entity =>
        {
            entity.ToTable("platform_invitations");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Email);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        });

        builder.Entity<PasswordResetToken>(entity =>
        {
            entity.ToTable("password_reset_tokens");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        builder.Entity<TeamEntity>(entity =>
        {
            entity.ToTable("teams");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.JoinMode).HasDefaultValue(TeamJoinMode.RequestToJoin);
            entity.Property(x => x.DefaultJoinRole).HasDefaultValue(MemberRole.Viewer);
            entity.Property(x => x.JoinTokenCiphertext).HasMaxLength(1024);
            entity.Property(x => x.JoinTokenHash).HasMaxLength(128);
            entity.Property(x => x.JoinTokenPrefix).HasMaxLength(8);
            entity.HasIndex(x => x.JoinTokenHash)
                .IsUnique()
                .HasFilter("\"JoinTokenHash\" IS NOT NULL")
                .HasDatabaseName("ux_teams_join_token_hash");
            // DefaultJoinRole: Viewer (0) or Member (1) only.
            entity.ToTable(t => t.HasCheckConstraint("ck_teams_default_join_role", "\"DefaultJoinRole\" IN (0, 1)"));
        });

        builder.Entity<TeamJoinRequestEntity>(entity =>
        {
            entity.ToTable("team_join_requests");
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Team).WithMany(t => t.JoinRequests).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.TeamId, x.Status });
            entity.HasIndex(x => new { x.TeamId, x.UserId, x.Status });
            entity.HasIndex(x => x.UserId);
        });

        builder.Entity<TeamMemberEntity>(entity =>
        {
            entity.ToTable("team_members");
            entity.HasKey(x => new { x.TeamId, x.UserId });
            entity.HasOne(x => x.Team).WithMany(t => t.Members).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.UserId);
        });

        builder.Entity<ChannelEntity>(entity =>
        {
            entity.ToTable("channels");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.HasIndex(x => new { x.TeamId, x.Name }).IsUnique();
            entity.HasOne(x => x.Team).WithMany(t => t.Channels).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChannelMemberEntity>(entity =>
        {
            entity.ToTable("channel_members");
            // PK (ChannelId, UserId) enforces uniqueness. ReadOnly=true means a view-only grant (default false = may write).
            entity.HasKey(x => new { x.ChannelId, x.UserId });
            entity.HasOne(x => x.Channel).WithMany(c => c.Members).HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChannelJoinRequestEntity>(entity =>
        {
            entity.ToTable("channel_join_requests");
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Channel).WithMany(c => c.JoinRequests).HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ChannelId, x.Status });
            entity.HasIndex(x => new { x.ChannelId, x.UserId, x.Status });
        });

        builder.Entity<TeamInvitationEntity>(entity =>
        {
            entity.ToTable("team_invitations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChannelMessageEntity>(entity =>
        {
            entity.ToTable("channel_messages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Body).IsRequired();
            entity.HasOne<ChannelEntity>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ReplyToMessage)
                .WithMany()
                .HasForeignKey(x => x.ReplyToMessageId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => x.ReplyToMessageId).HasDatabaseName("ix_channel_messages_reply_to");
            // Keyset pagination order: (created_at, id) newest first.
            entity.HasIndex(x => new { x.ChannelId, x.CreatedAt, x.Id }).HasDatabaseName("ix_channel_messages_channel_created_id");
            entity.HasIndex(x => x.DeletedAt).HasFilter("\"DeletedAt\" IS NOT NULL").HasDatabaseName("ix_channel_messages_deleted_at");
            entity.ToTable(t =>
            {
                // char_length counts code points (not bytes/UTF-16 units); the API validates identically.
                t.HasCheckConstraint("ck_channel_messages_body_length", "char_length(\"Body\") <= 4000");
                // Soft-delete must scrub the body in the same write.
                t.HasCheckConstraint("ck_channel_messages_deleted_scrubbed", "\"DeletedAt\" IS NULL OR \"Body\" = ''");
            });
        });

        builder.Entity<MessageMentionEntity>(entity =>
        {
            entity.ToTable("message_mentions");
            entity.HasKey(x => new { x.MessageId, x.UserId });
            entity.HasOne(x => x.Message)
                .WithMany(m => m.Mentions)
                .HasForeignKey(x => x.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.UserId).HasDatabaseName("ix_message_mentions_user");
        });

        builder.Entity<MessageFileRefEntity>(entity =>
        {
            entity.ToTable("message_file_refs");
            entity.HasKey(x => new { x.MessageId, x.FileId });
            entity.HasOne(x => x.Message)
                .WithMany(m => m.FileRefs)
                .HasForeignKey(x => x.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.FileId).HasDatabaseName("ix_message_file_refs_file");
        });

        builder.Entity<NotificationEntity>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BodyPreview).HasMaxLength(280).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("ix_notifications_user_created");
            entity.HasIndex(x => new { x.UserId, x.ReadAt }).HasDatabaseName("ix_notifications_user_read");
        });

        builder.Entity<FolderEntity>(entity =>
        {
            entity.ToTable("folders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ChannelId, x.ParentId, x.Name });
            entity.HasIndex(x => new { x.OwnerUserId, x.ParentId, x.Name });
            entity.ToTable(t => t.HasCheckConstraint(
                "ck_folders_scope",
                "((\"ChannelId\" IS NOT NULL AND \"OwnerUserId\" IS NULL) OR (\"ChannelId\" IS NULL AND \"OwnerUserId\" IS NOT NULL))"));
        });

        builder.Entity<FileEntity>(entity =>
        {
            entity.ToTable("files");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
            entity.HasOne(x => x.Folder).WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ChannelId, x.FolderId, x.Name });
            entity.HasIndex(x => new { x.OwnerUserId, x.FolderId, x.Name });
            entity.HasIndex(x => x.UploadedBy);
            // Target of the raw-SQL composite FK from file_share_links (file_id, channel_id).
            entity.HasIndex(x => new { x.Id, x.ChannelId }).IsUnique().HasDatabaseName("ux_files_id_channel_id");
            entity.ToTable(t => t.HasCheckConstraint(
                "ck_files_scope",
                "((\"ChannelId\" IS NOT NULL AND \"OwnerUserId\" IS NULL) OR (\"ChannelId\" IS NULL AND \"OwnerUserId\" IS NOT NULL))"));
        });

        builder.Entity<UploadSessionEntity>(entity =>
        {
            entity.ToTable("upload_sessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.MultipartId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ObjectKey).HasMaxLength(512).IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => new { x.UserId, x.IdempotencyKey });
            entity.HasMany(x => x.Parts).WithOne(p => p.Session!).HasForeignKey(p => p.UploadSessionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UploadPartEntity>(entity =>
        {
            entity.ToTable("upload_parts");
            entity.HasKey(x => new { x.UploadSessionId, x.PartNumber });
            entity.Property(x => x.ETag).HasMaxLength(200).IsRequired();
        });

        builder.Entity<FileStarEntity>(entity =>
        {
            entity.ToTable("file_stars");
            entity.HasKey(x => new { x.UserId, x.FileId });
            entity.HasIndex(x => x.FileId);
        });

        builder.Entity<JobEntity>(entity =>
        {
            entity.ToTable("jobs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasMaxLength(64).IsRequired();
            entity.Property(x => x.State).HasMaxLength(32).IsRequired();
            entity.Property(x => x.DetailsJson).HasColumnType("jsonb");
            entity.Property(x => x.Error).HasMaxLength(2000);
            entity.Property(x => x.Attempts).HasDefaultValue(0);
            entity.HasIndex(x => x.Type);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => new { x.Type, x.State, x.AvailableAt }).HasDatabaseName("ix_jobs_claim");
        });

        builder.Entity<BackupEntity>(entity =>
        {
            entity.ToTable("backups");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Directory).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.Kind).HasMaxLength(32).IsRequired();
            entity.Property(x => x.State).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Error).HasMaxLength(2000);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.Kind);
        });

        builder.Entity<FileShareLinkEntity>(entity =>
        {
            entity.ToTable("file_share_links");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            entity.Property(x => x.FileId).HasColumnName("file_id");
            entity.Property(x => x.ChannelId).HasColumnName("channel_id").IsRequired();
            entity.Property(x => x.OriginalChannelId).HasColumnName("original_channel_id").IsRequired();
            entity.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
            entity.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedBy).HasColumnName("created_by");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.MaxDownloads).HasColumnName("max_downloads");
            entity.Property(x => x.DownloadCount).HasColumnName("download_count").HasDefaultValue(0);
            entity.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            entity.Property(x => x.RevokedBy).HasColumnName("revoked_by");
            entity.Property(x => x.RevokeReason).HasColumnName("revoke_reason").HasMaxLength(64);
            entity.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_file_share_links_token_hash");
            entity.HasIndex(x => x.FileId).HasDatabaseName("ix_file_share_links_file_id");
            entity.HasIndex(x => x.ChannelId).HasDatabaseName("ix_file_share_links_channel_id");
            entity.HasIndex(x => new { x.TeamId, x.RevokedAt }).HasDatabaseName("ix_file_share_links_team_revoked");
            // No HasOne/HasMany to FileEntity on purpose: composite FK + ON DELETE SET NULL (file_id)
            // is created via raw SQL in the AddFileShareLinks migration.
        });
    }
}
