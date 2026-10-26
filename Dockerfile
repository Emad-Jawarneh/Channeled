# syntax=docker/dockerfile:1

# --- SPA -----------------------------------------------------------------
FROM node:22-bookworm AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# --- .NET publish --------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json Channeled.slnx ./
COPY src/Channeled.Domain/ src/Channeled.Domain/
COPY src/Channeled.Application/ src/Channeled.Application/
COPY src/Channeled.Infrastructure/ src/Channeled.Infrastructure/
COPY src/Channeled.Api/ src/Channeled.Api/
RUN dotnet restore src/Channeled.Api/Channeled.Api.csproj \
 && dotnet publish src/Channeled.Api/Channeled.Api.csproj \
      -c Release \
      --no-restore \
      -o /app/publish \
      /p:UseAppHost=false

# --- Runtime -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /data/object-store /data/backups /data/dp-keys \
 && chown -R $APP_UID:$APP_UID /data

COPY --from=build /app/publish ./
COPY --from=web /src/web/dist ./wwwroot

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    MigrateOnStartup=true \
    FileStore__Kind=FileSystem \
    FileStore__RootPath=/data/object-store \
    Backup__RootPath=/data/backups \
    DataProtection__KeysPath=/data/dp-keys

EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
  CMD curl -fsS http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "Channeled.Api.dll"]
