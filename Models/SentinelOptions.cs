namespace Phipes.Sentinel.Models;

// Configuración tipada que se enlaza desde la sección "Sentinel" de appsettings.json.
public sealed class SentinelOptions
{
    // Cada cuántos segundos se sondea a todos los equipos.
    public int PollIntervalSeconds { get; set; } = 30;

    // Timeout del ping ICMP por equipo (milisegundos).
    public int PingTimeoutMs { get; set; } = 1500;

    // Timeout del intento de conexión TCP por puerto (milisegundos).
    public int TcpTimeoutMs { get; set; } = 1500;

    // Dirección de broadcast a la que se envía el paquete mágico Wake-on-LAN.
    // 255.255.255.255 sirve cuando el contenedor corre en network_mode: host.
    // Si usa broadcast dirigido de subred, ponga p.ej. 192.168.1.255.
    public string WolBroadcastAddress { get; set; } = "255.255.255.255";

    // Puerto UDP del paquete mágico (convencionalmente 9, a veces 7).
    public int WolPort { get; set; } = 9;

    // Lista de equipos a monitorear.
    public List<DeviceConfig> Devices { get; set; } = new();
}

// Definición de un equipo monitoreado.
public sealed class DeviceConfig
{
    // Identificador estable usado en la API (/api/devices/{id}/wake). Ej: "nas".
    public string Id { get; set; } = "";

    // Nombre legible para el dashboard. Ej: "NAS de casa".
    public string Name { get; set; } = "";

    // IP o hostname para el ping y el chequeo de puertos. Ej: "192.168.1.10".
    public string Host { get; set; } = "";

    // MAC del equipo, destino del Wake-on-LAN. Acepta AA:BB:CC:DD:EE:FF o AA-BB-...
    public string MacAddress { get; set; } = "";

    // Puertos TCP a verificar para distinguir "encendido" de "servicio arriba".
    public List<PortConfig> Ports { get; set; } = new();
}

// Un puerto TCP a chequear, con etiqueta para el dashboard.
public sealed class PortConfig
{
    public string Name { get; set; } = "";
    public int Number { get; set; }
}
