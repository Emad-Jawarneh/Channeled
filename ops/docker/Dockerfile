# syntax=docker/dockerfile:1
#
# Used by docker compose and Railway (see railway.toml → DOCKERFILE).
# Entrypoint binds ASPNETCORE_URLS to $PORT (Railway injects PORT).

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
 && mkdir -p /app/data/object-store /app/data/backups /app/data/dp-keys \
 && chown -R $APP_UID:$APP_UID /app/data

COPY --from=build /app/publish ./
COPY --from=web /src/web/dist ./wwwroot
COPY ops/docker/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh \
 && chown $APP_UID:$APP_UID /app/entrypoint.sh

# Defaults for Compose. Do not hardcode ASPNETCORE_URLS — entrypoint uses $PORT.
# Postgres: set DATABASE_URL or ConnectionStrings__Postgres at deploy time
# (Railway: Variables → DATABASE_URL = ${{Postgres.DATABASE_URL}}).
ENV ASPNETCORE_ENVIRONMENT=Production \
    MigrateOnStartup=true \
    FileStore__Kind=FileSystem \
    FileStore__RootPath=/app/data/object-store \
    Backup__RootPath=/app/data/backups \
    DataProtection__KeysPath=/app/data/dp-keys \
    PORT=8080

EXPOSE 8080
USER $APP_UID

# No HEALTHCHECK: Railway / Compose define their own.

ENTRYPOINT ["/app/entrypoint.sh"]
