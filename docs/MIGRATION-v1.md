# Cirreum.Coordination.Redis v1.0.0 — Migration Guide

> **From:** _(no prior version)_ &nbsp;•&nbsp; **To:** v1.0.0

## Why v1

This is the **initial release** of `Cirreum.Coordination.Redis`. There is no earlier
published version, so there is nothing for a consumer to migrate from.

---

## Breaking Changes — Find/Replace Table

None. Initial release.

---

## New Capabilities

See [`docs/RELEASE-NOTES-v1.0.0.md`](RELEASE-NOTES-v1.0.0.md) for the full surface
and usage examples.

---

## Migration Walkthrough

### 1. Add the package reference

```xml
<PackageReference Include="Cirreum.Coordination.Redis" Version="1.0.0" />
```

### 2. Select the Redis backend

```csharp
services.AddCoordination(c => c.UseRedis());
```

The adapter resolves the application-provided `StackExchange.Redis.IConnectionMultiplexer`
from DI — it owns no connection configuration and reuses the connection the rest of your
app already registered.

---

## What Didn't Change

Everything — this is the first release.

---

## Downstream Package Impact

This package extends `Cirreum.Coordination 1.0.0` — the neutral coordination contracts —
with a distributed (Redis) backend. Any consumer of `IReplayGuard` / `IRequestThrottle`
can select it via `UseRedis()` without a code change.
