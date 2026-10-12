using Channeled.Application.Abstractions.Time;

namespace Channeled.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
