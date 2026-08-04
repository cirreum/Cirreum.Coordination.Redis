namespace Cirreum.Coordination.Redis;

using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

/// <summary>
/// Boot-time posture check contributed by <c>UseRedis()</c>: fails validation when the
/// <see cref="IConnectionMultiplexer"/> the Redis backend resolves lazily is not registered
/// (unkeyed, or under the <c>connectionKey</c> given to <c>UseRedis</c>), so the
/// mis-configuration surfaces at startup instead of on the first coordinated request.
/// </summary>
/// <remarks>
/// Anchored to the backend descriptor <c>UseRedis()</c> registered: when a later
/// <c>UseXxx()</c> call replaces the Redis backend (last one wins), the anchor leaves the
/// collection and the check disarms itself — it never fails an app whose active backend is
/// not Redis.
/// </remarks>
internal sealed class RedisConnectionPostureCheck(
	string? connectionKey,
	ServiceDescriptor backendAnchor
) : ICoordinationPostureCheck {

	/// <inheritdoc/>
	public string? Check(IServiceCollection services) {

		// Redis was replaced by a later backend selection; this posture no longer applies.
		if (!services.Contains(backendAnchor)) {
			return null;
		}

		var registered = connectionKey is null
			? services.Any(d =>
				d.ServiceType == typeof(IConnectionMultiplexer) && !d.IsKeyedService)
			: services.Any(d =>
				d.ServiceType == typeof(IConnectionMultiplexer) && d.IsKeyedService &&
				Equals(d.ServiceKey, connectionKey));

		if (registered) {
			return null;
		}

		return connectionKey is null
			? "The Redis coordination backend was chosen (UseRedis()), but no " +
			  "IConnectionMultiplexer is registered. Register the application's multiplexer " +
			  "(e.g. services.AddSingleton<IConnectionMultiplexer>(...)), or pass UseRedis(connectionKey) " +
			  "to resolve a keyed registration."
			: $"The Redis coordination backend was chosen (UseRedis(\"{connectionKey}\")), but no " +
			  $"IConnectionMultiplexer is registered under service key '{connectionKey}'. Register it via " +
			  $"services.AddKeyedSingleton<IConnectionMultiplexer>(\"{connectionKey}\", ...).";

	}

}
