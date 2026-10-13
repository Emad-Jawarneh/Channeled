using System.Text.Json;
using System.Text.Json.Serialization;
using Channeled.Domain.Identity;
using Channeled.Infrastructure.Persistence;

namespace Channeled.Infrastructure.Jobs;

public static class ObjectDeleteJobTypes
{
    public const string Type = "object_delete";
}

public sealed record ObjectDeleteMultipart(string ObjectKey, string MultipartId);

public sealed record ObjectDeletePayload(
    IReadOnlyList<string> Keys,
    IReadOnlyList<ObjectDeleteMultipart> Multiparts);

public static class ObjectDeleteOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Enqueue(
        ApplicationDbContext db,
        DateTimeOffset now,
        IEnumerable<string> keys,
        IEnumerable<ObjectDeleteMultipart>? multiparts,
        int chunkSize)
    {
        var keySet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                keySet.Add(key);
            }
        }

        var multipartList = (multiparts ?? Array.Empty<ObjectDeleteMultipart>())
            .Where(m => !string.IsNullOrWhiteSpace(m.ObjectKey) && !string.IsNullOrWhiteSpace(m.MultipartId))
            .GroupBy(m => m.ObjectKey, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        foreach (var m in multipartList)
        {
            keySet.Add(m.ObjectKey);
        }

        var allKeys = keySet.OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (allKeys.Count == 0 && multipartList.Count == 0)
        {
            return;
        }

        var size = Math.Clamp(chunkSize, 1, 2_000);
        for (var i = 0; i < Math.Max(allKeys.Count, 1); i += size)
        {
            var chunkKeys = allKeys.Skip(i).Take(size).ToList();
            var chunkMultiparts = i == 0
                ? multipartList
                : multipartList.Where(m => chunkKeys.Contains(m.ObjectKey, StringComparer.Ordinal)).ToList();

            if (chunkKeys.Count == 0 && chunkMultiparts.Count == 0)
            {
                continue;
            }

            var payload = new ObjectDeletePayload(chunkKeys, chunkMultiparts);
            db.Jobs.Add(new JobEntity
            {
                Id = UuidV7.NewGuid(),
                Type = ObjectDeleteJobTypes.Type,
                State = "pending",
                DetailsJson = JsonSerializer.Serialize(payload, JsonOptions),
                CreatedAt = now,
                Attempts = 0,
                AvailableAt = now,
            });
        }
    }

    public static ObjectDeletePayload? Parse(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ObjectDeletePayload>(detailsJson, JsonOptions);
    }
}
