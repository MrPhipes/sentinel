# Phipes Sentinel

Centinela liviano de red, en C# / .NET 10 (LTS), pensado para correr en un contenedor
Docker dentro de un equipo siempre encendido (por ejemplo un **iHost** que arranca
solo al volver la corriente).

Monitorea tu **NAS**, tu **servidor** o una lista de equipos —vía **ping ICMP** y
**chequeo de puertos TCP**— y te deja **encenderlos remotamente** con un
**Wake-on-LAN** desde un dashboard web o por API REST, incluso cuando no estás en
casa.

---

## Características

- **Monitoreo doble**: ping ICMP (¿está encendido?) + puertos TCP por servicio
  (SMB, SSH, RDP, web…). Un equipo se reporta "en línea" si responde el ping **o**
  si algún puerto está abierto, así sigue siendo útil aunque el ICMP esté bloqueado.
- **Wake-on-LAN**: envía el paquete mágico por UDP broadcast a la MAC del equipo.
- **Dashboard web**: una tarjeta por equipo con estado, latencia, puertos y un
  botón **Encender**. Responsivo (sirve desde el celular) y con auto-refresh.
- **API REST**: para integrar con Home Assistant, `curl`, scripts o un bot.
- **Edición en caliente**: agregás, editás y eliminás equipos **desde el propio
  dashboard** (o por API). Se persiste en un archivo JSON dentro de un volumen, sin
  reiniciar el contenedor ni tocar `appsettings.json`.

---

## Inicio rápido (PC / servidor con Docker)

```bash
git clone https://github.com/MrPhipes/sentinel.git
cd sentinel

# 1. Edita la lista de equipos (IP, MAC, puertos)
nano appsettings.json

# 2. Levanta el contenedor
docker compose up -d --build
```

Abre `http://<host>:8080` y verás el dashboard.

> El `docker-compose.yml` ya viene con `restart: unless-stopped`, así que el
> contenedor vuelve solo después de un corte de luz.

---

## Desplegar en el iHost (eWeLink CUBE · ARM v7)

El iHost de Sonoff corre **`linux/arm/v7`**. El `Dockerfile` usa una base
**Alpine** que publica arm32v7 (la imagen Debian por defecto de .NET **no** trae
arm32) y cross-compila para no emular el SDK ARM.

**1. La imagen ya está publicada en ghcr.io.** GitHub Actions la construye
multi-arch (amd64 + arm64 + arm/v7) y la sube a:

```
ghcr.io/mrphipes/sentinel:latest
```

No necesitas Docker local ni tokens: el workflow `release.yml` la republica con
cada tag `vX.Y.Z`. Para que el iHost la baje sin credenciales, deja el *package*
como **público** (GitHub → tu perfil → Packages → `sentinel` → Settings → Change
visibility → Public).

**2. Añadir e instalar en el CUBE** (`http://<ip-ihost>/#/docker`):

1. *Add-on List* → **+ Add** → **Add Repository**: URL `ghcr.io` (sin auth si el
   package es público; si lo dejas privado, usuario `MrPhipes` + un token con
   `read:packages`).
2. Busca/añade `ghcr.io/mrphipes/sentinel:latest` → **Install** y, en el panel:
   - **Network mode: `host`** ← imprescindible para que el Wake-on-LAN (broadcast
     L2) y el ping lleguen a tu LAN.
   - Sin `host`, mapea el puerto `8080:8080` y pon `WolBroadcastAddress` con el
     broadcast dirigido de tu subred (ej. `192.168.1.255`).
   - **Volumen**: mapeá una ruta persistente del iHost a **`/app/data`** para que
     la lista de equipos que edités desde el dashboard sobreviva reinicios.

> El ping ICMP necesita la capability `NET_RAW`. Si el panel del CUBE no la expone,
> el ping puede fallar, pero el **chequeo TCP** igual reporta el estado de cada
> equipo (por eso "online" = ping **o** puerto abierto).

---

## ⚠️ Wake-on-LAN y red de Docker (importante)

El paquete mágico de Wake-on-LAN es un **broadcast de capa 2**. Si el contenedor
corre en una red *bridge* de Docker, ese broadcast **no sale a tu LAN** y el WOL no
enciende nada.

Por eso el `docker-compose.yml` usa **`network_mode: host`**: el contenedor comparte
la red del iHost y el broadcast (y el ping) llegan a tus equipos reales. Si por
algún motivo no puedes usar host networking, configura `WolBroadcastAddress` con el
**broadcast dirigido** de tu subred (p. ej. `192.168.1.255`).

### Requisitos en los equipos a despertar

- **Wake-on-LAN habilitado** en la BIOS/UEFI y en el adaptador de red.
- En Windows: en el Administrador de dispositivos → propiedades del adaptador →
  *Energía*, activar "Permitir que este dispositivo reactive el equipo".
- En NAS (Synology/QNAP/etc.): activar WOL en el panel de energía.
- El equipo debe estar **suspendido o apagado dejando energía a la NIC** (no
  desconectado de la corriente).

---

## Configuración (`appsettings.json`)

```json
{
  "Sentinel": {
    "PollIntervalSeconds": 30,
    "PingTimeoutMs": 1500,
    "TcpTimeoutMs": 1500,
    "WolBroadcastAddress": "255.255.255.255",
    "WolPort": 9,
    "Devices": [
      {
        "Id": "nas",
        "Name": "NAS de casa",
        "Host": "192.168.1.10",
        "MacAddress": "AA:BB:CC:DD:EE:01",
        "Ports": [
          { "Name": "SMB", "Number": 445 },
          { "Name": "SSH", "Number": 22 }
        ]
      }
    ]
  }
}
```

| Campo | Descripción |
|---|---|
| `PollIntervalSeconds` | Cada cuántos segundos se sondea (mínimo 5). |
| `PingTimeoutMs` / `TcpTimeoutMs` | Timeouts de ping y de conexión TCP. |
| `WolBroadcastAddress` / `WolPort` | Destino del paquete mágico. |
| `DataPath` | Archivo JSON con la lista editable (default `data/devices.json`). |
| `Devices[]` | Semilla inicial; tras el primer arranque manda `DataPath`. |
| `Devices[].Id` | Identificador usado en la API (autogenerado al agregar por UI). |
| `Devices[].Host` | IP o hostname para ping / TCP. |
| `Devices[].MacAddress` | MAC destino del WOL (acepta `:`, `-` o `.`). |
| `Devices[].Ports` | Puertos TCP a chequear, con etiqueta. |

> Versionamos `appsettings.json` con equipos de **ejemplo**. Para tus datos reales
> usa un `appsettings.Local.json` (ya está en `.gitignore`) o edita el montado por
> Docker — no publiques las IP/MAC de tu casa en el repo.

---

## API REST

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/api/devices` | Estado en vivo de todos los equipos. |
| `GET` | `/api/devices/{id}` | Estado de un equipo. |
| `POST` | `/api/devices/{id}/wake` | Envía Wake-on-LAN al equipo. |
| `GET` | `/api/config/devices` | Lista la configuración editable. |
| `POST` | `/api/config/devices` | Agrega un equipo. |
| `PUT` | `/api/config/devices/{id}` | Edita un equipo. |
| `DELETE` | `/api/config/devices/{id}` | Elimina un equipo. |
| `GET` | `/api/health` | Healthcheck del servicio. |

```bash
# Encender el NAS
curl -X POST http://<ip-del-ihost>:8080/api/devices/nas/wake
```

---

## Desarrollo local (sin Docker)

```bash
dotnet run
# Dashboard en http://localhost:8080  (o el puerto de ASPNETCORE_URLS)
```

Requiere el **SDK de .NET 10**. En Windows el ping ICMP funciona de fábrica; en
Linux dentro de Docker se habilita con la capability `NET_RAW` (ya incluida en el
compose).

---

## Estructura

```
Phipes.Sentinel.csproj      Proyecto ASP.NET Core (Web SDK)
Program.cs                  Endpoints REST + archivos estáticos
Models/                     SentinelOptions, DeviceConfig, DeviceStatus
Services/
  MonitorService.cs         Sondeo periódico (ping + TCP) en segundo plano
  WakeOnLanService.cs       Construcción y envío del paquete mágico
  DeviceRepository.cs       Lista editable de equipos, persistida en JSON
  DeviceStatusStore.cs      Estado en memoria, seguro para concurrencia
wwwroot/index.html          Dashboard web
Dockerfile / docker-compose.yml
```

---

## Licencia

[MIT](LICENSE) © 2026 Phipes · [phipes.cl](https://phipes.cl)
