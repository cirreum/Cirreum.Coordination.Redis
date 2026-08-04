# Cirreum.Coordination.Redis 1.2.0 — a missing multiplexer now fails at startup

## Why this release exists

`UseRedis()` is deliberately blind: it owns no connection configuration and resolves the
application's `IConnectionMultiplexer` lazily, when a primitive is first used. The cost of that
design was a silent window — choose the Redis backend, forget the multiplexer registration, and
the mis-configuration surfaced as a runtime failure on the first coordinated request instead of
at boot. The backlog has carried this since June; `Cirreum.Coordination` 1.3.0's
`ICoordinationPostureCheck` seam is what let it ship.

## What's new

**`UseRedis()` contributes a boot-time posture check.** When
`CoordinationPostureValidator.Validate` runs (the authentication umbrella already invokes it
after composition), the check verifies the multiplexer the backend will resolve — unkeyed, or
under the `connectionKey` given to `UseRedis` — is actually registered, and fails with a clear,
actionable error when it is not.

Two details worth knowing:

- **Last-`UseXxx()`-wins is respected.** The check is anchored to the backend registration
  itself: if a later backend selection replaces Redis, the anchor leaves the collection and the
  check disarms — it never fails an app whose active backend is not Redis.
- **Order stays free.** Registering the multiplexer after `AddCoordination(c => c.UseRedis())`
  remains fully supported; the check runs at validation time, not composition time.

## Compatibility

Additive. Apps with a correctly registered multiplexer see no change. Apps that were
mis-configured now fail at startup with an explanation instead of at first coordinated use —
that is the point.

## See also

- `docs/CHANGELOG.md` — the enumerated changes
- `Cirreum.Coordination` 1.3.0 release notes — the `ICoordinationPostureCheck` seam
