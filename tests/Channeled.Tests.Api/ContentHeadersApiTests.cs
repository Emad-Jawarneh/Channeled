using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Channeled.Domain.Teams;
using FluentAssertions;
using static Channeled.Tests.Api.DownloadHelpers;

namespace Channeled.Tests.Api;

/// <summary>Validators, ranges and filenames on the channel/personal content endpoints (shared ContentResponder).</summary>
[Collection("Api")]
public class ContentHeadersApiTests(DownloadApiFactory factory) : TeamPrivacyTestBase(factory), IClassFixture<DownloadApiFactory>
{
    private static readonly byte[] Bytes = Encoding.ASCII.GetBytes("0123456789abcdefghij");

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateChannelAsync("general", ChannelVisibility.Everyone);
    }

    private async Task<(string Url, string ETag)> ChannelFileAsync(string name = "data.txt")
    {
        var file = await UploadChannelFileAsync(Owner.Client, Slug, "general", name, Bytes);
        return (TeamUrl($"/channels/general/items/{file.Id}/content"), $"\"{file.Sha256}\"");
    }

    [Fact]
    public async Task Inline_content_has_validators_and_revalidates_with_304()
    {
        var (url, etag) = await ChannelFileAsync();
        var first = await GetAsync(Owner.Client, url);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.ETag!.Tag.Should().Be(etag);
        first.Headers.AcceptRanges.Should().Contain("bytes");
        first.Content.Headers.LastModified.Should().NotBeNull();
        first.Headers.CacheControl!.NoCache.Should().BeTrue();

        var again = await GetAsync(Owner.Client, url, h => h.IfNoneMatch.Add(new EntityTagHeaderValue(etag)));
        again.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await again.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("bytes=0-4", "01234", "bytes 0-4/20")]
    [InlineData("bytes=15-", "fghij", "bytes 15-19/20")]
    [InlineData("bytes=-3", "hij", "bytes 17-19/20")]
    [InlineData("bytes=18-99", "ij", "bytes 18-19/20")]
    public async Task Single_ranges_return_206_with_content_range(string range, string expected, string contentRange)
    {
        var (url, _) = await ChannelFileAsync();
        var response = await GetAsync(Owner.Client, url, h => h.TryAddWithoutValidation("Range", range));
        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        Encoding.ASCII.GetString(await response.Content.ReadAsByteArrayAsync()).Should().Be(expected);
        response.Content.Headers.ContentRange!.ToString().Should().Be(contentRange);
    }

    [Fact]
    public async Task If_range_match_serves_the_range_and_a_mismatch_serves_the_whole_file()
    {
        var (url, etag) = await ChannelFileAsync();
        var match = await GetAsync(Owner.Client, url, h =>
        {
            h.Range = new RangeHeaderValue(10, 12);
            h.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(etag));
        });
        match.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        Encoding.ASCII.GetString(await match.Content.ReadAsByteArrayAsync()).Should().Be("abc");

        var mismatch = await GetAsync(Owner.Client, url, h =>
        {
            h.Range = new RangeHeaderValue(10, 12);
            h.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"other\""));
        });
        mismatch.StatusCode.Should().Be(HttpStatusCode.OK);
        (await mismatch.Content.ReadAsByteArrayAsync()).Should().Equal(Bytes);
    }

    [Fact]
    public async Task Unsatisfiable_ranges_return_416_with_the_size()
    {
        var (url, _) = await ChannelFileAsync();
        var response = await GetAsync(Owner.Client, url, h => h.TryAddWithoutValidation("Range", "bytes=50-60"));
        response.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable);
        response.Content.Headers.ContentRange!.ToString().Should().Be("bytes */20");
    }

    [Fact]
    public async Task Multi_range_and_malformed_ranges_get_the_whole_file()
    {
        var (url, _) = await ChannelFileAsync();
        foreach (var range in new[] { "bytes=0-1,5-6", "items=0-1", "bytes=garbage" })
        {
            var response = await GetAsync(Owner.Client, url, h => h.TryAddWithoutValidation("Range", range));
            response.StatusCode.Should().Be(HttpStatusCode.OK, range);
            (await response.Content.ReadAsByteArrayAsync()).Should().Equal(Bytes, range);
        }
    }

    [Fact]
    public async Task Download_flag_sets_attachment_with_an_rfc6266_unicode_filename()
    {
        var (url, _) = await ChannelFileAsync("naïve 文件.txt");
        var response = await GetAsync(Owner.Client, url + "?download=true");
        var disposition = response.Content.Headers.ContentDisposition!;
        disposition.DispositionType.Should().Be("attachment");
        disposition.FileNameStar.Should().Be("naïve 文件.txt");
        response.Content.Headers.GetValues("Content-Disposition").Single().Should().Contain("filename*=UTF-8''");
    }

    [Fact]
    public async Task Personal_content_uses_the_same_validators_and_ranges()
    {
        var file = await UploadPersonalFileAsync(Owner.Client, "personal.txt", Bytes);
        var url = $"/api/v1/me/files/items/{file.Id}/content";
        var range = await GetAsync(Owner.Client, url, h => h.TryAddWithoutValidation("Range", "bytes=-2"));
        range.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        Encoding.ASCII.GetString(await range.Content.ReadAsByteArrayAsync()).Should().Be("ij");
        range.Headers.ETag!.Tag.Should().Be($"\"{file.Sha256}\"");

        var stranger = await NewUserAsync("Stan");
        (await GetAsync(stranger.Client, url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
