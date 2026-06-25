# syntax=docker/dockerfile:1
# ==============================================================================
# FinanceCore.API — imagen de runtime para deploy (Render / cualquier PaaS Docker)
# ------------------------------------------------------------------------------
# Build context: la RAÍZ del repo (no esta carpeta), porque la API referencia
# por ProjectReference a Application → Infrastructure → Domain. En Render: setear
# "Dockerfile Path" = src/FinanceCore.API/Dockerfile y dejar el root como context.
# ==============================================================================

# ─── Build ────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Copiar solo los .csproj primero → cachea el `restore` mientras el código
#    cambia. La API arrastra sus refs transitivas (Application/Infra/Domain).
COPY src/FinanceCore.Domain/FinanceCore.Domain.csproj                 src/FinanceCore.Domain/
COPY src/FinanceCore.Application/FinanceCore.Application.csproj        src/FinanceCore.Application/
COPY src/FinanceCore.Infrastructure/FinanceCore.Infrastructure.csproj src/FinanceCore.Infrastructure/
COPY src/FinanceCore.API/FinanceCore.API.csproj                       src/FinanceCore.API/
RUN dotnet restore src/FinanceCore.API/FinanceCore.API.csproj

# 2. Copiar el resto del código y publicar (sin apphost nativo: corremos vía dll).
COPY src/ src/
RUN dotnet publish src/FinanceCore.API/FinanceCore.API.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

# ─── Runtime ──────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./

# Render (y la mayoría de PaaS) inyectan el puerto por la env $PORT. Hacemos bind
# dinámico con fallback a 8080 para `docker run -p 8080:8080 ...` en local.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet FinanceCore.API.dll"]
