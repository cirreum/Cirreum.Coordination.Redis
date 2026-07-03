# Cirreum.Coordination.Redis 1.0.0 — the Redis backend

A Redis-backed adapter for the coordination primitives defined in `Cirreum.Coordination`.
Swap the in-memory backend for a distributed one with a single registration call —
`IReplayGuard` and `IRequestThrottle` then coordinate across every instance sharing the
connection.

Strictly additive — initial release.

## Why this release exists

The in-memory coordination backend in `Cirreum.Coordination` is correct for a single
instance, but replay protection and rate limiting only mean something across a fleet when
the state is shared. This package provides that shared state over Redis, using atomic
Redis operations so the guarantees hold under concurrency without client-side locking.

It is a **blind adapter**: it owns no connection configuration and resolves the
application-provided `StackExchange.Redis.IConnectionMultiplexer` from DI, so it reuses the
same connection the rest of your app (e.g. a distributed cache tier) already uses.

## What's new

### `UseRedis()`

```csharp
// The app already registered an IConnectionMultiplexer; select the Redis backend:
services.AddCoordination(c => c.UseRedis());
```

Registers `RedisReplayGuard` + `RedisRequestThrottle` as singletons (replacing any prior
backend — the last `UseXxx()` wins).

### `RedisReplayGuard`

Implements `IReplayGuard` via an atomic `SET key "1" NX PX <ttl>` — exactly one caller
claims a token while the claim is live; the key auto-expires at the PX deadline. The token
is **SHA-256 hashed into the key**, so raw nonce values never land in Redis.

### `RedisRequestThrottle`

Implements `IRequestThrottle` via a server-side Lua script that `INCR`s the window counter
and `PEXPIRE`s it on the first hit (a fixed, non-sliding window), returning the
post-increment count plus the re-read `PTTL` in a single atomic round trip
(lost-update-free). A key ever found **without** an expiry is re-armed, so a window that
loses its TTL self-heals rather than staying stuck over the limit.

## Compatibility

- **Strictly additive.** Initial release.
- **Depends on `Cirreum.Coordination 1.0.0`** and `StackExchange.Redis`.
- **Fail-closed / fail-safe:** non-positive TTL / window / limit and null / blank token /
  key are rejected before any Redis call; an anomalous negative `PTTL` reaching the client
  advises a full-window `RetryAfter` rather than "retry now", so a stuck window can't drive
  a client hot loop.

## See also

- `Cirreum.Coordination` — the neutral coordination contracts + the in-memory backend
