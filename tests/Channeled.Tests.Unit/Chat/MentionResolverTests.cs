using Channeled.Infrastructure.Chat;
using FluentAssertions;

namespace Channeled.Tests.Unit.Chat;

public class MentionResolverTests
{
    private static readonly (Guid Id, string DisplayName)[] Members =
    [
        (Guid.Parse("11111111-1111-1111-1111-111111111111"), "Ada"),
        (Guid.Parse("22222222-2222-2222-2222-222222222222"), "Ada Lovelace"),
        (Guid.Parse("33333333-3333-3333-3333-333333333333"), "Bob"),
    ];

    [Fact]
    public void Parses_longest_display_name_match()
    {
        var ids = MentionResolver.Resolve("hey @Ada Lovelace and @Bob", Members, null);
        ids.Should().BeEquivalentTo([Members[1].Id, Members[2].Id]);
    }

    [Fact]
    public void Explicit_ids_must_be_team_members()
    {
        var stranger = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var ids = MentionResolver.Resolve("hi", Members, [Members[0].Id, stranger]);
        ids.Should().BeEquivalentTo([Members[0].Id]);
    }

    [Fact]
    public void Ignores_partial_name_prefix()
    {
        var ids = MentionResolver.Resolve("mail adam@example.com", Members, null);
        ids.Should().BeEmpty();
    }
}
