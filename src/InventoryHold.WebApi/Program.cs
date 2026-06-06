using InventoryHold.Domain.Services;
using InventoryHold.Infrastructure;
using InventoryHold.Infrastructure.Seeding;
using InventoryHold.WebApi.Middleware;
using InventoryHold.WebApi.Workers;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Controllers + Problem Details ────────────────────────────────────────────
// Problem Details is the default error format for all controller validation errors.
// Domain exceptions are mapped in ExceptionMiddleware (RFC 7807, §7).
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// ── OpenAPI / Scalar ─────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── Options ──────────────────────────────────────────────────────────────────
// Hold lifecycle tunables (expiry duration) — read by HoldService via IOptions<HoldOptions>.
builder.Services.Configure<HoldOptions>(
    builder.Configuration.GetSection(HoldOptions.SectionName));

// ExpiryWorker sweep interval — read by ExpiryWorker via IOptions<ExpiryWorkerOptions>.
builder.Services.Configure<ExpiryWorkerOptions>(
    builder.Configuration.GetSection(ExpiryWorkerOptions.SectionName));

// ── Infrastructure adapters ───────────────────────────────────────────────────
// Registers MongoDB client, Redis multiplexer, RabbitMQ channel, repository and
// cache adapters, the event bus, and InventorySeeder. All connection details are
// read from configuration — nothing hardcoded (§11).
builder.Services.AddInfrastructure(builder.Configuration);

// ── Domain service ────────────────────────────────────────────────────────────
// Scoped so it shares the same repository/cache/event-bus instances within a request.
builder.Services.AddScoped<HoldService>();

// ── Background worker ────────────────────────────────────────────────────────
// Sweeps for expired Active holds on a configurable interval (§5, ADR-001).
builder.Services.AddHostedService<ExpiryWorker>();

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ── Global exception → Problem Details middleware ────────────────────────────
// Registered first so it wraps the entire pipeline and catches all domain exceptions.
app.UseMiddleware<ExceptionMiddleware>();

// ── OpenAPI UI ───────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();   // available at /scalar/v1
}

// ── Startup seeding ───────────────────────────────────────────────────────────
// Seeds ≥5 products on first run (idempotent — skips if collection already has data).
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<InventorySeeder>();
    await seeder.SeedAsync();
}

// ── Routing ───────────────────────────────────────────────────────────────────
app.UseRouting();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Expose Program for integration test WebApplicationFactory
public partial class Program { }
