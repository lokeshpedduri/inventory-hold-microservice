# Inventory Hold Microservice

An e-commerce inventory reservation service. When a customer begins checkout, items are placed on a temporary **hold** so they cannot be sold to another customer. Holds expire after a configurable duration (default 15 minutes) and are released automatically.

Built as a Senior Full Stack assignment — graded on architecture quality, code quality, testing, and AI-steering. See [`CLAUDE.md`](CLAUDE.md) for the binding spec and [`docs/DECISIONS.md`](docs/DECISIONS.md) for the ADRs.

---

## Quick start — one command

```bash
docker-compose up --build
```

That's it. Docker Compose builds the API image, pulls MongoDB / Redis / RabbitMQ, waits for each dependency to pass its health check, runs the startup seeder (≥ 5 products), then starts serving.

> **Prerequisites:** Docker Desktop ≥ 4.x (or Docker Engine + Compose plugin). No .NET SDK required on the host.

---

## Exposed ports

| Service | Port | Notes |
|---|---|---|
| **API** | `8080` | REST — `http://localhost:8080` |
| **API docs** | `8080/scalar/v1` | Scalar interactive UI (dev mode only; not exposed in Production compose) |
| **MongoDB** | `27017` | Host-mapped for local inspection with Compass / mongosh |
| **Redis** | `6379` | Host-mapped for local inspection with redis-cli |
| **RabbitMQ AMQP** | `5672` | Used internally by the API |
| **RabbitMQ Management UI** | `15672` | `http://localhost:15672` — credentials `guest / guest` |
| **Health check** | `8080/health` | Returns 200 when the API process is up |

---

## API endpoints

| Method | Route | Success | Error codes |
|---|---|---|---|
| `POST` | `/api/holds` | `201` — created hold | `400` invalid body · `409` insufficient stock |
| `GET` | `/api/holds/{holdId}` | `200` — hold state | `404` not found |
| `DELETE` | `/api/holds/{holdId}` | `200` — released hold | `404` not found · `409` already terminal |
| `GET` | `/api/inventory` | `200` — product list | — |

All error responses follow **RFC 7807 Problem Details** (`application/problem+json`).

### Example — place a hold

```bash
curl -s -X POST http://localhost:8080/api/holds \
  -H "Content-Type: application/json" \
  -d '{
    "items": [
      { "productId": "prod-001", "quantity": 2 },
      { "productId": "prod-003", "quantity": 1 }
    ]
  }' | jq
```

```json
{
  "holdId": "4a9f1e2b...",
  "items": [
    { "productId": "prod-001", "quantity": 2 },
    { "productId": "prod-003", "quantity": 1 }
  ],
  "status": "Active",
  "createdAtUtc": "2025-01-15T10:00:00Z",
  "expiresAtUtc": "2025-01-15T10:15:00Z"
}
```

### Example — release a hold

```bash
curl -s -X DELETE http://localhost:8080/api/holds/4a9f1e2b... | jq
```

### Example — view inventory

```bash
curl -s http://localhost:8080/api/inventory | jq
```

---

## Seeded products

On first startup, seven products are inserted (idempotent — skipped if data already exists):

| ID | Name | Initial stock |
|---|---|---|
| `prod-001` | Wireless Headphones | 50 |
| `prod-002` | Mechanical Keyboard | 30 |
| `prod-003` | USB-C Hub (7-port) | 75 |
| `prod-004` | 27" 4K Monitor | 20 |
| `prod-005` | Ergonomic Mouse | 100 |
| `prod-006` | Laptop Stand (Aluminium) | 60 |
| `prod-007` | Webcam 1080p | 45 |

---

## Configuration

All tunables are injected as environment variables into the API container — nothing is hardcoded. The defaults below are what docker-compose sets:

| Env var | Default (compose) | Description |
|---|---|---|
| `Mongo__ConnectionString` | `mongodb://mongo:27017` | MongoDB connection string |
| `Mongo__DatabaseName` | `inventory_hold` | Database name |
| `Redis__ConnectionString` | `redis:6379` | Redis connection string |
| `RabbitMq__Host` | `rabbitmq` | RabbitMQ hostname |
| `RabbitMq__Username` | `guest` | RabbitMQ username |
| `RabbitMq__Password` | `guest` | RabbitMQ password |
| `RabbitMq__ExchangeName` | `inventory.holds` | Topic exchange name |
| `Hold__ExpiryDuration` | `00:15:00` | How long a hold stays active |
| `ExpiryWorker__SweepInterval` | `00:01:00` | How often the expiry worker sweeps |

Override any value by editing `docker-compose.yml` or passing `-e` flags.

---

## Architecture summary

```
┌─────────────────────────────────────────────────────────┐
│  InventoryHold.Contracts   (DTOs · enums · events)       │
│  InventoryHold.Domain      (entities · ports · service)  │◄─ no infra deps
│  InventoryHold.Infrastructure  (Mongo · Redis · Rabbit)  │
│  InventoryHold.WebApi      (controllers · worker · DI)   │
└─────────────────────────────────────────────────────────┘
```

**Ports & Adapters (DDD):** Domain defines four interfaces (`IHoldRepository`, `IInventoryRepository`, `ICache`, `IEventBus`). Infrastructure implements them. Domain never references MongoDB, Redis, or RabbitMQ types.

**Race-safe stock reservation:** Stock is decremented via a single `FindOneAndUpdateAsync` with the filter `{ ProductId == X AND AvailableQuantity >= qty }`. The check and decrement are one atomic MongoDB operation — no read-then-write race window, no transactions needed. Multi-item holds use application-level compensation if any item fails.

**Hold expiry (hybrid):**
1. `ExpiryWorker` (`IHostedService`) sweeps every minute for `Active` holds past their TTL, restores stock, and publishes `HoldExpired`.
2. `GET /holds/{id}` applies lazy expiry inline — a hold that slipped past a sweep reads as `Expired` immediately.

A MongoDB TTL index was explicitly rejected because it physically deletes the document, making stock restoration and event publishing impossible. See [ADR-001](docs/DECISIONS.md).

**Cache-aside:** `GET /api/inventory` is served from Redis (30 s TTL safety net). The cache is explicitly invalidated after every stock mutation (hold created, released, or expired).

---

## Frontend (local dev)

```bash
cd frontend
npm install
npm run dev     # http://localhost:5173
```

The Vite dev server proxies `/api/*` → `http://localhost:8080`, so start the backend stack first (`docker-compose up`) then run the frontend separately. No CORS configuration needed.

| Screen | Description |
|---|---|
| **Inventory** | Live grid of all products with available-quantity counts; auto-refreshes every 30 s |
| **New Hold** | Product stepper form; quantity guard prevents requesting more than available; submitting invalidates both the inventory and holds queries |
| **Holds** | Full holds list ordered Active-first; per-second countdown timer per active hold (amber < 2 min, red < 30 s); Release button with confirmation dialog |

**Font pairing:** Space Grotesk (UI) + JetBrains Mono (data/numbers/IDs/countdowns).

---

## Running tests

```bash
dotnet test src/InventoryHold.UnitTests/InventoryHold.UnitTests.csproj
```

10 xUnit tests — all ports mocked with Moq, no live infrastructure required.

---

## Stopping and cleaning up

```bash
# Stop containers, keep MongoDB volume
docker-compose down

# Stop containers AND delete MongoDB data
docker-compose down -v
```

---

## Project layout

```
InventoryHold.sln
src/
├── InventoryHold.Contracts/       # DTOs, enums, event payloads — no logic
├── InventoryHold.Domain/
│   ├── Entities/                  # Hold, InventoryItem, HoldItem
│   ├── Services/                  # HoldService, exceptions, options
│   └── Abstractions/              # Ports: IHoldRepository, IInventoryRepository, ICache, IEventBus
├── InventoryHold.Infrastructure/  # MongoDB repos, Redis cache, RabbitMQ bus, seeding
├── InventoryHold.WebApi/          # Controllers, ExpiryWorker, middleware, Program.cs
└── InventoryHold.UnitTests/       # xUnit + Moq — no live infra
docs/
├── DECISIONS.md                   # ADRs (expiry strategy, concurrency, state mgmt, DDD)
AI-USAGE.md                        # AI strategy and verification log
CLAUDE.md                          # Binding spec for all code generation
docker-compose.yml
.dockerignore
```
