namespace Cirreum.Coordination;

using Cirreum.Coordination.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

/// <summary>
/// Contributes the <c>UseRedis()</c> backend selector to the <see cref="CoordinationBuilder"/>, available
/// inside <c>services.AddCoordination(c =&gt; c.UseRedis())</c>.
/// </summary>
public static class RedisCoordinationBuilderExtensions {

	/// <summary>
	/// Backs the coordination primitives (<see cref="IReplayGuard"/> + <see cref="IRequestThrottle"/> +
	/// <see cref="ISignalBroadcaster"/>) with Redis, sharing an application-provided
	/// <see cref="IConnectionMultiplexer"/> from DI. The adapter is blind — it owns no connection string or
	/// configuration. By default it reuses the same connection the rest of your app (for example a
	/// distributed cache tier) already uses; pass <paramref name="connectionKey"/> to resolve a keyed
	/// <see cref="IConnectionMultiplexer"/> instead. Either way the application must register the
	/// multiplexer; resolution fails fast at first use if absent. Replaces any previously-registered
	/// coordination backend, so the last <c>UseXxx()</c> call wins. When a <see cref="CoordinationScope"/> is
	/// registered (e.g. via <see cref="CoordinationBuilder.WithScope(string)"/>), every key and channel is
	/// namespaced under it, so applications and environments sharing an instance never share state.
	/// </summary>
	/// <param name="builder">The coordination builder from <c>AddCoordination</c>.</param>
	/// <param name="connectionKey">Optional keyed-DI service key selecting which
	/// <see cref="IConnectionMultiplexer"/> the primitives use (registered via
	/// <c>AddKeyedSingleton&lt;IConnectionMultiplexer&gt;(key, ...)</c>). Coordination traffic can thereby
	/// ride a dedicated, differently-credentialed connection instead of sharing the app's default one —
	/// whoever can publish on the coordination connection can forge its signals, so it can warrant its own
	/// trust boundary. <see langword="null"/> (the default) resolves the app's unkeyed registration.</param>
	/// <returns>The builder for chaining.</returns>
	public static CoordinationBuilder UseRedis(this CoordinationBuilder builder, string? connectionKey = null) {
		ArgumentNullException.ThrowIfNull(builder);
		if (connectionKey is not null) {
			ArgumentException.ThrowIfNullOrWhiteSpace(connectionKey);
		}

		// Both the connection and the scope resolve when the singleton is first used, so UseRedis() /
		// WithScope() / the multiplexer registration compose in any order.
		builder.Services.Replace(ServiceDescriptor.Singleton<IReplayGuard>(
			sp => new RedisReplayGuard(ResolveConnection(sp, connectionKey), sp.GetService<CoordinationScope>())));
		builder.Services.Replace(ServiceDescriptor.Singleton<IRequestThrottle>(
			sp => new RedisRequestThrottle(ResolveConnection(sp, connectionKey), sp.GetService<CoordinationScope>())));
		builder.Services.Replace(ServiceDescriptor.Singleton<ISignalBroadcaster>(
			sp => new RedisSignalBroadcaster(ResolveConnection(sp, connectionKey), sp.GetService<CoordinationScope>())));
		return builder;
	}

	private static IConnectionMultiplexer ResolveConnection(IServiceProvider provider, string? connectionKey) =>
		connectionKey is null
			? provider.GetRequiredService<IConnectionMultiplexer>()
			: provider.GetRequiredKeyedService<IConnectionMultiplexer>(connectionKey);

}
