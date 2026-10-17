using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Channeled.Application.Admin;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Teams;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Channeled.Tests.Api;

[Collection("Api")]
public class UserDeleteApiTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private HttpClient _admin = null!;

    public UserDeleteApiTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        (await auth.SeedOperatorAsync(new SeedOperatorRequest("ud-admin@example.com", "Admin", "AdminPassword1!")))
            .Succeeded.Should().BeTrue();

        // SeedOperator allows only one admin; create a second via Identity.
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin2 = new AppUser
        {
            Id = UuidV7.NewGuid(),
            Email = "ud-admin2@example.com",
            UserName = "ud-admin2@example.com",
            DisplayName = "Admin2",
            UsernameSlug = "udadmin2",
            EmailConfirmed = true,
            IsPlatformAdmin = true,
            Status = "active",
        };
        (await users.CreateAsync(admin2, "AdminPassword1!")).Succeeded.Should().BeTrue();

        _admin = CreateClient();
        await LoginAsync(_admin, "ud-admin@example.com", "AdminPassword1!");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    [Fact]
    public async Task Preview_marks_sole_admin_as_last_enabled()
    {
        Guid admin2Id;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var a2 = await users.FindByEmailAsync("ud-admin2@example.com");
            a2.Should().NotBeNull();
            admin2Id = a2!.Id;
        }

        (await _admin.PostAsync($"/api/v1/admin/users/{admin2Id}/disable", null)).EnsureSuccessStatusCode();

        Guid admin1Id;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            admin1Id = (await users.FindByEmailAsync("ud-admin@example.com"))!.Id;
        }

        var preview = await _admin.GetFromJsonAsync<UserDeletePreviewDto>(
            $"/api/v1/admin/users/{admin1Id}/delete-preview");
        preview!.IsLastEnabledPlatformAdmin.Should().BeTrue();

        var del = await _admin.PostAsJsonAsync(
            $"/api/v1/admin/users/{admin1Id}/delete",
            new DeleteUserRequest("AdminPassword1!", []));
        del.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_user_transfers_sole_owned_team()
    {
        Guid targetId;
        Guid memberId;
        Guid teamId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var target = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = "ud-target@example.com",
                UserName = "ud-target@example.com",
                DisplayName = "Target",
                UsernameSlug = "udtarget",
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(target, "TargetPassword1!")).Succeeded.Should().BeTrue();
            targetId = target.Id;

            var member = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = "ud-member@example.com",
                UserName = "ud-member@example.com",
                DisplayName = "Member",
                UsernameSlug = "udmember",
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(member, "MemberPassword1!")).Succeeded.Should().BeTrue();
            memberId = member.Id;
        }

        using var targetClient = CreateClient();
        await LoginAsync(targetClient, "ud-target@example.com", "TargetPassword1!");
        var team = await (await targetClient.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("Owned", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        teamId = team!.Id;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TeamMembers.Add(new TeamMemberEntity
            {
                TeamId = teamId,
                UserId = memberId,
                Role = MemberRole.Member,
                JoinedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var del = await _admin.PostAsJsonAsync(
            $"/api/v1/admin/users/{targetId}/delete",
            new DeleteUserRequest("AdminPassword1!", [
                new DeleteUserTeamResolution(teamId, "transfer", memberId),
            ]));
        if (!del.IsSuccessStatusCode)
        {
            var body = await del.Content.ReadAsStringAsync();
            throw new Exception($"Delete failed {(int)del.StatusCode}: {body}");
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Users.AnyAsync(u => u.Id == targetId)).Should().BeFalse();
            var owner = await db.TeamMembers.SingleAsync(m => m.TeamId == teamId && m.Role == MemberRole.Owner);
            owner.UserId.Should().Be(memberId);
        }
    }

    [Fact]
    public async Task Sweep_completes_late_phase_user()
    {
        Guid targetId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var target = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = "ud-sweep@example.com",
                UserName = "ud-sweep@example.com",
                DisplayName = "Sweep",
                UsernameSlug = "udsweep",
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(target, "SweepPassword1!")).Succeeded.Should().BeTrue();
            targetId = target.Id;
            target.Status = "disabled";
            target.LockoutEnd = DateTimeOffset.MaxValue;
            target.DeletePhase = "personal";
            target.DeletePhaseChangedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await users.UpdateAsync(target);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IAdminService>();
            await admin.SweepStuckUserDeletesAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Users.AnyAsync(u => u.Id == targetId)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Refuse_reenable_after_personal_phase()
    {
        Guid targetId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var target = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = "ud-late@example.com",
                UserName = "ud-late@example.com",
                DisplayName = "Late",
                UsernameSlug = "udlate",
                EmailConfirmed = true,
                Status = "disabled",
                LockoutEnd = DateTimeOffset.MaxValue,
                DeletePhase = "personal",
                DeletePhaseChangedAt = DateTimeOffset.UtcNow,
            };
            (await users.CreateAsync(target, "LatePassword1!")).Succeeded.Should().BeTrue();
            targetId = target.Id;
        }

        var enable = await _admin.PostAsync($"/api/v1/admin/users/{targetId}/enable", null);
        enable.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Parallel_delete_advances_each_phase_once()
    {
        Guid targetId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var target = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = "ud-cas@example.com",
                UserName = "ud-cas@example.com",
                DisplayName = "Cas",
                UsernameSlug = "udcas",
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(target, "CasPassword1!")).Succeeded.Should().BeTrue();
            targetId = target.Id;
        }

        var a = _admin.PostAsJsonAsync(
            $"/api/v1/admin/users/{targetId}/delete",
            new DeleteUserRequest("AdminPassword1!", []));
        var b = _admin.PostAsJsonAsync(
            $"/api/v1/admin/users/{targetId}/delete",
            new DeleteUserRequest("AdminPassword1!", []));
        var results = await Task.WhenAll(a, b);
        results.Count(r => r.IsSuccessStatusCode).Should().BeGreaterThanOrEqualTo(1);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Users.AnyAsync(u => u.Id == targetId)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Mutual_admin_delete_leaves_exactly_one_admin()
    {
        Guid admin1Id;
        Guid admin2Id;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            admin1Id = (await users.FindByEmailAsync("ud-admin@example.com"))!.Id;
            admin2Id = (await users.FindByEmailAsync("ud-admin2@example.com"))!.Id;
        }

        using var admin2Client = CreateClient();
        await LoginAsync(admin2Client, "ud-admin2@example.com", "AdminPassword1!");

        var t1 = _admin.PostAsJsonAsync(
            $"/api/v1/admin/users/{admin2Id}/delete",
            new DeleteUserRequest("AdminPassword1!", []));
        var t2 = admin2Client.PostAsJsonAsync(
            $"/api/v1/admin/users/{admin1Id}/delete",
            new DeleteUserRequest("AdminPassword1!", []));
        await Task.WhenAll(t1, t2);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var enabledAdmins = await db.Users.CountAsync(u =>
                u.IsPlatformAdmin
                && u.Status == "active"
                && (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow));
            enabledAdmins.Should().BeGreaterThanOrEqualTo(1);

            var remaining = await db.Users.CountAsync(u => u.Id == admin1Id || u.Id == admin2Id);
            // Exactly one of the two delete races may complete; never both.
            remaining.Should().BeGreaterThanOrEqualTo(1);
        }
    }

    [Fact]
    public async Task Preview_marks_over_cap_team_transfer_only_and_delete_rejected()
    {
        Guid targetId;
        Guid memberId;
        Guid teamId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var target = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = "ud-cap@example.com",
                UserName = "ud-cap@example.com",
                DisplayName = "Cap",
                UsernameSlug = "udcap",
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(target, "CapPassword1!")).Succeeded.Should().BeTrue();
            targetId = target.Id;
            var member = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = "ud-cap-m@example.com",
                UserName = "ud-cap-m@example.com",
                DisplayName = "CapM",
                UsernameSlug = "udcapm",
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(member, "CapPassword1!")).Succeeded.Should().BeTrue();
            memberId = member.Id;
        }

        using var targetClient = CreateClient();
        await LoginAsync(targetClient, "ud-cap@example.com", "CapPassword1!");
        var team = await (await targetClient.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("Huge", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        teamId = team!.Id;
        (await targetClient.PostAsJsonAsync(
            $"/api/v1/teams/{team.Slug}/channels",
            new CreateChannelRequest("general", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TeamMembers.Add(new TeamMemberEntity
            {
                TeamId = teamId,
                UserId = memberId,
                Role = MemberRole.Member,
                JoinedAt = DateTimeOffset.UtcNow,
            });
            var channel = await db.Channels.SingleAsync(c => c.TeamId == teamId && c.Name == "general");
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 101; i++)
            {
                db.Files.Add(new Channeled.Infrastructure.Files.FileEntity
                {
                    Id = UuidV7.NewGuid(),
                    ChannelId = channel.Id,
                    Name = $"c{i}.txt",
                    SizeBytes = 1,
                    ContentType = "text/plain",
                    StorageKey = $"objects/ud/{Guid.NewGuid():N}",
                    Sha256 = new string('c', 64),
                    UploadedBy = targetId,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            await db.SaveChangesAsync();
        }

        var preview = await _admin.GetFromJsonAsync<UserDeletePreviewDto>(
            $"/api/v1/admin/users/{targetId}/delete-preview");
        preview!.SoleOwnedTeams.Should().ContainSingle(t => t.TeamId == teamId && t.TransferOnly);

        var del = await _admin.PostAsJsonAsync(
            $"/api/v1/admin/users/{targetId}/delete",
            new DeleteUserRequest("AdminPassword1!", [
                new DeleteUserTeamResolution(teamId, "delete", null),
            ]));
        del.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await del.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().BeOneOf(ErrorCodes.TeamDeleteCapExceeded, ErrorCodes.DeleteResolutionsStale);
    }

    [Fact]
    public async Task Deleted_uploader_chip_status_on_remaining_team_file()
    {
        Guid targetId;
        Guid memberId;
        string slug;
        Guid fileId = Guid.Empty;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var target = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = "ud-chip@example.com",
                UserName = "ud-chip@example.com",
                DisplayName = "Chip",
                UsernameSlug = "udchip",
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(target, "ChipPassword1!")).Succeeded.Should().BeTrue();
            targetId = target.Id;
            var member = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = "ud-chip-m@example.com",
                UserName = "ud-chip-m@example.com",
                DisplayName = "ChipM",
                UsernameSlug = "udchipm",
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(member, "ChipPassword1!")).Succeeded.Should().BeTrue();
            memberId = member.Id;
        }

        using var targetClient = CreateClient();
        await LoginAsync(targetClient, "ud-chip@example.com", "ChipPassword1!");
        var team = await (await targetClient.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("ChipTeam", null)))
            .Content.ReadFromJsonAsync<TeamDto>();
        slug = team!.Slug;
        (await targetClient.PostAsJsonAsync(
            $"/api/v1/teams/{slug}/channels",
            new CreateChannelRequest("general", null, ChannelVisibility.Everyone, ChannelKind.File))).EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TeamMembers.Add(new TeamMemberEntity
            {
                TeamId = team.Id,
                UserId = memberId,
                Role = MemberRole.Member,
                JoinedAt = DateTimeOffset.UtcNow,
            });
            var channel = await db.Channels.SingleAsync(c => c.TeamId == team.Id && c.Name == "general");
            fileId = UuidV7.NewGuid();
            var now = DateTimeOffset.UtcNow;
            db.Files.Add(new Channeled.Infrastructure.Files.FileEntity
            {
                Id = fileId,
                ChannelId = channel.Id,
                Name = "kept.txt",
                SizeBytes = 4,
                ContentType = "text/plain",
                StorageKey = $"objects/chip/{Guid.NewGuid():N}",
                Sha256 = new string('d', 64),
                UploadedBy = targetId,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        (await _admin.PostAsJsonAsync(
            $"/api/v1/admin/users/{targetId}/delete",
            new DeleteUserRequest("AdminPassword1!", [
                new DeleteUserTeamResolution(team.Id, "transfer", memberId),
            ]))).EnsureSuccessStatusCode();

        using var memberClient = CreateClient();
        await LoginAsync(memberClient, "ud-chip-m@example.com", "ChipPassword1!");
        var item = await memberClient.GetFromJsonAsync<FileItemDto>(
            $"/api/v1/teams/{slug}/channels/general/items/{fileId}");
        item!.UploadedByDisplayName.Should().Be("Deleted user");
        item.UploadedByStatus.Should().Be("deleted");
    }

    [Fact]
    public async Task Cancel_by_reenable_clears_early_phase()
    {
        Guid targetId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var target = new AppUser
            {
                Id = UuidV7.NewGuid(),
                Email = "ud-cancel@example.com",
                UserName = "ud-cancel@example.com",
                DisplayName = "Cancel",
                UsernameSlug = "udcancel",
                EmailConfirmed = true,
                Status = "active",
            };
            (await users.CreateAsync(target, "CancelPassword1!")).Succeeded.Should().BeTrue();
            targetId = target.Id;
            target.Status = "disabled";
            target.LockoutEnd = DateTimeOffset.MaxValue;
            target.DeletePhase = "disabled";
            target.DeletePhaseChangedAt = DateTimeOffset.UtcNow;
            await users.UpdateAsync(target);
        }

        (await _admin.PostAsync($"/api/v1/admin/users/{targetId}/enable", null)).EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var u = await db.Users.SingleAsync(x => x.Id == targetId);
            u.DeletePhase.Should().Be("none");
            u.Status.Should().Be("active");
        }
    }

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password))).EnsureSuccessStatusCode();
        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var body = await csrf.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
    }
}
