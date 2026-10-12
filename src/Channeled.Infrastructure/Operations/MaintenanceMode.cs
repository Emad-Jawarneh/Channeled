using Channeled.Application.Abstractions.Operations;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Operations;

public sealed class MaintenanceMode(IOptionsMonitor<MaintenanceOptions> options) : IMaintenanceMode
{
    private int _override = -1; // -1 = use config, 0 = off, 1 = on

    public bool IsEnabled
    {
        get
        {
            var o = Volatile.Read(ref _override);
            return o >= 0 ? o == 1 : options.CurrentValue.Enabled;
        }
    }

    public void SetEnabled(bool enabled) =>
        Interlocked.Exchange(ref _override, enabled ? 1 : 0);
}
