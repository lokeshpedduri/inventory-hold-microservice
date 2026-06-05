# AI-USAGE.md

> How AI tooling was used to build this service, what was accepted vs rejected, and how the
> output was verified. This is a first-class deliverable, maintained as the work progressed.

## 1. AI Strategy

**Tools used**
- **Claude (chat)** — architecture, system design (Excalidraw), build planning, prompt sequencing.
- **Claude Code** — phase-by-phase implementation against a locked plan.
- _(add Cursor / Copilot here if you also use them)_

**Context engineering**
The core of the strategy was a committed [`CLAUDE.md`](./CLAUDE.md) written *before any code*. It
pins the target framework, the exact DDD folder layout, the dependency direction, the expiry
strategy (and why TTL was rejected), the concurrency technique, the error-handling contract, and
documentation expectations. Every Claude Code task was steered by this file rather than ad-hoc
prompts, so output stayed architecturally consistent across layers.

Work was deliberately **phased** (skeleton → contracts → domain → infrastructure → web/wiring →
tests → docker → frontend) so each layer could be reviewed and corrected before the next built on it.

**Prompt structure pattern used**
> Context (point at CLAUDE.md + relevant files) → Task (single layer) → Constraints (the rules
> that matter for this layer) → Definition of done (what "correct" looks like).

## 2. Human Audit — accepted vs rejected

> Fill these in as you go. A few are pre-seeded from the design phase. Keep them specific —
> "the AI did X, I changed it to Y because Z" is what demonstrates judgment.

### Rejected
- **Destructive MongoDB TTL index for expiry.** A first instinct (and a common AI suggestion) is to
  let a TTL index auto-delete expired holds. Rejected: it can't restore inventory and can't emit
  `HoldExpired`. Replaced with a status-transition + background-worker + lazy-read hybrid.
- **Read-then-write stock check.** Rejected the naive "read available, compare, then decrement"
  because of the race window. Replaced with a single conditional `FindOneAndUpdateAsync` where the
  stock guard lives in the filter.
- _(add more as they happen)_

### Accepted (with verification)
- _(e.g. "Accepted the AI's cache-aside invalidation points after confirming every mutation path
  invalidates the inventory key — verified by test X.")_

## 3. Verification

- **Tests:** how AI helped generate the xUnit suite, and how you confirmed the tests actually
  assert meaningful behavior (not tautologies). Note the concurrency test specifically.
- **Manual review gates:** what you checked at each phase boundary (dependency direction, no driver
  types in Domain, status codes, etc.).
- **Runtime validation:** `docker-compose up --build` brings the stack up; manual API calls /
  Swagger exercised each endpoint; frontend observed syncing after mutations.
