namespace Cirreum.Coordination.Redis.Tests;

using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;

public sealed class RedisReplayGuardTests {

	private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

	private static (RedisReplayGuard guard, IDatabase db) Build() {
		var db = Substitute.For<IDatabase>();
		var connection = Substitute.For<IConnectionMultiplexer>();
		connection.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
		return (new RedisReplayGuard(connection), db);
	}

	// The code calls the canonical keepTtl overload (the When-only one is a forwarding default-interface
	// method NSubstitute cannot intercept), so the setup/verification target the same 6-arg overload.
	private static void StubSet(IDatabase db, bool result) =>
		db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
			.Returns(result);

	[Fact]
	public async Task First_claim_returns_true_when_set_nx_succeeds() {
		var (guard, db) = Build();
		StubSet(db, result: true);

		(await guard.TryClaimAsync("nonce", Ttl)).Should().BeTrue();
	}

	[Fact]
	public async Task Replayed_token_returns_false_when_set_nx_fails() {
		var (guard, db) = Build();
		StubSet(db, result: false);

		(await guard.TryClaimAsync("nonce", Ttl)).Should().BeFalse();
	}

	[Fact]
	public async Task Claim_issues_set_if_not_exists_with_the_requested_ttl() {
		var (guard, db) = Build();
		StubSet(db, result: true);

		await guard.TryClaimAsync("nonce", TimeSpan.FromSeconds(30));

		// The token is SHA-256 hashed into the key, so raw nonces never land in Redis.
		var expectedKey = (RedisKey)("cirreum:coordination:replay:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("nonce"))));
		await db.Received(1).StringSetAsync(
			expectedKey,
			Arg.Any<RedisValue>(),
			TimeSpan.FromSeconds(30),
			Arg.Any<bool>(),
			When.NotExists,
			Arg.Any<CommandFlags>());
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task Non_positive_ttl_is_rejected_without_touching_redis(int milliseconds) {
		var (guard, db) = Build();

		var act = async () => await guard.TryClaimAsync("x", TimeSpan.FromMilliseconds(milliseconds));

		await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
		await db.DidNotReceive().StringSetAsync(
			Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>());
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task Null_or_blank_token_is_rejected(string? token) {
		var (guard, _) = Build();

		var act = async () => await guard.TryClaimAsync(token!, Ttl);

		await act.Should().ThrowAsync<ArgumentException>();
	}

}
