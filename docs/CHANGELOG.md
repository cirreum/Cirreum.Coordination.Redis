# Changelog

All notable changes to **Cirreum.Coordination.Redis** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

Updated NuGet packages (Cirreum spine 4.0.1 wave: Contracts 4.0.1 / Domain 4.0.1 / Kernel 2.0.1 / AuthenticationProvider 2.0.3).

## [1.1.2] - 2026-07-30

### Updated

- Updated `StackExchange.Redis` `3.0.17` → `3.0.25`.

## [1.1.1] - 2026-07-19

### Updated

- Updated NuGet packages.

## [1.1.0] - 2026-07-05

### Added

- `RedisSignalBroadcaster` implements `ISignalBroadcaster` (the third coordination primitive, new in
  `Cirreum.Coordination 1.1.0`) over Redis pub/sub (`ISubscriber`). Publishes to
  `cirreum:coordination:signal:{channel}` with native pub/sub semantics — at-most-once, unbuffered, to
  currently-connected subscribers only — matching the contract's live-signal (not durable message) charter.
  Each subscription drains through its own sequential pump (a single-reader queue fed from the Redis
  callback), so the handler runs one signal at a time, in arrival order, off the Redis callback thread, and
  a faulted handler is isolated per-signal — it can never tear down the subscription.
- `UseRedis(string? connectionKey = null)` — optional keyed-DI connection selection. When a key is passed,
  all three primitives resolve the `IConnectionMultiplexer` registered under that key
  (`AddKeyedSingleton<IConnectionMultiplexer>(key, ...)`) instead of the default registration, so
  coordination traffic can ride a dedicated, differently-credentialed Redis connection rather than sharing
  the app's default one — whoever can publish on the coordination connection can forge its signals (for
  example auth-event delivery), so it can warrant its own trust boundary. The adapter stays blind either
  way: it still owns no connection string or configuration. Omitting the key keeps today's behavior
  exactly; a blank (empty / whitespace) key is rejected at composition time, and an unregistered key fails
  fast at first resolution.
- **`CoordinationScope` support** (new in `Cirreum.Coordination 1.2.0`): when a scope is registered
  (`c.WithScope("MyApp", "Production")`), every key and channel is namespaced under it —
  `cirreum:coordination:{scope}:replay|throttle|signal:...` — so applications and environments sharing one
  Redis instance never share claims, windows, or signals. The scope sits directly after the keyspace root,
  so one backend-side access rule per identity covers everything its application touches (key glob
  `~cirreum:coordination:MyApp:Production:*`, channel pattern
  `&cirreum:coordination:MyApp:Production:signal:*`). Both the scope and the connection resolve at first
  use, so `UseRedis()` / `WithScope()` / multiplexer registration compose in any order. No scope → the
  unscoped 1.0.0 layout, unchanged.

### Changed

- `UseRedis()` now registers all three coordination primitives (previously two): `IReplayGuard`,
  `IRequestThrottle`, and the new `ISignalBroadcaster`.
- `Cirreum.Coordination` dependency: `1.0.0` → `1.2.0` (the source of the `ISignalBroadcaster` contract
  and the `CoordinationScope` concept).
- A scoped deployment rolls its replay/throttle keys to the scoped layout on upgrade: during a rolling
  deploy, old replicas claim under unscoped keys while new ones use scoped keys, so the replay and
  throttle windows briefly don't see each other across old/new replicas. All of this state is
  TTL-ephemeral; the windows converge as soon as the rollout completes.

## [1.0.0] - 2026-07-03

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
