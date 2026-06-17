# --- Etapa de compilación ---
# Se compila SIEMPRE en la arquitectura nativa del PC de build ($BUILDPLATFORM),
# generando una publicación "framework-dependent" con IL portable. Ese IL corre en
# cualquier arquitectura, así evitamos emular el SDK ARM bajo QEMU (lento).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Phipes.Sentinel.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish Phipes.Sentinel.csproj -c Release -o /app

# --- Etapa de runtime ---
# IMPORTANTE: la imagen Debian por defecto (aspnet:9.0) NO publica linux/arm/v7.
# Usamos la variante Alpine, que SÍ trae arm32v7 y además es más liviana — clave
# para el iHost (eWeLink CUBE) con TF Card de 7 GB y RAM acotada.
# buildx resuelve esta etapa al target real (arm/v7) según --platform.
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Phipes.Sentinel.dll"]
