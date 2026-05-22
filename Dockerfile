# ─── Stage 1: build ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar el .sln y los .csproj primero para aprovechar cache de capas
COPY LitoralMarket.sln ./
COPY src/LitoralMarket.Domain/LitoralMarket.Domain.csproj                 src/LitoralMarket.Domain/
COPY src/LitoralMarket.Application/LitoralMarket.Application.csproj       src/LitoralMarket.Application/
COPY src/LitoralMarket.Infrastructure/LitoralMarket.Infrastructure.csproj src/LitoralMarket.Infrastructure/
COPY src/LitoralMarket.Web/LitoralMarket.Web.csproj                       src/LitoralMarket.Web/

# Restaurar dependencias NuGet
RUN dotnet restore src/LitoralMarket.Web/LitoralMarket.Web.csproj

# Copiar el resto del código y publicar
COPY . .
RUN dotnet publish src/LitoralMarket.Web/LitoralMarket.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ─── Stage 2: runtime ──────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Railway inyecta PORT en tiempo de ejecución. Kestrel debe escuchar en 0.0.0.0
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

# El start script garantiza que escuchemos en el PORT que Railway asigne
# (por defecto 8080 si no está seteado)
EXPOSE 8080
CMD ASPNETCORE_URLS="http://+:${PORT:-8080}" dotnet LitoralMarket.Web.dll
