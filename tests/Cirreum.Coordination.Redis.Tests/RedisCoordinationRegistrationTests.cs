namespace Cirreum.Coordination.Redis.Tests;

using Cirreum.Coordination;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

public sealed class RedisCoordinationRegistrationTests {

	private static ServiceCollection ServicesWithRedis() {
		var services = new ServiceCollection();
		services.AddSingleton(Substitute.For<IConnectionMultiplexer>());
		return services;
	}

	[Fact]
	public void UseRedis_registers_the_redis_backed_primitives() {
		var services = ServicesWithRedis();

		services.AddCoordination(c => c.UseRedis());

		using var provider = services.BuildServiceProvider();
		provider.GetRequiredService<IReplayGuard>().Should().BeOfType<RedisReplayGuard>();
		provider.GetRequiredService<IRequestThrottle>().Should().BeOfType<RedisRequestThrottle>();
		provider.GetRequiredService<ISignalBroadcaster>().Should().BeOfType<RedisSignalBroadcaster>();
	}

	[Fact]
	public void UseRedis_overrides_a_prior_in_memory_registration_last_wins() {
		var services = ServicesWithRedis();

		services.AddCoordination(c => c.UseInMemory().UseRedis());

		using var provider = services.BuildServiceProvider();
		provider.GetRequiredService<IReplayGuard>().Should().BeOfType<RedisReplayGuard>();
		provider.GetRequiredService<IRequestThrottle>().Should().BeOfType<RedisRequestThrottle>();
		provider.GetRequiredService<ISignalBroadcaster>().Should().BeOfType<RedisSignalBroadcaster>();
		services.Count(d => d.ServiceType == typeof(IReplayGuard)).Should().Be(1);
		services.Count(d => d.ServiceType == typeof(IRequestThrottle)).Should().Be(1);
		services.Count(d => d.ServiceType == typeof(ISignalBroadcaster)).Should().Be(1);
	}

	[Fact]
	public void UseRedis_replaces_the_fail_closed_sentinel_when_coordination_was_pulled_first() {
		var services = ServicesWithRedis();

		services.AddCoordination();                  // pull (sentinel)
		services.AddCoordination(c => c.UseRedis()); // choose Redis

		using var provider = services.BuildServiceProvider();
		provider.GetRequiredService<IReplayGuard>().Should().BeOfType<RedisReplayGuard>();
		CoordinationPostureValidator.Validate(services); // no sentinel remains
	}

	[Fact]
	public async Task UseRedis_with_a_connection_key_resolves_the_keyed_multiplexer_not_the_default_one() {
		var services = new ServiceCollection();
		var keyed = Substitute.For<IConnectionMultiplexer>();
		var unkeyed = Substitute.For<IConnectionMultiplexer>();
		services.AddSingleton(unkeyed);
		services.AddKeyedSingleton("auth-events", keyed);

		services.AddCoordination(c => c.UseRedis("auth-events"));

		using var provider = services.BuildServiceProvider();
		var guard = provider.GetRequiredService<IReplayGuard>();
		guard.Should().BeOfType<RedisReplayGuard>();
		// The wiring is only observable behaviorally: first use must hit the keyed connection, never the default.
		await guard.TryClaimAsync("nonce", TimeSpan.FromMinutes(1));
		keyed.Received(1).GetDatabase(Arg.Any<int>(), Arg.Any<object>());
		unkeyed.DidNotReceive().GetDatabase(Arg.Any<int>(), Arg.Any<object>());
	}

	[Fact]
	public async Task WithScope_namespaces_the_keys_end_to_end_regardless_of_call_order() {
		var services = new ServiceCollection();
		var db = Substitute.For<IDatabase>();
		var connection = Substitute.For<IConnectionMultiplexer>();
		connection.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
		services.AddSingleton(connection);

		// WithScope AFTER UseRedis: the factories resolve the scope at first use, so order is irrelevant.
		services.AddCoordination(c => c.UseRedis().WithScope("MyApp", "Production"));

		using var provider = services.BuildServiceProvider();
		await provider.GetRequiredService<IReplayGuard>().TryClaimAsync("nonce", TimeSpan.FromMinutes(1));
		await db.Received(1).StringSetAsync(
			Arg.Is<RedisKey>(k => ((string)k!).StartsWith("cirreum:coordination:MyApp:Production:replay:")),
			Arg.Any<RedisValue>(),
			Arg.Any<TimeSpan?>(),
			Arg.Any<bool>(),
			Arg.Any<When>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public void UseRedis_with_an_unregistered_connection_key_fails_fast_at_first_resolution() {
		var services = ServicesWithRedis(); // default (unkeyed) registration only

		services.AddCoordination(c => c.UseRedis("auth-events"));

		using var provider = services.BuildServiceProvider();
		var act = () => provider.GetRequiredService<IReplayGuard>();
		act.Should().Throw<InvalidOperationException>();
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void UseRedis_with_a_blank_connection_key_throws(string connectionKey) {
		var services = ServicesWithRedis();

		var act = () => services.AddCoordination(c => c.UseRedis(connectionKey));

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void UseRedis_with_a_null_builder_throws() {
		var act = () => ((CoordinationBuilder)null!).UseRedis();

		act.Should().Throw<ArgumentNullException>();
	}

}
