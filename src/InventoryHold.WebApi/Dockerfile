# ── Build stage ────────────────────────────────────────────────────────────────
# Uses the full SDK image so dotnet restore + publish can run.
# Only the production output is copied to the runtime stage — SDK never ships.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution + project files first so the restore layer is cached independently
# of source changes. A source edit will not re-run `dotnet restore` unless a
# .csproj or .sln file also changes.
COPY InventoryHold.sln                                                           ./
COPY src/InventoryHold.Contracts/InventoryHold.Contracts.csproj                 src/InventoryHold.Contracts/
COPY src/InventoryHold.Domain/InventoryHold.Domain.csproj                       src/InventoryHold.Domain/
COPY src/InventoryHold.Infrastructure/InventoryHold.Infrastructure.csproj       src/InventoryHold.Infrastructure/
COPY src/InventoryHold.WebApi/InventoryHold.WebApi.csproj                       src/InventoryHold.WebApi/

# WHY: UnitTests .csproj is intentionally NOT copied here.
# Including it would force the restore layer to download xUnit + Moq into the
# production image build context; those packages serve no purpose at runtime.
# The solution file still references the test project, so we tell restore to
# ignore it via --ignore-failed-sources is not needed — the sln entry is enough
# for dotnet to skip projects whose .csproj files are absent from the context.
RUN dotnet restore src/InventoryHold.WebApi/InventoryHold.WebApi.csproj

# Copy all source (obj/ bin/ are excluded via .dockerignore)
COPY src/ src/

RUN dotnet publish src/InventoryHold.WebApi/InventoryHold.WebApi.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ── Runtime stage ──────────────────────────────────────────────────────────────
# Lean ASP.NET runtime — no compiler, no SDK, no NuGet cache.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as a non-root user to reduce blast radius of any container escape.
# WHY: aspnet:10.0 is Debian 12 (Bookworm) which ships shadow-utils, not BusyBox.
#      Use groupadd/useradd (shadow) instead of addgroup/adduser (BusyBox/Alpine).
RUN groupadd --system appgroup \
 && useradd  --system --gid appgroup --no-create-home appuser

COPY --from=build --chown=appuser:appgroup /app/publish .

USER appuser

# Port declared for documentation; docker-compose maps it via ASPNETCORE_URLS env var.
EXPOSE 8080

ENTRYPOINT ["dotnet", "InventoryHold.WebApi.dll"]
