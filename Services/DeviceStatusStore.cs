using System.Collections.Concurrent;
using Phipes.Sentinel.Models;

namespace Phipes.Sentinel.Services;

// Almacén en memoria, seguro para concurrencia, con el último estado de cada equipo.
// El MonitorService escribe y la API lee.
public sealed class DeviceStatusStore
{
    private readonly ConcurrentDictionary<string, DeviceStatus> _statuses =
        new(StringComparer.OrdinalIgnoreCase);

    public void Update(DeviceStatus status) => _statuses[status.Id] = status;

    public IReadOnlyCollection<DeviceStatus> GetAll() =>
        _statuses.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public DeviceStatus? Get(string id) =>
        _statuses.TryGetValue(id, out var status) ? status : null;
}
