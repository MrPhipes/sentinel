using Microsoft.Extensions.Options;
using Phipes.Sentinel.Models;
using Phipes.Sentinel.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuración tipada desde la sección "Sentinel" de appsettings.json.
builder.Services.Configure<SentinelOptions>(builder.Configuration.GetSection("Sentinel"));

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
    WakeOnLanService wol,
    ILogger<Program> logger) =>
{
    var device = options.Value.Devices
        .FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    if (device is null)
        return Results.NotFound(new { error = $"Equipo '{id}' no encontrado" });

    await wol.SendMagicPacketAsync(
        device.MacAddress,
        options.Value.WolBroadcastAddress,
        options.Value.WolPort);

    logger.LogInformation("Wake-on-LAN enviado a {Name} ({Mac})", device.Name, device.MacAddress);
    return Results.Ok(new { ok = true, message = $"Paquete mágico enviado a {device.Name}" });
});

// Salud del servicio (para healthchecks de Docker / reverse proxy).
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();
