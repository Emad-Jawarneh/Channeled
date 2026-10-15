using System.Net;
using Channeled.Application.Files;
using Channeled.Domain.Errors;
using Channeled.Domain.Teams;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using static Channeled.Tests.Api.DownloadHelpers;

namespace Channeled.Tests.Api;

/// <summary>Concurrent download slots (2 per user, 3 global) with a slow 64 KiB/s stream so downloads stay open.</summary>
[Collection("Api")]
public class DownloadEgressLimitTests(DownloadLimitFactory factory) : TeamPrivacyTestBase(factory), IClassFixture<DownloadLimitFactory>
{
    private static readonly byte[] Big = new byte[2 * 1024 * 1024];

    private IDownloadSlots Slots => Factory.Services.GetRequiredService<IDownloadSlots>();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateChannelAsync("general", ChannelVisibility.Everyone);
    }

    private async Task<HttpResponseMessage> OpenDownloadAsync(HttpClient client, Guid fileId)
    {
        var url = await TicketUrlAsync(client, Channel(Slug, "general", fileId));
        return await GetAsync(client, url, completion: HttpCompletionOption.ResponseHeadersRead);
    }

    private async Task WaitForSlotsAsync(Guid userId, int expected)
    {
        for (var i = 0; i < 100 && Slots.ActiveFor(userId) != expected; i++)
        {
            await Task.Delay(50);
        }

        Slots.ActiveFor(userId).Should().Be(expected);
    }

    [Fact]
    public async Task Per_user_limit_returns_429_and_an_aborted_download_frees_its_slot()
    {
        var file = await UploadChannelFileAsync(Owner.Client, Slug, "general", "big.bin", Big);
        var small = await UploadChannelFileAsync(Owner.Client, Slug, "general", "small.png", "p"u8.ToArray());

        using var first = await OpenDownloadAsync(Owner.Client, file.Id);
        using var second = await OpenDownloadAsync(Owner.Client, file.Id);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        await WaitForSlotsAsync(Owner.Id, 2);

        // Issuing more tickets now reports the limit per item...
        var tickets = await TicketsAsync(Owner.Client, Channel(Slug, "general", file.Id));
        tickets.Results.Single().Code.Should().Be(ErrorCodes.TooManyDownloads);
        // ...and an explicit ?download=true request is refused at stream start.
        var third = await GetAsync(Owner.Client, TeamUrl($"/channels/general/items/{file.Id}/content?download=true"));
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await ErrorCodeAsync(third)).Should().Be(ErrorCodes.TooManyDownloads);

        // Inline previews are not counted.
        (await GetAsync(Owner.Client, TeamUrl($"/channels/general/items/{small.Id}/content"))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Abandoning a download (client goes away) releases its slot.
        first.Dispose();
        await WaitForSlotsAsync(Owner.Id, 1);
        using var replacement = await OpenDownloadAsync(Owner.Client, file.Id);
        replacement.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Global_limit_applies_across_users()
    {
        var member = await NewMemberAsync("Gia", MemberRole.Member);
        var file = await UploadChannelFileAsync(Owner.Client, Slug, "general", "shared.bin", Big);

        using var a = await OpenDownloadAsync(Owner.Client, file.Id);
        using var b = await OpenDownloadAsync(Owner.Client, file.Id);
        using var c = await OpenDownloadAsync(member.Client, file.Id);
        Slots.ActiveGlobal.Should().Be(3);

        var tickets = await TicketsAsync(member.Client, Channel(Slug, "general", file.Id));
        tickets.Results.Single().Code.Should().Be(ErrorCodes.TooManyDownloads);
    }
}
