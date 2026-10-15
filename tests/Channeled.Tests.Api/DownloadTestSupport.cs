using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Personal;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Channeled.Tests.Api;

/// <summary>Real time plus a test-controlled offset (download ticket start window).</summary>
public sealed class MutableClock : IClock
{
    private long _offsetTicks;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);

    public void Reset() => Interlocked.Exchange(ref _offsetTicks, 0);
}

/// <summary>Auth factory with a controllable clock and test download limits.</summary>
public class DownloadApiFactory : AuthApiFactory
{
    public MutableClock Clock { get; } = new();

    protected virtual IReadOnlyDictionary<string, string?> DownloadSettings { get; } = new Dictionary<string, string?>
    {
        ["Downloads:MaxConcurrentPerUser"] = "3",
        ["Downloads:MaxConcurrentGlobal"] = "64",
        ["Downloads:PerStreamBytesPerSecond"] = "0",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(DownloadSettings));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }
}

/// <summary>Small limits and a slow per-stream cap so tests can hold downloads open.</summary>
public sealed class DownloadLimitFactory : DownloadApiFactory
{
    protected override IReadOnlyDictionary<string, string?> DownloadSettings { get; } = new Dictionary<string, string?>
    {
        ["Downloads:MaxConcurrentPerUser"] = "2",
        ["Downloads:MaxConcurrentGlobal"] = "3",
        ["Downloads:PerStreamBytesPerSecond"] = "65536",
    };
}

internal static class DownloadHelpers
{
    public static async Task<FileItemDto> UploadChannelFileAsync(HttpClient client, string slug, string channel, string name, byte[] bytes)
    {
        var start = await client.PostAsJsonAsync("/api/v1/uploads/", new StartUploadRequest(slug, channel, name, bytes.Length, "text/plain", null));
        start.EnsureSuccessStatusCode();
        var body = (await start.Content.ReadFromJsonAsync<StartUploadResponse>())!;
        return await SendPartsAsync(client, body.UploadId, body.PartSizeBytes, bytes);
    }

    public static async Task<FileItemDto> UploadPersonalFileAsync(HttpClient client, string name, byte[] bytes)
    {
        var start = await client.PostAsJsonAsync("/api/v1/me/files/uploads", new StartPersonalUploadRequest(name, bytes.Length, "text/plain", null));
        start.EnsureSuccessStatusCode();
        var body = await start.Content.ReadFromJsonAsync<JsonElement>();
        return await SendPartsAsync(client, body.GetProperty("uploadId").GetGuid(), body.GetProperty("partSizeBytes").GetInt64(), bytes);
    }

    private static async Task<FileItemDto> SendPartsAsync(HttpClient client, Guid uploadId, long partSize, byte[] bytes)
    {
        var parts = (int)Math.Max(1, Math.Ceiling(bytes.Length / (double)partSize));
        for (var part = 1; part <= parts; part++)
        {
            var offset = (int)((part - 1) * partSize);
            var count = (int)Math.Min(partSize, bytes.Length - offset);
            using var content = new ByteArrayContent(bytes, offset, count);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            (await client.PutAsync($"/api/v1/uploads/{uploadId}/parts/{part}", content)).EnsureSuccessStatusCode();
        }

        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var complete = await client.PostAsJsonAsync($"/api/v1/uploads/{uploadId}/complete", new CompleteUploadRequest(sha, null));
        complete.EnsureSuccessStatusCode();
        return (await complete.Content.ReadFromJsonAsync<FileItemDto>())!;
    }

    public static DownloadTicketItem Channel(string slug, string channel, Guid id) => new("channel", id, slug, channel);

    public static DownloadTicketItem Personal(Guid id) => new("personal", id);

    public static async Task<DownloadTicketsResponse> TicketsAsync(HttpClient client, params DownloadTicketItem[] items)
    {
        var response = await client.PostAsJsonAsync("/api/v1/downloads/tickets", new DownloadTicketsRequest(items));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DownloadTicketsResponse>())!;
    }

    public static async Task<string> TicketUrlAsync(HttpClient client, DownloadTicketItem item)
    {
        var tickets = await TicketsAsync(client, item);
        tickets.Results.Should().ContainSingle();
        tickets.Results[0].Url.Should().NotBeNull(tickets.Results[0].Message);
        return tickets.Results[0].Url!;
    }

    public static async Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string url,
        Action<HttpRequestHeaders>? headers = null,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        headers?.Invoke(request.Headers);
        return await client.SendAsync(request, completion);
    }
}
