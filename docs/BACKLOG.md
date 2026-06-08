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
- **Noted:** 2026-06-06
- `Cirreum.Coordination`'s `CoordinationPostureValidator` already fails fast when coordination was *pulled*
  but no backend was chosen. A missing `IConnectionMultiplexer` is the next gap: the Redis backend *was*
  chosen, but its dependency is absent, and today that surfaces only on first use via lazy resolution.
  `UseRedis()` could additionally assert the multiplexer is registered at composition time so the
  misconfiguration fails fast at startup rather than on the first replay / throttle call.
