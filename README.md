# Cirreum.Coordination.Redis

[![NuGet Version](https://img.shields.io/nuget/v/Cirreum.Coordination.Redis.svg?style=flat-square&labelColor=1F1F1F&color=003D8F)](https://www.nuget.org/packages/Cirreum.Coordination.Redis/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Cirreum.Coordination.Redis.svg?style=flat-square&labelColor=1F1F1F&color=003D8F)](https://www.nuget.org/packages/Cirreum.Coordination.Redis/)
[![GitHub Release](https://img.shields.io/github/v/release/cirreum/Cirreum.Coordination.Redis?style=flat-square&labelColor=1F1F1F&color=FF3B2E)](https://github.com/cirreum/Cirreum.Coordination.Redis/releases)
[![License](https://img.shields.io/badge/license-MIT-F2F2F2?style=flat-square&labelColor=1F1F1F)](https://github.com/cirreum/Cirreum.Coordination.Redis/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-003D8F?style=flat-square&labelColor=1F1F1F)](https://dotnet.microsoft.com/)

**Redis backend for Cirreum.Coordination — distributed, multi-instance replay protection, rate limiting, and live signals**

## Overview

**Cirreum.Coordination.Redis** is the distributed backend for **Cirreum.Coordination**. It implements the
coordination primitives over an application-provided
[StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/) connection, so they coordinate
across **every instance** sharing the connection (unlike the in-memory default):

- **`IReplayGuard`** — an atomic `SET key value NX PX ttl`: exactly one caller wins per token while the claim
  is live, and Redis evicts the key at the PX deadline. The token is SHA-256 hashed into the key, so raw
  values never land in Redis.
- **`IRequestThrottle`** — a server-side Lua fixed-window counter (`INCR`, then `PEXPIRE` on the first hit so
  the window is anchored, not sliding; self-heals a key found without an expiry), returning the count +
  remaining TTL in one atomic round trip.
- **`ISignalBroadcaster`** — Redis pub/sub on `cirreum:coordination:signal:{channel}`: at-most-once,
  unbuffered live signals to currently-connected subscribers only (a nudge, not a durable message). Each
  subscription drains through its own sequential pump, so the handler runs one signal at a time, in arrival
  order, and a faulted handler never tears down the subscription.

It is **blind**: it owns no connection configuration, resolving the application-provided
`IConnectionMultiplexer` from DI — by default the same connection the rest of your app already uses, or a
dedicated keyed one via `UseRedis(connectionKey)`.

## Installation

```bash
dotnet add package Cirreum.Coordination.Redis
```

## Usage

Register a multiplexer once (shared across your app), then select the Redis backend:

```csharp
var multiplexer = ConnectionMultiplexer.Connect("your-redis-connection");
builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);

builder.Services.AddCoordination(c => c.UseRedis());
```

If no `IConnectionMultiplexer` is registered, resolution fails fast the first time a coordination primitive is
used.

### Scoping (sharing one instance across applications and environments)

A Redis instance is one flat keyspace and one flat pub/sub namespace. Register a `CoordinationScope` and
every key and channel is namespaced under it, so applications and environments sharing an instance never
share state — a Test deployment cannot touch Production's claims, windows, or signals:

```csharp
builder.Services.AddCoordination(c => c
    .UseRedis()
    .WithScope("MyApp", "Production"));   // canonical {app}:{env}
```

The scope sits directly after the keyspace root, so one access rule per identity covers everything its
application touches:

```text
cirreum:coordination:MyApp:Production:replay:{sha256}
cirreum:coordination:MyApp:Production:throttle:{key}
cirreum:coordination:MyApp:Production:signal:{channel}
```

With Redis ACLs (or Azure Managed Redis custom access policies), that's a key glob
`~cirreum:coordination:MyApp:Production:*` plus a channel pattern
`&cirreum:coordination:MyApp:Production:signal:*` — an identity pinned to its own application and
environment. Without a scope, the unscoped layout (`cirreum:coordination:replay:` etc.) applies; don't
share an instance across applications in that mode. Cirreum's authentication composition provides the
canonical `{ApplicationName}:{EnvironmentName}` scope automatically when none is registered.

### Isolating the coordination connection

Coordination traffic can ride its own, differently-credentialed connection instead of sharing the app's
default one — whoever can publish on the coordination connection can forge its signals, so it can warrant
its own trust boundary:

```csharp
builder.Services.AddKeyedSingleton<IConnectionMultiplexer>(
    "coordination",
    (_, _) => ConnectionMultiplexer.Connect("your-coordination-connection"));

builder.Services.AddCoordination(c => c.UseRedis("coordination"));
```

## Requirements

- .NET 10.0 or later
- `Cirreum.Coordination` 1.2+
- A reachable Redis (any deployment that supports `SET NX`, `INCR`, `PEXPIRE`, `PTTL` — i.e. any Redis)

## Contribution Guidelines

1. **Be conservative with new abstractions**  
   The API surface must remain stable and meaningful.

2. **Limit dependency expansion**  
   Only add foundational, version-stable dependencies.

3. **Favor additive, non-breaking changes**  
   Breaking changes ripple through the entire ecosystem.

4. **Include thorough unit tests**  
   All primitives and patterns should be independently testable.

5. **Document architectural decisions**  
   Context and reasoning should be clear for future maintainers.

6. **Follow .NET conventions**  
   Use established patterns from Microsoft.Extensions.* libraries.

## Versioning

Cirreum.Coordination.Redis follows [Semantic Versioning](https://semver.org/):

- **Major** - Breaking API changes
- **Minor** - New features, backward compatible
- **Patch** - Bug fixes, backward compatible

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

**Cirreum Foundation Framework**  
*Layered simplicity for modern .NET*
