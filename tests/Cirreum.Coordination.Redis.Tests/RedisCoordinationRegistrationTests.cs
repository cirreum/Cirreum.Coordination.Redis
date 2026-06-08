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
	}

	[Fact]
	public void UseRedis_overrides_a_prior_in_memory_registration_last_wins() {
		var services = ServicesWithRedis();

		services.AddCoordination(c => c.UseInMemory().UseRedis());

		using var provider = services.BuildServiceProvider();
		provider.GetRequiredService<IReplayGuard>().Should().BeOfType<RedisReplayGuard>();
		provider.GetRequiredService<IRequestThrottle>().Should().BeOfType<RedisRequestThrottle>();
		services.Count(d => d.ServiceType == typeof(IReplayGuard)).Should().Be(1);
		services.Count(d => d.ServiceType == typeof(IRequestThrottle)).Should().Be(1);
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
	public void UseRedis_with_a_null_builder_throws() {
		var act = () => ((CoordinationBuilder)null!).UseRedis();

		act.Should().Throw<ArgumentNullException>();
	}

}
