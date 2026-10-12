using Channeled.Domain.Audit;
using Channeled.Infrastructure.Identity;
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
    }
}
