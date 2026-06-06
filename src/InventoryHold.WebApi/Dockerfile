# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution + project files first for layer-cached restore
COPY InventoryHold.sln ./
COPY src/InventoryHold.Contracts/InventoryHold.Contracts.csproj             src/InventoryHold.Contracts/
COPY src/InventoryHold.Domain/InventoryHold.Domain.csproj                   src/InventoryHold.Domain/
COPY src/InventoryHold.Infrastructure/InventoryHold.Infrastructure.csproj   src/InventoryHold.Infrastructure/
COPY src/InventoryHold.WebApi/InventoryHold.WebApi.csproj                   src/InventoryHold.WebApi/
COPY src/InventoryHold.UnitTests/InventoryHold.UnitTests.csproj             src/InventoryHold.UnitTests/

RUN dotnet restore

# Copy remaining source and publish
COPY . .
RUN dotnet publish src/InventoryHold.WebApi/InventoryHold.WebApi.csproj \
    -c Release -o /app/publish --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "InventoryHold.WebApi.dll"]
