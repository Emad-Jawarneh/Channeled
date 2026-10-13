using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Notifications;
using Channeled.Domain.Teams;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Channeled.Tests.Api;

/// <summary>RequestToJoin → OpenJoin conversion: exact pending set, audit trail, and races with join POSTs.</summary>
[Collection("Api")]
public class TeamJoinConversionTests(AuthApiFactory factory) : TeamPrivacyTestBase(factory), IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Conversion_approves_exactly_the_pending_set_with_one_audit_per_user()
    {
        var alice = await NewUserAsync("Alice");
        var bob = await NewUserAsync("Bob");
        var carol = await NewUserAsync("Carol"); // rejected earlier
        var dave = await NewUserAsync("Dave"); // pending, but account disabled before conversion
        var erin = await NewUserAsync("Erin"); // withdrew earlier

        var carolReq = await JoinAsync(carol);
        (await Owner.Client.PostAsync(TeamUrl($"/join-requests/{carolReq.PendingRequestId}/reject"), null)).EnsureSuccessStatusCode();
        var erinReq = await JoinAsync(erin);
        (await erin.Client.PostAsync($"/api/v1/teams/join-requests/{erinReq.PendingRequestId}/withdraw", null)).EnsureSuccessStatusCode();
        await JoinAsync(alice);
        await JoinAsync(bob);
        await JoinAsync(dave);
        await DbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == dave.Id);
            user.Status = "disabled";
            await db.SaveChangesAsync();
        });

        var resp = await Owner.Client.PutAsJsonAsync(
            TeamUrl("/settings"),
            new UpdateTeamSettingsRequest(null, null, TeamJoinMode.OpenJoin, MemberRole.Member, ConfirmOpenJoinApprovePending: true));
        resp.EnsureSuccessStatusCode();
        var result = (await resp.Content.ReadFromJsonAsync<UpdateTeamSettingsResult>())!;
        result.ApprovedCount.Should().Be(2);

        await DbAsync(async db =>
        {
            var members = await db.TeamMembers.AsNoTracking().Where(m => m.TeamId == TeamId).ToListAsync();
            members.Select(m => m.UserId).Should().BeEquivalentTo([Owner.Id, alice.Id, bob.Id]);
            members.Where(m => m.UserId != Owner.Id).Should().OnlyContain(m => m.Role == MemberRole.Member);

            var statuses = await db.TeamJoinRequests.AsNoTracking().Where(r => r.TeamId == TeamId)
                .ToDictionaryAsync(r => r.UserId, r => r.Status);
            statuses[alice.Id].Should().Be(TeamJoinRequestStatus.Approved);
            statuses[bob.Id].Should().Be(TeamJoinRequestStatus.Approved);
            statuses[carol.Id].Should().Be(TeamJoinRequestStatus.Rejected);
            statuses[dave.Id].Should().Be(TeamJoinRequestStatus.Rejected, "closed without membership: account is disabled");
            statuses[erin.Id].Should().Be(TeamJoinRequestStatus.Withdrawn);

            (await db.Notifications.CountAsync(n => n.TeamId == TeamId && n.Type == NotificationType.TeamJoinRequest))
                .Should().Be(0, "no pending request remains after conversion");
        });

        (await CountAuditAsync(AuditEventType.TeamJoinRequestApprove)).Should().Be(2);
        foreach (var approved in new[] { alice, bob })
        {
            (await CountAuditAsync(AuditEventType.TeamJoinRequestApprove, approved.Id)).Should().Be(1, approved.Name);
        }

        foreach (var untouched in new[] { carol, dave, erin })
        {
            (await CountAuditAsync(AuditEventType.TeamJoinRequestApprove, untouched.Id)).Should().Be(0, untouched.Name);
        }

        (await CountAuditAsync(AuditEventType.TeamJoinModeChange)).Should().Be(1);

        // Re-sending the same settings is a no-op: no second conversion, no duplicate approvals.
        var again = await Owner.Client.PutAsJsonAsync(
            TeamUrl("/settings"),
            new UpdateTeamSettingsRequest(null, null, TeamJoinMode.OpenJoin, MemberRole.Member, ConfirmOpenJoinApprovePending: true));
        again.EnsureSuccessStatusCode();
        (await again.Content.ReadFromJsonAsync<UpdateTeamSettingsResult>())!.ApprovedCount.Should().BeNull();
        (await CountAuditAsync(AuditEventType.TeamJoinRequestApprove)).Should().Be(2);
        (await CountAuditAsync(AuditEventType.TeamJoinModeChange)).Should().Be(1);
    }

    /// <summary>
    /// Barrier race: an external transaction holds the team row <c>FOR UPDATE</c> while a join POST and the conversion
    /// both queue behind it (Postgres lock queues are FIFO), so the order is deterministic. Either way nobody is left
    /// pending and every user gets exactly one approve audit.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Conversion_and_join_post_serialize_on_the_team_row_in_either_order(bool joinQueuesFirst)
    {
        var alice = await NewUserAsync("Alice");
        var bob = await NewUserAsync("Bob");
        await JoinAsync(alice);

        await using var holder = new NpgsqlConnection(AuthApiFactory.ConnectionString);
        await holder.OpenAsync();
        await using var tx = await holder.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand("""SELECT "Id" FROM teams WHERE "Id" = @id FOR UPDATE""", holder, tx))
        {
            lockCmd.Parameters.AddWithValue("id", TeamId);
            await lockCmd.ExecuteScalarAsync();
        }

        Task<HttpResponseMessage> StartJoin() => Task.Run(() => bob.Client.PostAsync($"/api/v1/teams/join/{Token}", null));
        Task<HttpResponseMessage> StartConversion() => Task.Run(() => Owner.Client.PutAsJsonAsync(
            TeamUrl("/settings"),
            new UpdateTeamSettingsRequest(null, null, TeamJoinMode.OpenJoin, MemberRole.Member, ConfirmOpenJoinApprovePending: true)));

        Task<HttpResponseMessage> first, second;
        if (joinQueuesFirst)
        {
            first = StartJoin();
            await WaitForQueuedOnTeamLockAsync(1);
            second = StartConversion();
        }
        else
        {
            first = StartConversion();
            await WaitForQueuedOnTeamLockAsync(1);
            second = StartJoin();
        }

        await WaitForQueuedOnTeamLockAsync(2);
        first.IsCompleted.Should().BeFalse("the team row lock is held");
        second.IsCompleted.Should().BeFalse("the team row lock is held");
        await tx.CommitAsync();

        var joinResp = joinQueuesFirst ? await first : await second;
        var convResp = joinQueuesFirst ? await second : await first;
        joinResp.StatusCode.Should().Be(HttpStatusCode.OK);
        convResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var join = (await joinResp.Content.ReadFromJsonAsync<TeamJoinResultDto>())!;
        var conversion = (await convResp.Content.ReadFromJsonAsync<UpdateTeamSettingsResult>())!;

        if (joinQueuesFirst)
        {
            join.Joined.Should().BeFalse("the join ran first, while still RequestToJoin");
            conversion.ApprovedCount.Should().Be(2, "the conversion sees the request the join committed before it");
        }
        else
        {
            join.Joined.Should().BeTrue("the conversion committed first, so the join is an immediate Open Join");
            join.Role.Should().Be(MemberRole.Member);
            conversion.ApprovedCount.Should().Be(1);
        }

        await DbAsync(async db =>
        {
            (await db.TeamJoinRequests.CountAsync(r => r.TeamId == TeamId && r.Status == TeamJoinRequestStatus.Pending))
                .Should().Be(0, "no request may be left stranded in Pending");
            (await db.TeamMembers.CountAsync(m => m.TeamId == TeamId && (m.UserId == alice.Id || m.UserId == bob.Id)))
                .Should().Be(2);
            (await db.Notifications.CountAsync(n => n.TeamId == TeamId && n.Type == NotificationType.TeamJoinRequest)).Should().Be(0);
        });
        (await CountAuditAsync(AuditEventType.TeamJoinRequestApprove, alice.Id)).Should().Be(1);
        (await CountAuditAsync(AuditEventType.TeamJoinRequestApprove, bob.Id)).Should().Be(1);
        (await CountAuditAsync(AuditEventType.TeamJoinModeChange)).Should().Be(1);
    }

    private static async Task WaitForQueuedOnTeamLockAsync(int expected)
    {
        await using var conn = new NpgsqlConnection(AuthApiFactory.ConnectionString);
        await conn.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        long queued = 0;
        while (DateTime.UtcNow < deadline)
        {
            await using var cmd = new NpgsqlCommand(
                """
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database() AND pid <> pg_backend_pid()
                  AND wait_event_type = 'Lock' AND query ILIKE '%FROM teams%FOR UPDATE%'
                """,
                conn);
            queued = (long)(await cmd.ExecuteScalarAsync())!;
            if (queued >= expected)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Expected {expected} statements waiting on the team lock, saw {queued}.");
    }
}

/// <summary>Host with the Open Join conversion cap lowered to 5 (<c>TeamJoin:MaxOpenJoinPendingApprove</c>).</summary>
public sealed class LowPendingCapFactory : AuthApiFactory
{
    public const int Cap = 5;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TeamJoinOptions.SectionName}:{nameof(TeamJoinOptions.MaxOpenJoinPendingApprove)}"] = Cap.ToString(),
            });
        });
    }
}

[Collection("Api")]
public class TeamJoinCapTests(LowPendingCapFactory factory) : TeamPrivacyTestBase(factory), IClassFixture<LowPendingCapFactory>
{
    [Fact]
    public async Task Conversion_with_more_than_the_configured_cap_pending_fails_without_partial_apply()
    {
        var pending = new List<Person>();
        for (var i = 0; i < LowPendingCapFactory.Cap + 1; i++)
        {
            var person = await NewUserAsync($"Joiner{i}");
            await JoinAsync(person);
            pending.Add(person);
        }

        var failed = await Owner.Client.PutAsJsonAsync(
            TeamUrl("/settings"),
            new UpdateTeamSettingsRequest("Renamed", null, TeamJoinMode.OpenJoin, MemberRole.Member, ConfirmOpenJoinApprovePending: true));
        failed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await failed.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("open_join_too_many_pending");
        body.GetProperty("detail").GetString().Should().Contain($"More than {LowPendingCapFactory.Cap} join requests");

        var team = (await Owner.Client.GetFromJsonAsync<TeamDto>(TeamUrl()))!;
        team.JoinMode.Should().Be(TeamJoinMode.RequestToJoin);
        team.DefaultJoinRole.Should().Be(MemberRole.Viewer);
        team.Name.Should().Be("PrivacyTeam");
        await DbAsync(async db =>
        {
            (await db.TeamJoinRequests.CountAsync(r => r.TeamId == TeamId && r.Status == TeamJoinRequestStatus.Pending))
                .Should().Be(LowPendingCapFactory.Cap + 1);
            (await db.TeamMembers.CountAsync(m => m.TeamId == TeamId)).Should().Be(1);
            (await db.Notifications.CountAsync(n => n.TeamId == TeamId && n.Type == NotificationType.TeamJoinRequest))
                .Should().BePositive("the Owner's pending-request notification is untouched");
        });
        (await CountAuditAsync(AuditEventType.TeamJoinRequestApprove)).Should().Be(0);
        (await CountAuditAsync(AuditEventType.TeamJoinModeChange)).Should().Be(0);

        // Clear one from the queue: exactly at the cap the conversion is accepted and approves everyone.
        var requests = (await Owner.Client.GetFromJsonAsync<List<TeamJoinRequestDto>>(TeamUrl("/join-requests")))!;
        requests.Should().HaveCount(LowPendingCapFactory.Cap + 1);
        (await Owner.Client.PostAsync(TeamUrl($"/join-requests/{requests[0].Id}/reject"), null)).EnsureSuccessStatusCode();

        var ok = await Owner.Client.PutAsJsonAsync(
            TeamUrl("/settings"),
            new UpdateTeamSettingsRequest(null, null, TeamJoinMode.OpenJoin, MemberRole.Member, ConfirmOpenJoinApprovePending: true));
        ok.EnsureSuccessStatusCode();
        (await ok.Content.ReadFromJsonAsync<UpdateTeamSettingsResult>())!.ApprovedCount.Should().Be(LowPendingCapFactory.Cap);
        (await CountAuditAsync(AuditEventType.TeamJoinRequestApprove)).Should().Be(LowPendingCapFactory.Cap);
    }
}
