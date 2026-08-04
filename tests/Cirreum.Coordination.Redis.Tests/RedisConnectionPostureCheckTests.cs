namespace Cirreum.Coordination.Redis.Tests;

using Cirreum.Coordination;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

public sealed class RedisConnectionPostureCheckTests {

	[Fact]
	public void Validate_throws_when_UseRedis_has_no_multiplexer_registered() {
		var services = new ServiceCollection();

		services.AddCoordination(c => c.UseRedis());

		var act = () => CoordinationPostureValidator.Validate(services);
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*no IConnectionMultiplexer is registered*");
	}

	[Fact]
	public void Validate_passes_when_the_multiplexer_is_registered_after_UseRedis() {
		var services = new ServiceCollection();

		services.AddCoordination(c => c.UseRedis());
		services.AddSingleton(Substitute.For<IConnectionMultiplexer>());

		var act = () => CoordinationPostureValidator.Validate(services);
		act.Should().NotThrow();
	}

	[Fact]
	public void Validate_throws_when_the_keyed_variant_has_no_matching_keyed_registration() {
		var services = new ServiceCollection();
		services.AddSingleton(Substitute.For<IConnectionMultiplexer>()); // unkeyed only

		services.AddCoordination(c => c.UseRedis("auth-events"));

		var act = () => CoordinationPostureValidator.Validate(services);
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*service key 'auth-events'*");
	}

	[Fact]
	public void Validate_passes_when_the_keyed_variant_finds_its_keyed_registration() {
		var services = new ServiceCollection();
		services.AddKeyedSingleton("auth-events", Substitute.For<IConnectionMultiplexer>());

		services.AddCoordination(c => c.UseRedis("auth-events"));

		var act = () => CoordinationPostureValidator.Validate(services);
		act.Should().NotThrow();
	}

	[Fact]
	public void Validate_passes_when_a_later_backend_replaced_redis_last_wins_disarms_the_check() {
		var services = new ServiceCollection();
		// No multiplexer anywhere — the Redis check would fail if it were still armed.

		services.AddCoordination(c => c.UseRedis().UseInMemory());

		var act = () => CoordinationPostureValidator.Validate(services);
		act.Should().NotThrow();
	}

}
