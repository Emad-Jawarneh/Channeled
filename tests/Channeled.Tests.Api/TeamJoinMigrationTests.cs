using Channeled.Domain.Teams;
using Channeled.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Channeled.Tests.Api;

/// <summary>
/// Runs the real <c>TeamPrivacyJoinAndReadOnly</c> migration against seeded old-schema data.
/// Contract: legacy <c>channel_members.CanWrite = false</c> → <c>ReadOnly = true</c> (CanWrite = true → false);
/// <c>CanManage</c> is dropped (managing channel membership is Admin+ only); existing teams default to
/// RequestToJoin / Viewer with no join token. A Member whose grant is ReadOnly cannot write
/// (see <c>TeamPrivacyAuthzTests</c> and the Authorizer unit tests).
/// The shared test database is rolled back one migration, seeded, migrated forward, and always returned to the latest schema.
/// </summary>
[Collection("Api")]
public class TeamJoinMigrationTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string PreviousMigration = "20261013130949_JobAttemptsAvailableAt";

    [Fact]
    public async Task Up_maps_can_write_false_to_read_only_true_drops_can_manage_and_defaults_teams_safely()
    {
        await factory.ResetDatabaseAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(AuthApiFactory.ConnectionString).Options;
        await using var db = new ApplicationDbContext(options);
        var migrator = db.GetService<IMigrator>();

        var teamId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var users = new
        {
            ReadWrite = Guid.NewGuid(), // CanWrite=true,  CanManage=false
            ReadOnly = Guid.NewGuid(), //  CanWrite=false, CanManage=false
            ManageOnly = Guid.NewGuid(), // CanWrite=false, CanManage=true  (manage-only grants become view-only)
            Full = Guid.NewGuid(), //      CanWrite=true,  CanManage=true
        };

        try
        {
            // ---- old schema (before the privacy migration) ----
            await migrator.MigrateAsync(PreviousMigration);
            (await ColumnsAsync("channel_members")).Should().Contain(["CanWrite", "CanManage"]).And.NotContain("ReadOnly");

            await ExecAsync(
                """INSERT INTO teams ("Id","Name","Slug","QuotaBytes","UsageBytes","CreatedAt") VALUES (@t,'Legacy','legacy-team',1073741824,0,now())""",
                ("t", teamId));
            await ExecAsync(
                """INSERT INTO channels ("Id","TeamId","Name","Visibility","Kind","CreatedAt") VALUES (@c,@t,'private',@v,0,now())""",
                ("c", channelId), ("t", teamId), ("v", (int)ChannelVisibility.Restricted));
            foreach (var (user, canWrite, canManage) in new[]
                     {
                         (users.ReadWrite, true, false),
                         (users.ReadOnly, false, false),
                         (users.ManageOnly, false, true),
                         (users.Full, true, true),
                     })
            {
                await ExecAsync(
                    """INSERT INTO channel_members ("ChannelId","UserId","CanWrite","CanManage") VALUES (@c,@u,@w,@m)""",
                    ("c", channelId), ("u", user), ("w", canWrite), ("m", canManage));
            }

            // ---- migrate forward ----
            await migrator.MigrateAsync();

            var columns = await ColumnsAsync("channel_members");
            columns.Should().Contain("ReadOnly").And.NotContain(["CanWrite", "CanManage"]);
            var readOnly = await ReadOnlyByUserAsync(channelId);
            readOnly[users.ReadWrite].Should().BeFalse();
            readOnly[users.ReadOnly].Should().BeTrue("can_write=false → ReadOnly=true");
            readOnly[users.ManageOnly].Should().BeTrue("a manage-only grant could not write, so it stays view-only");
            readOnly[users.Full].Should().BeFalse();

            await using (var conn = new NpgsqlConnection(AuthApiFactory.ConnectionString))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    """SELECT "JoinMode","DefaultJoinRole","JoinTokenHash","JoinTokenCiphertext" FROM teams WHERE "Id" = @t""", conn);
                cmd.Parameters.AddWithValue("t", teamId);
                await using var reader = await cmd.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                reader.GetInt32(0).Should().Be((int)TeamJoinMode.RequestToJoin, "existing teams must not become open");
                reader.GetInt32(1).Should().Be((int)MemberRole.Viewer);
                reader.IsDBNull(2).Should().BeTrue("legacy teams start without a join token");
                reader.IsDBNull(3).Should().BeTrue();
            }

            // ---- and back: Down reconstructs CanWrite from ReadOnly (CanManage cannot be recovered) ----
            await migrator.MigrateAsync(PreviousMigration);
            (await ColumnsAsync("channel_members")).Should().Contain(["CanWrite", "CanManage"]).And.NotContain("ReadOnly");
            var legacy = await CanWriteByUserAsync(channelId);
            legacy[users.ReadWrite].Should().BeTrue();
            legacy[users.ReadOnly].Should().BeFalse();
            legacy[users.ManageOnly].Should().BeFalse();
            legacy[users.Full].Should().BeTrue();

            // Round trip is stable.
            await migrator.MigrateAsync();
            (await ReadOnlyByUserAsync(channelId)).Should().BeEquivalentTo(readOnly);
        }
        finally
        {
            await db.Database.MigrateAsync(); // leave the shared database at the latest schema for other tests
            await factory.ResetDatabaseAsync();
        }
    }

    private static async Task<List<string>> ColumnsAsync(string table)
    {
        await using var conn = new NpgsqlConnection(AuthApiFactory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @t", conn);
        cmd.Parameters.AddWithValue("t", table);
        var result = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static Task<Dictionary<Guid, bool>> ReadOnlyByUserAsync(Guid channelId) =>
        ReadFlagsAsync("""SELECT "UserId","ReadOnly" FROM channel_members WHERE "ChannelId" = @c""", channelId);

    private static Task<Dictionary<Guid, bool>> CanWriteByUserAsync(Guid channelId) =>
        ReadFlagsAsync("""SELECT "UserId","CanWrite" FROM channel_members WHERE "ChannelId" = @c""", channelId);

    private static async Task<Dictionary<Guid, bool>> ReadFlagsAsync(string sql, Guid channelId)
    {
        await using var conn = new NpgsqlConnection(AuthApiFactory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("c", channelId);
        var result = new Dictionary<Guid, bool>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetGuid(0)] = reader.GetBoolean(1);
        }

        return result;
    }

    private static async Task ExecAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(AuthApiFactory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync();
    }
}
