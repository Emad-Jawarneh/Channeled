using Channeled.Domain.Audit;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Identity;
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
    public DbSet<TeamInvitationEntity> TeamInvitations => Set<TeamInvitationEntity>();
    public DbSet<FolderEntity> Folders => Set<FolderEntity>();
    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<UploadSessionEntity> UploadSessions => Set<UploadSessionEntity>();
    public DbSet<UploadPartEntity> UploadParts => Set<UploadPartEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
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
            entity.HasKey(x => new { x.ChannelId, x.UserId });
            entity.HasOne(x => x.Channel).WithMany(c => c.Members).HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
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
    }
}
