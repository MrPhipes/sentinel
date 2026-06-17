using System.Text.Json;
using Microsoft.Extensions.Options;
using Phipes.Sentinel.Models;

namespace Phipes.Sentinel.Services;

// Repositorio de equipos persistido en un archivo JSON (idealmente en un volumen
// Docker). Permite editar la lista de equipos en caliente desde la UI / API sin
// reiniciar el contenedor. La sección Devices de appsettings.json solo siembra
// el primer arranque; luego el archivo de DataPath es la fuente de verdad.
public sealed class DeviceRepository
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly ILogger<DeviceRepository> _logger;
    private List<DeviceConfig> _devices = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public DeviceRepository(
        IOptions<SentinelOptions> options,
        IHostEnvironment env,
        ILogger<DeviceRepository> logger)
    {
        _logger = logger;

        var configured = options.Value.DataPath;
        _path = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured);

        Load(options.Value.Devices);
    }

    // Carga desde disco; si el archivo no existe, siembra con los equipos de appsettings.
    private void Load(List<DeviceConfig> seed)
    {
        lock (_lock)
        {
            if (File.Exists(_path))
            {
                try
                {
                    var json = File.ReadAllText(_path);
                    _devices = JsonSerializer.Deserialize<List<DeviceConfig>>(json, JsonOpts) ?? new();
                    _logger.LogInformation("Equipos cargados desde {Path}: {Count}", _path, _devices.Count);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "No se pudo leer {Path}; se usará la semilla de appsettings", _path);
                }
            }

            _devices = seed.Select(Clone).ToList();
            Persist();
            _logger.LogInformation("Sembrados {Count} equipo(s) en {Path}", _devices.Count, _path);
        }
    }

    public IReadOnlyList<DeviceConfig> GetAll()
    {
        lock (_lock) return _devices.Select(Clone).ToList();
    }

    public DeviceConfig? Get(string id)
    {
        lock (_lock)
        {
            var device = _devices.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            return device is null ? null : Clone(device);
        }
    }

    // Agrega un equipo nuevo, asignándole un id estable y único.
    public DeviceConfig Add(DeviceConfig device)
    {
        lock (_lock)
        {
            device.Id = MakeUniqueId(device);
            _devices.Add(Clone(device));
            Persist();
            return Clone(device);
        }
    }

    // Actualiza un equipo existente (el id no cambia).
    public bool Update(string id, DeviceConfig updated)
    {
        lock (_lock)
        {
            var index = _devices.FindIndex(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;

            updated.Id = _devices[index].Id;
            _devices[index] = Clone(updated);
            Persist();
            return true;
        }
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            var removed = _devices.RemoveAll(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Persist();
            return removed;
        }
    }

    private void Persist()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(_devices, JsonOpts));
    }

    // Genera un id estable a partir del nombre (slug), garantizando unicidad.
    private string MakeUniqueId(DeviceConfig device)
    {
        var baseId = !string.IsNullOrWhiteSpace(device.Id) ? device.Id : Slugify(device.Name);
        if (string.IsNullOrWhiteSpace(baseId)) baseId = "equipo";

        var id = baseId;
        var n = 2;
        while (_devices.Any(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            id = $"{baseId}-{n++}";
        return id;
    }

    private static string Slugify(string text)
    {
        var chars = text.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-');
        var slug = new string(chars.ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    private static DeviceConfig Clone(DeviceConfig d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Host = d.Host,
        MacAddress = d.MacAddress,
        Ports = (d.Ports ?? new()).Select(p => new PortConfig { Name = p.Name, Number = p.Number }).ToList()
    };
}
