# How I built this with AI

**Lokesh Pedduri**

This is the honest version of how I used AI to build the service. What I tried, where I let the
model run, where I stepped in and overruled it, and how I checked the result. The bugs in here are
real ones from the build, not a cleaned-up highlight reel. I wanted it to read like what actually
happened, because the interesting part isn't "I used AI," it's the judgment calls around it.

## The short version

I drove; the AI typed. Before writing any code I did three things: got my tooling set up so the
model had decent defaults, wrote down the rules I wanted the code to follow ([`CLAUDE.md`](./CLAUDE.md)),
and sketched the architecture on a canvas first. After that I generated code one layer at a time,
read every layer before moving on, and ran a heavier model over it periodically to catch what I'd
missed. I made the design decisions. The model filled them in. That's why I can still explain every
part of this repo.

## Getting set up

I spent a bit of time up front on tooling, because the right skills and plugins change what the
model reaches for by default, which means less correcting later.

The skills and plugins I actually leaned on (from `~/.claude/skills`, `~/.agents/skills`, and the
official plugin marketplace):

- Anthropic engineering skills (`architecture`, `system-design`, `code-review`, `testing-strategy`,
  `debug`, `documentation`) for the layering and the test design.
- Frontend and design skills (`frontend-design`, `ui-ux-pro-max`, `web-design-guidelines`,
  `ckm-ui-styling`, `ckm-design-system`) so the UI didn't end up looking like every other AI demo.
  I deliberately went with Space Grotesk + JetBrains Mono and a dark-navy/amber palette.
- Vercel/React skills (`vercel-react-best-practices`, `vercel-composition-patterns`,
  `vercel-optimize`) for the TanStack Query patterns and component structure.
- `csharp-lsp` for language-aware help on .NET 10.
- The `context7` plugin to pull current docs for the MongoDB C# driver v3, StackExchange.Redis,
  RabbitMQ.Client v7, and ASP.NET 10.
- The `github` plugin plus `skill-creator`, `find-skills`, and `commit-commands` for my commit/push
  workflow and for finding the right skills in the first place.

The one worth singling out is `context7`. RabbitMQ.Client v7 and the MongoDB driver v3 both changed
their APIs in ways the model's training data gets wrong (v7's async `IChannel` is the obvious one).
Feeding it the current docs killed a whole category of code that looks right and then falls over at
runtime.

## Architecture before code

I designed the thing visually before writing implementation, using Opus to put the DDD layers and
the request flow onto Excalidraw. Doing it on a canvas made me commit to the hard calls early:

- everything depends inward toward the Domain,
- which four ports live where, and what their adapters are,
- the lifecycle for place, get, release, and expire,
- and where Postgres would slot in if I ever needed it (I didn't, so it's out of scope).

Those went into [`docs/DECISIONS.md`](./docs/DECISIONS.md) as short ADRs and into the rules file,
`CLAUDE.md`.

## Writing the rules down (`CLAUDE.md`)

This was the highest-leverage thing I did. I turned my plan into a committed rules file that every
later prompt pointed at. It pins down the framework (.NET 10), the exact folder layout, the rule
that no MongoDB/Redis/RabbitMQ types are allowed in the Domain, the expiry strategy and why I said
no to a TTL index, the atomic-update concurrency approach, the RFC 7807 error format, and the docs
I wanted to keep.

The trick was writing down the *why*, not just the *what*. Once the reasoning was on the page, the
model stopped trying to re-suggest the approaches I'd already thrown out.

## Splitting the work between models

I used Opus and Sonnet for different jobs:

- Opus for the thinking. Architecture, the plan, the prompt sequence, and the periodic deep reviews.
  I brought it back whenever something got subtle (the lifetime and serialisation bugs below came
  out of those reviews).
- Sonnet for the volume. Once the thinking was done, it wrote each layer against the locked plan.
  Fast and cheap for "build this to this spec."

When Sonnet hit something it wasn't sure about, I bumped the work back up to Opus instead of letting
it guess.

I also kept the context window tight on purpose. Each phase referenced only the two or three files
in play plus the rules file, never the whole tree, and I compacted the conversation at clean breaks.
That alone cut down on drift.

## Building it in phases

I committed and pushed each phase as I went, partly for the safety net and partly so I could see the
thing taking shape:

1. solution scaffold + `Contracts`
2. `Domain`: entities, ports, the `HoldService`, the invariants
3. `Infrastructure`: the Mongo/Redis/RabbitMQ adapters and seeding
4. `WebApi`: controllers, DI, the `ExpiryWorker`, Problem Details, health, Scalar
5. unit tests (10 of them, all ports mocked)
6. Docker + compose + README
7. the React frontend and the `GET /api/holds` endpoint it needed

The prompt shape was the same every time: point at the rules file and the relevant files, give it
one layer, list the constraints that matter for that layer, and say what "done" looks like.

One honest note: at one point the work jumped to the tests and skipped the WebApi phase entirely. I
caught it ("did we miss phase 4?") and we backfilled. Keeping the sequence straight was my job, not
the model's, and that's the kind of thing a person in the loop notices.

## What I kept and what I threw out

Things I rejected:

- A TTL index for expiry. Common suggestion, wrong fit here. A TTL index deletes the document, and
  if it's gone I can't restore the stock or publish a `HoldExpired` event. I went with a status
  field plus a background worker plus a lazy check on read instead.
- The read-then-write stock check. There's a race between the read and the write. I replaced it with
  one `FindOneAndUpdateAsync` where the `AvailableQuantity >= qty` guard sits inside the filter, so
  the check and the decrement happen as a single atomic operation.
- Multi-document transactions. Not needed. Single-document writes are already atomic, and multi-item
  holds get application-level compensation. That also means I don't have to force a replica set.
- A client-side state library on the frontend. This app is basically all server state, so TanStack
  Query already does the caching and the invalidate-after-mutation. Redux would've been ceremony.

Things I accepted, after checking:

- The cache-aside invalidation points, once I'd confirmed every mutation path clears the inventory
  key and written tests that assert it.
- The `JsonStringEnumConverter` fix, once testing showed me the integer-enum problem.
- Optimistic expiry in the UI, as the right way to handle the gap between the timer hitting zero and
  the worker confirming it.

## The bugs my reviews and testing caught

This is the part I care most about. The AI wrote code that was structurally fine but still had real
defects that only showed up once I reviewed it properly or ran it. AI made me a lot faster, but the
checking is what made it correct.

From the Opus reviews, before anything ran:

- A caching bug. `GetInventoryAsync` was caching the `InventoryItem` domain entity, which has
  private setters and no parameterless constructor, so `System.Text.Json` can't deserialise it. Cache
  hits would've returned garbage. I switched it to cache the `InventoryItemResponse` DTO and rebuild
  the entity on read.
- A RabbitMQ lifetime bug. The event bus implemented `IAsyncDisposable` and closed the singleton
  channel when it was disposed, but the bus is scoped, so it would've shut the shared channel after
  the first request and broken every publish after that. I dropped `IAsyncDisposable` and let the
  singleton manage its own lifetime.
- Housekeeping: `obj/bin` got committed, the test project leaked into the Docker build context, some
  compose env vars were missing, and the frontend needed a `GET /api/holds` endpoint that didn't
  exist yet.

The first real compile on .NET 10 (the code hadn't been built on a machine with the SDK until late,
and I have warnings-as-errors on) turned up more:

- a redundant `System.Text.Json` package that ships in the framework anyway,
- transitive `Snappier`/`SharpCompress` CVEs, which I fixed by bumping MongoDB.Driver 3.1 to 3.9,
- `Scalar.AspNetCore` 2.5.8 gone from the feed, pinned to 2.6.0,
- missing `Microsoft.Extensions.Configuration.*` references that had only been there transitively,
- an ambiguous `Deserialize<T>` call on `RedisValue`, which converts to both `string` and
  `ReadOnlySpan<byte>`,
- and a TypeScript `unknown` vs `ReactNode` error in the holds list.

And the ones I only found by running it:

- The Dockerfile used Alpine's `addgroup`/`adduser`, but the aspnet image is Debian, so I switched to
  `groupadd`/`useradd`.
- Scalar was gated to development only, but compose runs in production, so the docs 404'd. I ungated
  them and added a redirect from `/` to the docs, which were also a bare 404.
- `HoldStatus` was serialising as `0/1/2` while the frontend compares against `'Active'`, so every
  hold looked inactive: no timer, no release button. Fixed with `JsonStringEnumConverter`. My unit
  tests didn't catch this one; clicking around the actual UI did. That's exactly why I do both.
- The countdown hit zero but the cards stayed `ACTIVE`, because the list endpoint doesn't do lazy
  expiry (only the single-hold GET does) and the worker runs on an interval. I added optimistic
  expiry on the client and fixed some PascalCase CSS classes while I was there.

## How I checked everything

I didn't trust any single layer of checking on its own, so I ran three.

The unit tests (10, all ports mocked, no live infra) are written to assert real behaviour rather than
shape. The one I like best is the compensation test: item A deducts, item B fails, then assert the
restore for A fired exactly once and the hold never got saved. Strict mocks mean any unexpected call
fails the test.

For a second opinion I had Opus review at the phase boundaries, and I ran the Gemini CLI over the
code too, so I wasn't letting one model family grade its own work.

Then I actually sat down and used it, on both the happy path and the failure path:

- Happy path: place a hold, watch the stock drop, GET it back as Active, release it, watch the stock
  come back. Checked in both curl/Scalar and the React UI.
- Failure path: empty body and quantity zero give 400, over-requesting gives 409, releasing twice
  gives 409, an unknown id gives 404, all as RFC 7807 problem+json.
- Concurrency: I fired 20 parallel POSTs for 20 units of a 20-stock item and got exactly one 201 and
  nineteen 409s, ending at zero stock. That's the proof the atomic filter works, on top of the unit
  test.
- Compensation: requested a plentiful item plus an out-of-stock one together, got the 409, and
  confirmed the plentiful item's stock was rolled back rather than left short.
- Expiry: re-ran with a 30-second TTL and a 10-second sweep, watched the worker log the expirations,
  the status flip, the stock come back, and a later release correctly fail with 409.
- Messaging: bound a `test.sink` queue to the topic exchange in the RabbitMQ UI and confirmed the
  created/released/expired messages landed with the right payloads and routing keys.
- Cache: poked at `inventory:all` in `redis-cli`, confirmed it fills on read, carries the ~30s TTL,
  and gets cleared right after a mutation.

A few of the fixes above came straight out of this loop. I'd reproduce the problem, hand the model
the exact symptom and context, and then re-run the same scenario to confirm the fix held.

## What I take from it

I owned the architecture and the final say; the AI handled the typing. Every rejected approach and
every bug above was either my call or something one of my reviews surfaced. Writing the rules down
first is what kept five projects and a frontend coherent. And checking in layers mattered, because
tests, a second model, and actually running the thing each caught a different kind of problem. The
ones that only turned up at runtime are the reason I don't ship code I haven't run.
