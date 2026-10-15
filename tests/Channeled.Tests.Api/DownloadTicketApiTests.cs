using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Channeled.Application.Identity;
using Channeled.Domain.Errors;
using Channeled.Domain.Teams;
using FluentAssertions;
using static Channeled.Tests.Api.DownloadHelpers;

namespace Channeled.Tests.Api;

/// <summary>Session-bound download tickets: issue, use, start window vs resume, and privacy.</summary>
[Collection("Api")]
public class DownloadTicketApiTests(DownloadApiFactory factory) : TeamPrivacyTestBase(factory), IClassFixture<DownloadApiFactory>
{
    private readonly DownloadApiFactory _factory = factory;

    public override async Task InitializeAsync()
    {
        _factory.Clock.Reset();
        await base.InitializeAsync();
        await CreateChannelAsync("general", ChannelVisibility.Everyone);
    }

    [Fact]
    public async Task Ticket_download_streams_the_file_with_resumable_private_headers()
    {
        var bytes = Encoding.UTF8.GetBytes("ticketed download " + Guid.NewGuid());
        var file = await UploadChannelFileAsync(Owner.Client, Slug, "general", "résumé 📄.txt", bytes);
        var url = await TicketUrlAsync(Owner.Client, Channel(Slug, "general", file.Id));
        url.Should().StartWith("/api/v1/downloads/");

        var response = await GetAsync(Owner.Client, url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
        response.Headers.ETag!.Tag.Should().Be($"\"{file.Sha256}\"");
        response.Headers.AcceptRanges.Should().Contain("bytes");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("Referrer-Policy").Should().Contain("no-referrer");
        response.Content.Headers.LastModified.Should().NotBeNull();
        var disposition = response.Content.Headers.ContentDisposition!;
        disposition.DispositionType.Should().Be("attachment");
        disposition.FileNameStar.Should().Be("résumé 📄.txt");
    }

    [Fact]
    public async Task Batch_returns_a_url_or_an_error_per_item()
    {
        var member = await NewMemberAsync("Mia", MemberRole.Member);
        var channelFile = await UploadChannelFileAsync(Owner.Client, Slug, "general", "a.txt", "a"u8.ToArray());
        var personal = await UploadPersonalFileAsync(Owner.Client, "mine.txt", "mine"u8.ToArray());
        var othersPersonal = await UploadPersonalFileAsync(member.Client, "theirs.txt", "theirs"u8.ToArray());

        var tickets = await TicketsAsync(
            Owner.Client,
            Channel(Slug, "general", channelFile.Id),
            Channel(Slug, "general", Guid.NewGuid()),
            Personal(personal.Id),
            Personal(othersPersonal.Id),
            new("channel", channelFile.Id)); // channel scope without slug/channel

        tickets.Results.Select(r => r.Url is not null).Should().Equal(true, false, true, false, false);
        tickets.Results[1].Code.Should().Be(ErrorCodes.NotFound);
        // Someone else's personal file is indistinguishable from a missing one, even for a platform admin.
        tickets.Results[3].Code.Should().Be(ErrorCodes.NotFound);
        tickets.Results[4].Code.Should().Be(ErrorCodes.Validation);
    }

    [Fact]
    public async Task Issuing_tickets_requires_csrf()
    {
        var file = await UploadChannelFileAsync(Owner.Client, Slug, "general", "c.txt", "c"u8.ToArray());
        using var noCsrf = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
        (await noCsrf.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner@example.com", "OwnerPassword1!"))).EnsureSuccessStatusCode();

        var response = await noCsrf.PostAsJsonAsync(
            "/api/v1/downloads/tickets",
            new Channeled.Application.Files.DownloadTicketsRequest([Channel(Slug, "general", file.Id)]));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).Should().Be(ErrorCodes.CsrfRejected);
    }

    [Fact]
    public async Task Tickets_are_bound_to_the_user_and_the_session()
    {
        var member = await NewMemberAsync("Max", MemberRole.Member);
        var file = await UploadChannelFileAsync(Owner.Client, Slug, "general", "bound.txt", "bound"u8.ToArray());
        var url = await TicketUrlAsync(Owner.Client, Channel(Slug, "general", file.Id));

        // Another user who can read the same file still cannot use the owner's ticket.
        (await GetAsync(member.Client, url)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Same user, different session (second sign-in): not found either.
        using var secondSession = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
        (await secondSession.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner@example.com", "OwnerPassword1!"))).EnsureSuccessStatusCode();
        (await GetAsync(secondSession, url)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Without any session: 401.
        using var anonymous = Factory.CreateClient();
        (await GetAsync(anonymous, url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The issuing session still works.
        (await GetAsync(Owner.Client, url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revoked_or_signed_out_sessions_cannot_use_their_tickets()
    {
        var mia = await NewMemberAsync("Mira", MemberRole.Member);
        var file = await UploadChannelFileAsync(mia.Client, Slug, "general", "s.txt", "s"u8.ToArray());
        var url = await TicketUrlAsync(mia.Client, Channel(Slug, "general", file.Id));
        (await mia.Client.PostAsync("/api/v1/auth/logout", null)).EnsureSuccessStatusCode();
        (await GetAsync(mia.Client, url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Fresh_requests_must_start_within_the_window_but_resumes_may_come_later()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('r', 1000));
        var file = await UploadChannelFileAsync(Owner.Client, Slug, "general", "resume.bin", bytes);
        var url = await TicketUrlAsync(Owner.Client, Channel(Slug, "general", file.Id));
        var etag = $"\"{file.Sha256}\"";

        _factory.Clock.Advance(TimeSpan.FromSeconds(61));

        var fresh = await GetAsync(Owner.Client, url);
        fresh.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await ErrorCodeAsync(fresh)).Should().Be(ErrorCodes.DownloadTicketExpired);

        var resume = await GetAsync(Owner.Client, url, h =>
        {
            h.Range = new System.Net.Http.Headers.RangeHeaderValue(400, null);
            h.IfRange = new System.Net.Http.Headers.RangeConditionHeaderValue(new System.Net.Http.Headers.EntityTagHeaderValue(etag));
        });
        resume.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        (await resume.Content.ReadAsByteArrayAsync()).Should().Equal(bytes[400..]);

        // A bare Range (no validator) or a stale validator is not a resume: it would be a fresh full download.
        (await GetAsync(Owner.Client, url, h => h.Range = new System.Net.Http.Headers.RangeHeaderValue(400, null)))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
        (await GetAsync(Owner.Client, url, h =>
            {
                h.Range = new System.Net.Http.Headers.RangeHeaderValue(400, null);
                h.IfRange = new System.Net.Http.Headers.RangeConditionHeaderValue(new System.Net.Http.Headers.EntityTagHeaderValue("\"stale\""));
            }))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Liveness_and_access_are_rechecked_on_every_use()
    {
        var member = await NewMemberAsync("Rex", MemberRole.Member);
        await CreateChannelAsync("vault", ChannelVisibility.Restricted);
        await GrantAsync("vault", member, readOnly: true);
        var vaultFile = await UploadChannelFileAsync(Owner.Client, Slug, "vault", "vault.txt", "v"u8.ToArray());
        var generalFile = await UploadChannelFileAsync(member.Client, Slug, "general", "trash-me.txt", "t"u8.ToArray());

        var vaultUrl = await TicketUrlAsync(member.Client, Channel(Slug, "vault", vaultFile.Id));
        var trashUrl = await TicketUrlAsync(member.Client, Channel(Slug, "general", generalFile.Id));

        (await Owner.Client.DeleteAsync(TeamUrl($"/channels/vault/members/{member.Id}"))).EnsureSuccessStatusCode();
        (await GetAsync(member.Client, vaultUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await member.Client.DeleteAsync(TeamUrl($"/channels/general/items/{generalFile.Id}"))).EnsureSuccessStatusCode();
        (await GetAsync(member.Client, trashUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tampered_or_garbage_tickets_are_not_found()
    {
        var file = await UploadChannelFileAsync(Owner.Client, Slug, "general", "t.txt", "t"u8.ToArray());
        var url = await TicketUrlAsync(Owner.Client, Channel(Slug, "general", file.Id));
        var ticket = url["/api/v1/downloads/".Length..];
        var flipped = ticket[..^2] + (ticket[^2] == 'A' ? 'B' : 'A') + ticket[^1];

        (await GetAsync(Owner.Client, "/api/v1/downloads/" + flipped)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetAsync(Owner.Client, "/api/v1/downloads/not-a-ticket")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Outsiders_cannot_get_tickets_for_team_files()
    {
        var outsider = await NewUserAsync("Ola");
        var file = await UploadChannelFileAsync(Owner.Client, Slug, "general", "o.txt", "o"u8.ToArray());
        var tickets = await TicketsAsync(outsider.Client, Channel(Slug, "general", file.Id));
        tickets.Results.Single().Url.Should().BeNull();
        tickets.Results.Single().Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Issuing_more_than_the_concurrent_limit_returns_too_many_downloads_for_the_rest()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add((await UploadChannelFileAsync(Owner.Client, Slug, "general", $"many-{i}.txt", "m"u8.ToArray())).Id);
        }

        var tickets = await TicketsAsync(Owner.Client, ids.Select(id => Channel(Slug, "general", id)).ToArray());
        tickets.MaxConcurrentDownloads.Should().Be(3);
        tickets.Results.Count(r => r.Url is not null).Should().Be(3);
        tickets.Results.Skip(3).Should().OnlyContain(r => r.Code == ErrorCodes.TooManyDownloads);
    }

    [Fact]
    public async Task Request_size_is_validated()
    {
        var empty = await Owner.Client.PostAsJsonAsync("/api/v1/downloads/tickets", new { items = Array.Empty<object>() });
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var tooMany = await Owner.Client.PostAsJsonAsync(
            "/api/v1/downloads/tickets",
            new { items = Enumerable.Range(0, 51).Select(_ => new { scope = "personal", itemId = Guid.NewGuid() }) });
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooMany.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be(ErrorCodes.Validation);
    }
}
