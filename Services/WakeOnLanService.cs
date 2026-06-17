using System.Net;
using System.Net.Sockets;

namespace Phipes.Sentinel.Services;

// Construye y envía el "paquete mágico" (magic packet) de Wake-on-LAN por UDP broadcast.
public sealed class WakeOnLanService
{
    // Envía el paquete mágico a la MAC indicada.
    // Importante: el broadcast solo llega a la LAN si el contenedor corre en
    // network_mode: host (o si se usa el broadcast dirigido de la subred).
    public async Task SendMagicPacketAsync(string macAddress, string broadcastAddress, int port)
    {
        if (!TryParseMac(macAddress, out var mac))
            throw new FormatException($"MAC inválida: '{macAddress}'");

        // El paquete mágico son 6 bytes 0xFF seguidos de 16 repeticiones de la MAC.
        var packet = new byte[6 + 16 * 6];
        for (var i = 0; i < 6; i++)
            packet[i] = 0xFF;
        for (var i = 0; i < 16; i++)
            Array.Copy(mac, 0, packet, 6 + i * 6, 6);

        using var client = new UdpClient { EnableBroadcast = true };
        var endpoint = new IPEndPoint(IPAddress.Parse(broadcastAddress), port);

        // Se envía dos veces por si se pierde el primer datagrama UDP.
        await client.SendAsync(packet, packet.Length, endpoint);
        await client.SendAsync(packet, packet.Length, endpoint);
    }

    // Normaliza "AA:BB:CC:DD:EE:FF", "AA-BB-...", "aabb.ccdd.eeff" a 6 bytes.
    // Devuelve false si la MAC no es válida (sirve para validar en la API).
    public static bool TryParseMac(string macAddress, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(macAddress))
            return false;

        var clean = macAddress
            .Replace(":", "")
            .Replace("-", "")
            .Replace(".", "")
            .Trim();

        if (clean.Length != 12)
            return false;

        var parsed = new byte[6];
        for (var i = 0; i < 6; i++)
        {
            if (!byte.TryParse(clean.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out parsed[i]))
                return false;
        }

        bytes = parsed;
        return true;
    }
}
