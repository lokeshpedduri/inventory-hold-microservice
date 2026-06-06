# CLAUDE.md — Project Context & Engineering Standards

> This file is the single source of truth for how code in this repository is written.
> Claude Code MUST read and obey it on every task. It encodes architecture, conventions,
> and the *reasoning* behind key decisions so generated code is consistent and defensible.

---

## 1. What we are building

An **Inventory Hold Microservice**. When a customer begins checkout, items are placed on a
temporary *hold* so they cannot be sold to another customer. Holds expire after a configurable
duration (default 15 minutes). This is the classic e-commerce reservation pattern.

This is a hands-on weekend project to work a realistic e-commerce stack end to end. I'm
optimizing for a codebase that's modular, readable, and maintainable — the kind of thing I'd
be happy to come back to months later and still understand.

## 2. Tech stack (do not deviate without an ADR)

| Concern        | Choice                          | Notes |
|----------------|---------------------------------|-------|
| Runtime        | **.NET 10**, C#                 | Use current/latest syntax & features. |
| Persistence    | **MongoDB** (official C# driver)| Source of truth for holds + inventory. |
| Concurrency    | Atomic `FindOneAndUpdateAsync`  | Conditional filter guards stock. NO multi-doc transactions needed. |
| Cache          | **Redis** (StackExchange.Redis) | Cache-aside on the inventory read path. |
| Messaging      | **RabbitMQ** (RabbitMQ.Client)  | Topic exchange, durable queues. |
| API            | ASP.NET Core Minimal/Controller | Controllers for clarity. Problem Details for errors. |
| Tests          | **xUnit** + Moq                 | Mock all ports; no live infra in unit tests. |
| Frontend       | **React + TypeScript + Vite**   | State via **TanStack Query** (server-state). |
| Container      | **Docker + docker-compose**     | One command: `docker-compose up --build`. |

**Rejected alternatives (and why):**
- *Postgres*: not needed for what this project does. Out of scope; noted in ARCHITECTURE.md only.
- *Zustand / Redux on the frontend*: this app is ~all server-state; TanStack Query gives
  caching + auto-invalidation-after-mutation for free. Adding a client store would be over-engineering.
- *Destructive MongoDB TTL index for expiry*: rejected — see §5.

## 3. Architecture — Domain-Driven Design layering

Dependencies point **inward**. Domain depends on nothing. Infrastructure and WebApi depend on Domain/Contracts.

```
src/
├── InventoryHold.Contracts/      # DTOs, enums, event payloads. No logic. No deps.
├── InventoryHold.Domain/
│   ├── Entities/                 # Hold, InventoryItem (domain models)
│   ├── Services/                 # HoldService — business logic, lifecycle rules
│   └── Abstractions/             # IHoldRepository, IInventoryRepository, ICache, IEventBus (PORTS)
├── InventoryHold.Infrastructure/ # Mongo repos, Redis cache, RabbitMQ bus (ADAPTERS), seeding
├── InventoryHold.WebApi/         # Controllers, DI, Program.cs, middleware, ExpiryWorker
└── InventoryHold.UnitTests/      # xUnit, mocks the ports
```

**Ports & Adapters rule:** Domain defines interfaces (ports). Infrastructure implements them
(adapters). The Domain layer must NEVER reference MongoDB, Redis, or RabbitMQ types. If you find
yourself importing a driver type into Domain, stop — it belongs behind a port.

## 4. Domain model

**InventoryItem**: `{ ProductId, Name, AvailableQuantity }` — `AvailableQuantity` is the live
sellable count (already net of active holds).

**Hold**: `{ HoldId, Items[{ProductId, Quantity}], Status, CreatedAtUtc, ExpiresAtUtc }`
- `Status` enum: `Active | Released | Expired`. **Status is the source of truth for lifecycle.**
- Holds are NEVER hard-deleted on expiry/release. They transition status. (Auditability + idempotency.)

## 5. Hold expiry strategy (the key design decision)

We use a **hybrid** approach, NOT a destructive TTL index:

1. **Background worker** (`IHostedService`) sweeps on an interval for `Active` holds where
   `ExpiresAtUtc < now`. For each: flip status → `Expired`, restore inventory atomically,
   publish `HoldExpired`.
2. **Lazy check on read**: `GET /holds/{id}` checks expiry inline so a just-expired hold reads
   as `Expired` even between sweeps.

**Why not a plain MongoDB TTL index?** A TTL index physically deletes the document. That would
(a) make it impossible to restore inventory, and (b) make it impossible to publish `HoldExpired`
or return a proper expired state on GET. The requirements need both. A TTL index could only ever
be a *cleanup* mechanism for already-terminal records — mention as future work, do not rely on it.

## 6. Concurrency (the core problem)

Stock decrement and restore MUST be race-safe via a **single conditional atomic operation**:

- **Deduct**: `FindOneAndUpdateAsync` with filter `{ ProductId == X AND AvailableQuantity >= qty }`
  and update `$inc: { AvailableQuantity: -qty }`. If filter matches none → returns null →
  insufficient stock, no decrement occurred. No read-then-write race window.
- **Restore**: `$inc: { AvailableQuantity: +qty }` (unconditional).
- Multi-item holds: deduct each item; if any deduct fails, **compensate** by restoring the
  already-deducted items, then fail the request. (No cross-document transaction required.)

Document-level writes in MongoDB are atomic; this is why we don't need transactions here.

## 7. API contract

| Method | Route                 | Success | Errors |
|--------|-----------------------|---------|--------|
| POST   | `/api/holds`          | 201 + hold | 400 invalid body, 409 insufficient stock |
| GET    | `/api/holds/{holdId}` | 200 + hold | 404 not found |
| DELETE | `/api/holds/{holdId}` | 200 / 204  | 404 not found, 409 already terminal |
| GET    | `/api/inventory`      | 200 + list | — |

- Return **meaningful status codes**, never just 200/500.
- All errors use **RFC 7807 Problem Details** (`application/problem+json`).
- Validate input at the controller/DTO boundary; domain assumes valid shapes but enforces invariants.

## 8. Messaging

Topic exchange `inventory.holds`. Routing keys: `hold.created`, `hold.released`, `hold.expired`.
Events carry enough context to act on independently: `HoldId`, item lines, `OccurredAtUtc`, reason.
Publishing failures must NOT corrupt state — publish after the DB mutation commits; log on failure.

## 9. Caching

Cache-aside on `GET /api/inventory` (high-frequency read). On any stock mutation
(create/release/expire), **invalidate** the inventory cache key so reads stay consistent.
Modest TTL (e.g. 30s) as a safety net. Document the chosen TTL and the invalidation points.

## 10. Code quality conventions

- **SOLID**, small focused classes, constructor injection only. No service locator.
- `async`/`await` end-to-end; suffix async methods with `Async`; pass `CancellationToken`.
- One public type per file; file name == type name.
- Nullable reference types **enabled**. Treat warnings as errors where practical.
- **Every layer is documented**: XML doc comments on public types/members stating *intent and
  why*, not just what. Non-obvious decisions get an inline `// WHY:` comment.
- No magic values — expiry duration, TTLs, connection strings come from config/env.

## 11. Configuration

NOTHING hardcoded. All connection strings and tunables via `appsettings.json` +
environment variable overrides (compose injects env). Provide sensible local defaults.

## 12. Documentation deliverables (maintained in parallel with code)

- `README.md` — setup, one-command startup, design summary.
- `ARCHITECTURE.md` — layer diagram, data flow, where Postgres *would* fit (out of scope).
- `docs/DECISIONS.md` — lightweight ADRs (one per key decision: expiry, concurrency, state mgmt).
- `AI-USAGE.md` — AI strategy, accepted/rejected suggestions with reasoning, verification approach.

## 13. Definition of done

- `docker-compose up --build` brings up API + Mongo + Redis + RabbitMQ in one command.
- DB seeded with ≥5 products on startup.
- ≥5 xUnit tests (validation, lifecycle, ≥1 concurrency edge case), all mock ports, no live infra.
- Frontend: 4 screens, typed, live-syncing after mutations, loading + error states.
- All four doc deliverables present and accurate.
