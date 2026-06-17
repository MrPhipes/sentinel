using Microsoft.Extensions.Options;
using Phipes.Sentinel.Models;
using Phipes.Sentinel.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuración tipada desde la sección "Sentinel" de appsettings.json.
builder.Services.Configure<SentinelOptions>(builder.Configuration.GetSection("Sentinel"));

builder.Services.AddSingleton<DeviceRepository>();
builder.Services.AddSingleton<DeviceStatusStore>();
builder.Services.AddSingleton<WakeOnLanService>();
builder.Services.AddHostedService<MonitorService>();

var app = builder.Build();

// Dashboard estático servido desde wwwroot (index.html en la raíz).
app.UseDefaultFiles();
app.UseStaticFiles();

// --- API REST ---

// Estado de todos los equipos monitoreados.
app.MapGet("/api/devices", (DeviceStatusStore store) => Results.Ok(store.GetAll()));

// Estado de un equipo puntual.
app.MapGet("/api/devices/{id}", (string id, DeviceStatusStore store) =>
{
    var status = store.Get(id);
    return status is not null ? Results.Ok(status) : Results.NotFound();
});

// Dispara el Wake-on-LAN hacia el equipo indicado.
app.MapPost("/api/devices/{id}/wake", async (
    string id,
    IOptions<SentinelOptions> options,
    DeviceRepository repository,
    WakeOnLanService wol,
    ILogger<Program> logger) =>
{
    var device = repository.Get(id);
    if (device is null)
        return Results.NotFound(new { error = $"Equipo '{id}' no encontrado" });

    await wol.SendMagicPacketAsync(
        device.MacAddress,
        options.Value.WolBroadcastAddress,
        options.Value.WolPort);

    logger.LogInformation("Wake-on-LAN enviado a {Name} ({Mac})", device.Name, device.MacAddress);
    return Results.Ok(new { ok = true, message = $"Paquete mágico enviado a {device.Name}" });
});

// --- CRUD de configuración de equipos (edición en caliente) ---

// Lista la configuración editable de los equipos.
app.MapGet("/api/config/devices", (DeviceRepository repository) =>
    Results.Ok(repository.GetAll()));

// Agrega un equipo nuevo.
app.MapPost("/api/config/devices", (
    DeviceConfig device,
    DeviceRepository repository,
    DeviceStatusStore store) =>
{
    var (ok, error) = ValidateDevice(device);
    if (!ok) return Results.BadRequest(new { error });

    var created = repository.Add(device);
    store.Update(Placeholder(created)); // aparece de inmediato en el dashboard
    return Results.Created($"/api/config/devices/{created.Id}", created);
});

// Edita un equipo existente.
app.MapPut("/api/config/devices/{id}", (
    string id,
    DeviceConfig device,
    DeviceRepository repository,
    DeviceStatusStore store) =>
{
    var (ok, error) = ValidateDevice(device);
    if (!ok) return Results.BadRequest(new { error });

    if (!repository.Update(id, device))
        return Results.NotFound(new { error = $"Equipo '{id}' no encontrado" });

    var updated = repository.Get(id)!;
    store.Update(Placeholder(updated)); // refleja nombre/host nuevos al toque
    return Results.Ok(updated);
});

// Elimina un equipo.
app.MapDelete("/api/config/devices/{id}", (
    string id,
    DeviceRepository repository,
    DeviceStatusStore store) =>
{
    if (!repository.Delete(id))
        return Results.NotFound(new { error = $"Equipo '{id}' no encontrado" });

    store.Remove(id);
    return Results.NoContent();
});

// Salud del servicio (para healthchecks de Docker / reverse proxy).
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Valida los datos mínimos de un equipo antes de persistirlo.
static (bool Ok, string? Error) ValidateDevice(DeviceConfig device)
{
    if (string.IsNullOrWhiteSpace(device.Name))
        return (false, "El nombre es obligatorio");
    if (string.IsNullOrWhiteSpace(device.Host))
        return (false, "El host (IP o hostname) es obligatorio");
    if (!WakeOnLanService.TryParseMac(device.MacAddress, out _))
        return (false, "La MAC no es válida (formato AA:BB:CC:DD:EE:FF)");

    device.Ports ??= new();
    return (true, null);
}

// Estado inicial "apagado / sin chequear" para que un equipo recién creado o
// editado aparezca de inmediato en el dashboard, antes del próximo sondeo.
static DeviceStatus Placeholder(DeviceConfig device) => new()
{
    Id = device.Id,
    Name = device.Name,
    Host = device.Host,
    MacAddress = device.MacAddress,
    Online = false,
    PingOnline = false,
    LatencyMs = null,
    Ports = device.Ports.Select(p => new PortStatus(p.Name, p.Number, false)).ToList(),
    LastChecked = DateTimeOffset.Now
};
