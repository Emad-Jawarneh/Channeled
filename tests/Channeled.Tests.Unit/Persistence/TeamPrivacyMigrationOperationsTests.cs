using Channeled.Domain.Teams;
using Channeled.Infrastructure.Persistence.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Channeled.Tests.Unit.Persistence;

/// <summary>
/// Pins the shape of the <c>TeamPrivacyJoinAndReadOnly</c> migration without a database:
/// the legacy <c>channel_members.CanWrite = false</c> rows MUST become <c>ReadOnly = true</c> (and CanWrite = true rows
/// <c>ReadOnly = false</c>) BEFORE the old columns are dropped. The DB-backed counterpart (seeded old-schema data,
/// real Up/Down) is <c>Channeled.Tests.Api.TeamJoinMigrationTests</c>.
/// </summary>
public class TeamPrivacyMigrationOperationsTests
{
    private readonly TeamPrivacyJoinAndReadOnly _migration = new();

    [Fact]
    public void Up_backfills_ReadOnly_as_NOT_CanWrite_before_dropping_the_legacy_columns()
    {
        var ops = _migration.UpOperations;

        var add = ops.OfType<AddColumnOperation>().Single(o => o.Table == "channel_members" && o.Name == "ReadOnly");
        add.ClrType.Should().Be(typeof(bool));
        add.IsNullable.Should().BeFalse();
        add.DefaultValue.Should().Be(false, "new grants are writable unless explicitly read-only");

        var backfill = ops.OfType<SqlOperation>().Single(o => Normalize(o.Sql).Contains("\"ReadOnly\" = NOT \"CanWrite\""));
        Normalize(backfill.Sql).Should().Be("UPDATE channel_members SET \"ReadOnly\" = NOT \"CanWrite\";");
        backfill.Sql.Should().NotContain("WHERE", "every legacy row is converted, not a subset");

        var dropCanWrite = ops.OfType<DropColumnOperation>().Single(o => o.Table == "channel_members" && o.Name == "CanWrite");
        var dropCanManage = ops.OfType<DropColumnOperation>().Single(o => o.Table == "channel_members" && o.Name == "CanManage");

        var order = ops.ToList();
        order.IndexOf(add).Should().BeLessThan(order.IndexOf(backfill));
        order.IndexOf(backfill).Should().BeLessThan(order.IndexOf(dropCanWrite), "CanWrite must still exist while backfilling");
        order.IndexOf(backfill).Should().BeLessThan(order.IndexOf(dropCanManage));
    }

    [Fact]
    public void Up_adds_join_settings_with_safe_defaults_and_a_role_ceiling()
    {
        var ops = _migration.UpOperations;

        ops.OfType<AddColumnOperation>().Single(o => o.Table == "teams" && o.Name == "JoinMode")
            .DefaultValue.Should().Be((int)TeamJoinMode.RequestToJoin, "existing teams must not silently become open");
        ops.OfType<AddColumnOperation>().Single(o => o.Table == "teams" && o.Name == "DefaultJoinRole")
            .DefaultValue.Should().Be((int)MemberRole.Viewer);

        var check = ops.OfType<AddCheckConstraintOperation>().Single(o => o.Name == "ck_teams_default_join_role");
        Normalize(check.Sql).Should().Be("\"DefaultJoinRole\" IN (0, 1)");
        ((int)MemberRole.Viewer).Should().Be(0);
        ((int)MemberRole.Member).Should().Be(1);

        ops.OfType<CreateIndexOperation>().Should().Contain(i => i.Name == "ux_teams_join_token_hash" && i.IsUnique);
    }

    [Fact]
    public void Down_restores_CanWrite_from_ReadOnly_before_dropping_it()
    {
        var ops = _migration.DownOperations.ToList();

        var addCanWrite = ops.OfType<AddColumnOperation>().Single(o => o.Table == "channel_members" && o.Name == "CanWrite");
        var restore = ops.OfType<SqlOperation>().Single();
        Normalize(restore.Sql).Should().Be("UPDATE channel_members SET \"CanWrite\" = NOT \"ReadOnly\";");
        var dropReadOnly = ops.OfType<DropColumnOperation>().Single(o => o.Table == "channel_members" && o.Name == "ReadOnly");

        ops.IndexOf(addCanWrite).Should().BeLessThan(ops.IndexOf(restore));
        ops.IndexOf(restore).Should().BeLessThan(ops.IndexOf(dropReadOnly));
    }

    private static string Normalize(string sql) => string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
