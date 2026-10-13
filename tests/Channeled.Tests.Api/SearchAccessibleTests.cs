using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Channeled.Application.Search;
using Channeled.Application.Teams;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Teams;
using FluentAssertions;

namespace Channeled.Tests.Api;

/// <summary>Scope=accessible must only ever return what the caller can see right now, also with many channels/files.</summary>
[Collection("Api")]
public class SearchAccessibleTests(AuthApiFactory factory) : TeamPrivacyTestBase(factory), IClassFixture<AuthApiFactory>
{
    private const int OpenChannels = 120;
    private const int SecretChannels = 70; // Restricted, no grant for the regular users
    private const int GrantedChannels = 15; // Restricted, granted to Alice (first 5 also to the Viewer)
    private const int ForeignChannels = 60; // another team the users do not belong to

    [Fact]
    public async Task Accessible_search_never_returns_ungranted_restricted_or_foreign_team_files_at_scale()
    {
        var alice = await NewMemberAsync("Alice", MemberRole.Member);
        var viewer = await NewMemberAsync("Vince", MemberRole.Viewer);
        var admin = await NewMemberAsync("Adam", MemberRole.Admin);

        var foreignResp = await Owner.Client.PostAsJsonAsync("/api/v1/teams/", new CreateTeamRequest("Foreign", null));
        foreignResp.EnsureSuccessStatusCode();
        var foreignTeam = (await foreignResp.Content.ReadFromJsonAsync<TeamDto>())!;

        var open = new HashSet<string>();
        var secret = new HashSet<string>();
        var granted = new HashSet<string>();
        var viewerGranted = new HashSet<string>();
        var now = DateTimeOffset.UtcNow;
        var seq = 0;

        await DbAsync(async db =>
        {
            void AddChannel(Guid teamId, string label, int i, ChannelVisibility visibility, HashSet<string>? bucket, Action<Guid>? grant = null)
            {
                var channel = new ChannelEntity
                {
                    Id = UuidV7.NewGuid(),
                    TeamId = teamId,
                    Name = $"{label}-{i}",
                    Kind = ChannelKind.File,
                    Visibility = visibility,
                    CreatedAt = now,
                };
                db.Channels.Add(channel);
                var fileName = $"needle-{label}-{i}.txt";
                bucket?.Add(fileName);
                db.Files.Add(new FileEntity
                {
                    Id = UuidV7.NewGuid(),
                    ChannelId = channel.Id,
                    Name = fileName,
                    ContentType = "text/plain",
                    SizeBytes = 1,
                    Sha256 = new string('a', 64),
                    StorageKey = "seed/" + Guid.NewGuid(),
                    UploadedBy = Owner.Id,
                    CreatedAt = now,
                    UpdatedAt = now.AddMilliseconds(-seq++),
                });
                grant?.Invoke(channel.Id);
            }

            for (var i = 0; i < OpenChannels; i++)
            {
                AddChannel(TeamId, "open", i, ChannelVisibility.Everyone, open);
            }

            for (var i = 0; i < SecretChannels; i++)
            {
                AddChannel(TeamId, "secret", i, ChannelVisibility.Restricted, secret);
            }

            for (var i = 0; i < GrantedChannels; i++)
            {
                var index = i;
                AddChannel(TeamId, "granted", i, ChannelVisibility.Restricted, granted, channelId =>
                {
                    db.ChannelMembers.Add(new ChannelMemberEntity { ChannelId = channelId, UserId = alice.Id, ReadOnly = index % 2 == 0 });
                    if (index < 5)
                    {
                        db.ChannelMembers.Add(new ChannelMemberEntity { ChannelId = channelId, UserId = viewer.Id, ReadOnly = true });
                        viewerGranted.Add($"needle-granted-{index}.txt");
                    }
                });
            }

            for (var i = 0; i < ForeignChannels; i++)
            {
                AddChannel(foreignTeam.Id, "foreign", i, i % 2 == 0 ? ChannelVisibility.Everyone : ChannelVisibility.Restricted, null);
            }

            await db.SaveChangesAsync();
        });

        var timer = Stopwatch.StartNew();

        var aliceHits = await SearchAllAsync(alice, "needle", pageSize: 50);
        AssertNames(aliceHits, open.Concat(granted));
        aliceHits.Should().OnlyContain(h => h.TeamSlug == Slug);

        var viewerHits = await SearchAllAsync(viewer, "needle", pageSize: 50);
        AssertNames(viewerHits, open.Concat(viewerGranted));

        var adminHits = await SearchAllAsync(admin, "needle", pageSize: 200);
        AssertNames(adminHits, open.Concat(secret).Concat(granted)); // 205 results → paged; never the foreign team

        var teamScope = await alice.Client.GetFromJsonAsync<SearchResponse>(
            $"/api/v1/search?q=needle&scope=team&teamSlug={Slug}&limit=200");
        teamScope!.Hits.Select(h => h.Name).Should().NotContain(n => secret.Contains(n) || n.Contains("foreign"));

        timer.Stop();
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "scale smoke: ~265 channels / files stay interactive");

        // Direct channel-scoped searches into hidden places answer 404 (no existence leak).
        (await alice.Client.GetAsync($"/api/v1/search?q=needle&scope=channel&teamSlug={Slug}&channelName=secret-0"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await alice.Client.GetAsync($"/api/v1/search?q=needle&scope=team&teamSlug={foreignTeam.Slug}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Granting one more channel / revoking one is reflected on the very next query.
        await GrantAsync("secret-0", alice, readOnly: false);
        (await SearchAllAsync(alice, "needle-secret-0", pageSize: 50)).Select(h => h.Name).Should().Equal("needle-secret-0.txt");
        (await Owner.Client.DeleteAsync(TeamUrl($"/channels/secret-0/members/{alice.Id}"))).EnsureSuccessStatusCode();
        (await SearchAllAsync(alice, "needle-secret-0", pageSize: 50)).Should().BeEmpty();
    }

    private static void AssertNames(IReadOnlyList<SearchHitDto> hits, IEnumerable<string> expected)
    {
        var names = hits.Select(h => h.Name).ToList();
        names.Should().OnlyHaveUniqueItems("keyset paging must not repeat rows");
        names.Should().BeEquivalentTo(expected);
        names.Should().NotContain(n => n.Contains("foreign"));
    }

    private static async Task<List<SearchHitDto>> SearchAllAsync(Person person, string q, int pageSize)
    {
        var all = new List<SearchHitDto>();
        string? cursor = null;
        for (var page = 0; page < 20; page++)
        {
            var url = $"/api/v1/search?q={Uri.EscapeDataString(q)}&scope=accessible&limit={pageSize}"
                      + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var resp = await person.Client.GetFromJsonAsync<SearchResponse>(url);
            all.AddRange(resp!.Hits);
            cursor = resp.NextCursor;
            if (cursor is null)
            {
                return all;
            }
        }

        throw new InvalidOperationException("Search paging did not terminate.");
    }
}
