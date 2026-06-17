using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Phipes.Sentinel.Models;

namespace Phipes.Sentinel.Services;

// Servicio en segundo plano que sondea periódicamente a cada equipo:
// ping ICMP + chequeo de puertos TCP. Guarda el resultado en el DeviceStatusStore.
public sealed class MonitorService : BackgroundService
{
    private readonly SentinelOptions _options;
    private readonly DeviceStatusStore _store;
    private readonly ILogger<MonitorService> _logger;

    public MonitorService(
        IOptions<SentinelOptions> options,
        DeviceStatusStore store,
        ILogger<MonitorService> logger)
    {
        _options = options.Value;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Centinela iniciado: {Count} equipo(s), sondeo cada {Seconds}s",
            _options.Devices.Count, _options.PollIntervalSeconds);

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        // Sondeo inmediato al arrancar y luego en cada tick del temporizador.
        do
        {
            try
            {
                await PollAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante el sondeo");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAllAsync(CancellationToken ct)
    {
        var tasks = _options.Devices.Select(device => PollDeviceAsync(device, ct));
        var results = await Task.WhenAll(tasks);
        foreach (var status in results)
            _store.Update(status);
    }

    private async Task<DeviceStatus> PollDeviceAsync(DeviceConfig device, CancellationToken ct)
    {
        var (pingOnline, latency) = await PingAsync(device.Host);

        var portTasks = device.Ports.Select(async port =>
            new PortStatus(port.Name, port.Number, await CheckTcpAsync(device.Host, port.Number, ct)));
        var ports = await Task.WhenAll(portTasks);

        // Se considera "encendido" si responde ping O si algún puerto está abierto.
        // Así mantenemos estado útil aunque el ICMP esté bloqueado por firewall.
        var online = pingOnline || ports.Any(p => p.Open);

        return new DeviceStatus
        {
            Id = device.Id,
            Name = device.Name,
            Host = device.Host,
            MacAddress = device.MacAddress,
            Online = online,
            PingOnline = pingOnline,
            LatencyMs = latency,
            Ports = ports,
            LastChecked = DateTimeOffset.Now
        };
    }

    private async Task<(bool Online, long? Latency)> PingAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, _options.PingTimeoutMs);
            return reply.Status == IPStatus.Success
                ? (true, reply.RoundtripTime)
                : (false, null);
        }
        catch (Exception)
        {
            // El ping ICMP puede fallar por permisos del contenedor; no es fatal,
            // el chequeo TCP igual nos dice si el equipo está arriba.
            return (false, null);
        }
    }

    private async Task<bool> CheckTcpAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_options.TcpTimeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
