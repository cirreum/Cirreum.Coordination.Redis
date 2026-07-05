# Backlog

Deferred work for **Cirreum.Coordination.Redis**. Items here are tracked but not yet ready
to ship — either because the cost outweighs the benefit in isolation, or because they're waiting on a
forcing function (a related change, a consumer upgrade, a coordinated multi-repo rollout).

## How this file works

- Each item is a `###` heading so it can be linked to and parsed.
- Each item declares **`SemVer:`** (`Patch` | `Minor` | `Major` | `Unspecified`),
  **`Trigger:`** (the human-readable condition that will make it ready), and
  **`Noted:`** (the date the item was added).
- The Cirreum DevOps release scripts (`PatchRelease`, `MinorRelease`, `MajorRelease`) surface items
  at-or-below the requested bump level so the operator can decide whether to fold them in before tagging.
- Items that ship: move from this file to `docs/CHANGELOG.md` under `[Unreleased]`. Items that grow into
  design discussions: promote to an ADR.

## Queued

### Live-Redis integration test pass

- **SemVer:** Unspecified
- **Trigger:** A CI service container (or Testcontainers) for Redis is wired into the test pipeline.
- **Noted:** 2026-06-06
- The shipped unit tests mock `IDatabase` to verify the adapter's wiring and result interpretation; they
  do not exercise a real server. Add an integration suite (skipped when no `REDIS_TEST_CONNECTION` is
  present) asserting true end-to-end atomicity: concurrent `SET NX` admitting exactly one winner, the Lua
  fixed-window counter enforcing the limit across connections, and PX/PEXPIRE expiry releasing claims.

### Boot-time fail-fast when UseRedis() has no IConnectionMultiplexer

- **SemVer:** Minor
- **Trigger:** A consumer wants startup validation that the Redis backend's connection is registered.
- **Noted:** 2026-06-06 (revised 2026-07-05 for the `connectionKey` overload shipped in 1.1.0)
- `Cirreum.Coordination`'s `CoordinationPostureValidator` already fails fast when coordination was *pulled*
  but no backend was chosen. A missing `IConnectionMultiplexer` is the next gap: the Redis backend *was*
  chosen, but its dependency is absent, and today that surfaces only when a primitive is first resolved
  from DI (the 1.1.0 factory registrations resolve the multiplexer — unkeyed, or keyed when
  `UseRedis(connectionKey)` was used — at that point, and the unregistered-key case is covered by an
  explicit fails-fast-at-first-resolution test). The remaining window is lazily-resolved consumers only.
- Design note: the check does NOT belong inside `UseRedis()` at composition time — apps may legitimately
  register the multiplexer after calling `AddCoordination`, and the keyed variant must match on
  `ServiceKey` too. It belongs in a post-composition validation phase (the `CoordinationPostureValidator`
  timing), which means either a Redis-side validator the runtime invokes alongside the posture validator,
  or a validation seam in `Cirreum.Coordination` that backends contribute to. Needs that seam designed
  before this can ship.
