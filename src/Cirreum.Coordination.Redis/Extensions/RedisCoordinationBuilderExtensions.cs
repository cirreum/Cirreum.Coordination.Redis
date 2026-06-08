namespace Cirreum.Coordination;

using Cirreum.Coordination.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Contributes the <c>UseRedis()</c> backend selector to the <see cref="CoordinationBuilder"/>, available
/// inside <c>services.AddCoordination(c =&gt; c.UseRedis())</c>.
/// </summary>
public static class RedisCoordinationBuilderExtensions {

	/// <summary>
	/// Backs the coordination primitives (<see cref="IReplayGuard"/> + <see cref="IRequestThrottle"/>) with
	/// Redis, sharing the application-provided <see cref="StackExchange.Redis.IConnectionMultiplexer"/> from DI.
	/// The adapter is blind — it owns no connection string or configuration, so it reuses the same connection
	/// the rest of your app (for example a distributed cache tier) already uses. The application must register
	/// an <c>IConnectionMultiplexer</c> in DI; resolution fails fast at first use if absent. Replaces any
	/// previously-registered coordination backend, so the last <c>UseXxx()</c> call wins.
	/// </summary>
	/// <param name="builder">The coordination builder from <c>AddCoordination</c>.</param>
	/// <returns>The builder for chaining.</returns>
	public static CoordinationBuilder UseRedis(this CoordinationBuilder builder) {
		ArgumentNullException.ThrowIfNull(builder);

		builder.Services.Replace(ServiceDescriptor.Singleton<IReplayGuard, RedisReplayGuard>());
		builder.Services.Replace(ServiceDescriptor.Singleton<IRequestThrottle, RedisRequestThrottle>());
		return builder;
	}

}
