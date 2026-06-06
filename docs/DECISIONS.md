# Architecture Decision Records

Short notes on the decisions that shaped this repo: what I picked, why, what I passed on, and
what it cost me. Mostly so future-me remembers the reasoning instead of guessing.

_The calls here are mine. — Lokesh Pedduri_

---

## ADR-001: Hybrid hold expiry (status + worker + lazy read), not a TTL index
**Status:** Accepted
**Context:** Holds expire after a configurable duration. On expiry we must (a) restore inventory
and (b) publish a `HoldExpired` event, and `GET` must report an expired hold correctly.
**Decision:** Holds carry a `Status` (`Active|Released|Expired`) and are never hard-deleted. A
background `IHostedService` sweeps expired-but-Active holds, restores stock atomically, and
publishes `HoldExpired`. `GET /holds/{id}` also checks expiry lazily.
**Alternatives:** MongoDB TTL index (auto-delete) — rejected: deletion prevents stock restore and
event emission. Pure lazy expiry — rejected: inventory would never recover without a read.
**Consequences:** Slightly more code (a worker) but full control, auditability, and idempotent
lifecycle. A TTL index remains available later purely to purge terminal records.

---

## ADR-002: Conditional atomic `FindOneAndUpdate` for stock safety
**Status:** Accepted
**Context:** Concurrent checkouts must not oversell inventory.
**Decision:** Deduct stock with a single `FindOneAndUpdateAsync` whose filter includes
`AvailableQuantity >= qty` and whose update is `$inc: -qty`. A null result means insufficient
stock; no decrement occurred. Restores are an unconditional `$inc: +qty`.
**Alternatives:** Read-modify-write (race window); multi-document transactions (unneeded overhead
for single-document atomicity).
**Consequences:** Race-free at the document level without transactions. Multi-item holds use
application-level compensation (restore already-deducted items on partial failure).

---

## ADR-003: TanStack Query for frontend state
**Status:** Accepted
**Context:** The UI must reflect inventory/hold changes without manual refresh.
**Decision:** Use TanStack Query. Inventory and holds are server-state; queries are invalidated
after each mutation, giving automatic, consistent re-sync.
**Alternatives:** Zustand / Redux — rejected: they manage client-state this app doesn't have;
would add ceremony without benefit.
**Consequences:** Minimal state code, built-in caching/refetch, requirement satisfied directly.

---

## ADR-004: DDD ports-and-adapters layering
**Status:** Accepted
**Context:** I wanted this to stay maintainable and genuinely easy to unit-test.
**Decision:** Domain defines ports (repository/cache/bus interfaces) and holds all business logic;
Infrastructure provides adapters. Dependencies point inward; Domain references no infra SDKs.
**Consequences:** Unit tests mock ports and need no live infra. Infra is swappable. Clear review
gate: "is there a driver type in Domain?" must always be no.
