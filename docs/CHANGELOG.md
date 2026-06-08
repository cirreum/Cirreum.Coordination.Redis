# Changelog

All notable changes to **Cirreum.Coordination.Redis** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added

- Initial release of **Cirreum.Coordination.Redis** — the Redis-backed adapter for the coordination
  primitives defined in `Cirreum.Coordination`.
- `RedisReplayGuard` implements `IReplayGuard` via an atomic `SET key "1" NX PX <ttl>` — exactly one caller
  claims a token while the claim is live; the key auto-expires at the PX deadline, re-opening the token. The
  token is SHA-256 hashed into the key, so raw values never land in Redis. Coordinates replay protection
  across every instance sharing the connection.
- `RedisRequestThrottle` implements `IRequestThrottle` via a server-side Lua script that `INCR`s the
  window counter and `PEXPIRE`s it on the first hit — or re-arms it whenever the key is found without an
  expiry, so a key that ever loses its TTL self-heals rather than staying stuck over the limit — giving a
  fixed, non-sliding window, and returns the post-increment count plus the re-read `PTTL` in a single
  atomic round trip (lost-update-free).
- `services.AddCoordination(c => c.UseRedis())` — the `UseRedis()` selector on `CoordinationBuilder`.
  Registers both primitives as singletons (replacing any prior backend so the last `UseXxx()` wins).
- **Blind adapter**: owns no connection configuration; resolves the application-provided
  `StackExchange.Redis.IConnectionMultiplexer` from DI so it reuses the same connection the rest of your app
  (for example a distributed cache tier) already uses.
- Fail-closed input validation: non-positive TTL / window / limit and null / blank token / key are rejected
  before any Redis call.
- Fail-safe throttle backoff (defense-in-depth alongside the script's self-heal): should a negative `PTTL`
  ever reach the client logic, `RetryAfter` advises a full-window backoff rather than "retry now", so an
  anomalous stuck window cannot drive a client hot loop.
