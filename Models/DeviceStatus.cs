namespace Phipes.Sentinel.Models;

// Estado en vivo de un equipo, calculado en cada sondeo y servido por la API.
public sealed record DeviceStatus
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Host { get; init; } = "";
    public string MacAddress { get; init; } = "";

    // "Encendido": responde ping ICMP o tiene al menos un puerto TCP abierto.
    public bool Online { get; init; }

    // Resultado puro del ping ICMP (separado de Online para diagnóstico).
    public bool PingOnline { get; init; }

    // Latencia del ping en milisegundos; null si no respondió.
    public long? LatencyMs { get; init; }

    // Estado de cada puerto TCP configurado.
    public IReadOnlyList<PortStatus> Ports { get; init; } = Array.Empty<PortStatus>();

    // Momento del último sondeo.
    public DateTimeOffset LastChecked { get; init; }
}

// Resultado del chequeo de un puerto TCP puntual.
public sealed record PortStatus(string Name, int Number, bool Open);
