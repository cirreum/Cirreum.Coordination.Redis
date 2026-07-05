namespace Cirreum.Coordination.Redis.Tests;

using Cirreum.Coordination;
using StackExchange.Redis;

public sealed class RedisRequestThrottleTests {

	private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

	private static (RedisRequestThrottle throttle, IDatabase db) Build(CoordinationScope? scope = null) {
		var db = Substitute.For<IDatabase>();
		var connection = Substitute.For<IConnectionMultiplexer>();
		connection.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
		return (new RedisRequestThrottle(connection, scope), db);
	}

	// The Lua script returns { count, pttl } as a multi-bulk; RedisResult.Create(RedisValue[]) reproduces it.
	private static RedisResult ScriptResult(long count, long pttlMilliseconds) =>
		RedisResult.Create([count, pttlMilliseconds]);

	private static void StubScript(IDatabase db, RedisResult result) =>
		db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
			.Returns(result);

	[Fact]
	public async Task A_hit_within_the_limit_is_allowed() {
		var (throttle, db) = Build();
		StubScript(db, ScriptResult(count: 1, pttlMilliseconds: 60_000));

		(await throttle.RecordAsync("k", Window, 5)).Should().Be(new ThrottleOutcome(1, Allowed: true, RetryAfter: null));
	}

	[Fact]
	public async Task A_count_exactly_at_the_limit_is_allowed() {
		var (throttle, db) = Build();
		StubScript(db, ScriptResult(count: 5, pttlMilliseconds: 12_000));

		(await throttle.RecordAsync("k", Window, 5)).Allowed.Should().BeTrue();
	}

	[Fact]
	public async Task A_hit_over_the_limit_is_throttled_with_retry_after_from_pttl() {
		var (throttle, db) = Build();
		StubScript(db, ScriptResult(count: 6, pttlMilliseconds: 30_000));

		var outcome = await throttle.RecordAsync("k", Window, 5);

		outcome.Allowed.Should().BeFalse();
		outcome.Count.Should().Be(6);
		outcome.RetryAfter.Should().Be(TimeSpan.FromMilliseconds(30_000));
	}

	[Fact]
	public async Task An_over_limit_hit_with_a_negative_pttl_fails_safe_with_a_full_window_backoff() {
		var (throttle, db) = Build();
		StubScript(db, ScriptResult(count: 9, pttlMilliseconds: -1)); // PTTL -1: key with no expiry (anomaly)

		var outcome = await throttle.RecordAsync("k", Window, 5);

		outcome.Allowed.Should().BeFalse();
		// Fail safe: advise a full-window backoff rather than "retry now" (which would hot-loop).
		outcome.RetryAfter.Should().Be(Window);
	}

	[Fact]
	public async Task An_over_limit_hit_with_a_zero_pttl_yields_zero_retry_after() {
		var (throttle, db) = Build();
		StubScript(db, ScriptResult(count: 6, pttlMilliseconds: 0)); // window resetting this instant

		var outcome = await throttle.RecordAsync("k", Window, 5);

		outcome.Allowed.Should().BeFalse();
		outcome.RetryAfter.Should().Be(TimeSpan.Zero);
	}

	[Fact]
	public async Task The_window_key_is_the_prefixed_caller_key() {
		var (throttle, db) = Build();
		RedisKey[]? keys = null;
		db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Do<RedisKey[]>(k => keys = k), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
			.Returns(ScriptResult(count: 1, pttlMilliseconds: 60_000));

		await throttle.RecordAsync("client:abc", Window, 5);

		keys.Should().Equal((RedisKey)"cirreum:coordination:throttle:client:abc");
	}

	[Fact]
	public async Task A_registered_scope_namespaces_the_window_key() {
		var (throttle, db) = Build(CoordinationScope.For("MyApp", "Production"));
		RedisKey[]? keys = null;
		db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Do<RedisKey[]>(k => keys = k), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
			.Returns(ScriptResult(count: 1, pttlMilliseconds: 60_000));

		await throttle.RecordAsync("client:abc", Window, 5);

		keys.Should().Equal((RedisKey)"cirreum:coordination:MyApp:Production:throttle:client:abc");
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public async Task Non_positive_window_is_rejected_without_touching_redis(int milliseconds) {
		var (throttle, db) = Build();

		var act = async () => await throttle.RecordAsync("k", TimeSpan.FromMilliseconds(milliseconds), 5);

		await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
		await db.DidNotReceive().ScriptEvaluateAsync(
			Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>());
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task Non_positive_limit_is_rejected(long limit) {
		var (throttle, _) = Build();

		var act = async () => await throttle.RecordAsync("k", Window, limit);

		await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task Null_or_blank_key_is_rejected(string? key) {
		var (throttle, _) = Build();

		var act = async () => await throttle.RecordAsync(key!, Window, 5);

		await act.Should().ThrowAsync<ArgumentException>();
	}

	[Fact]
	public async Task A_script_result_of_unexpected_shape_is_rejected() {
		var (throttle, db) = Build();
		StubScript(db, RedisResult.Create([1])); // 1 element; the script must return 2

		var act = async () => await throttle.RecordAsync("k", Window, 5);

		await act.Should().ThrowAsync<InvalidOperationException>();
	}

}
