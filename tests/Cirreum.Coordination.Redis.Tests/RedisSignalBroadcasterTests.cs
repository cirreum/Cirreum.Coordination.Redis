namespace Cirreum.Coordination.Redis.Tests;

using StackExchange.Redis;
using System.Text;

public sealed class RedisSignalBroadcasterTests {

	private static readonly RedisChannel PrefixedChannel =
		RedisChannel.Literal("cirreum:coordination:signal:auth-events");

	private static (RedisSignalBroadcaster broadcaster, ISubscriber subscriber) Build(CoordinationScope? scope = null) {
		var subscriber = Substitute.For<ISubscriber>();
		var connection = Substitute.For<IConnectionMultiplexer>();
		connection.GetSubscriber(Arg.Any<object?>()).Returns(subscriber);
		return (new RedisSignalBroadcaster(connection, scope), subscriber);
	}

	[Fact]
	public async Task Publish_sends_the_payload_to_the_prefixed_literal_channel() {
		var (broadcaster, subscriber) = Build();
		RedisValue sent = default;
		subscriber
			.PublishAsync(Arg.Any<RedisChannel>(), Arg.Do<RedisValue>(v => sent = v), Arg.Any<CommandFlags>())
			.Returns(1L);
		var payload = Encoding.UTF8.GetBytes("signal");

		await broadcaster.PublishAsync("auth-events", payload);

		await subscriber.Received(1).PublishAsync(PrefixedChannel, Arg.Any<RedisValue>(), Arg.Any<CommandFlags>());
		((byte[]?)sent).Should().Equal(payload);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task Publish_with_a_null_or_blank_channel_is_rejected_without_touching_redis(string? channel) {
		var (broadcaster, subscriber) = Build();

		var act = async () => await broadcaster.PublishAsync(channel!, ReadOnlyMemory<byte>.Empty);

		await act.Should().ThrowAsync<ArgumentException>();
		await subscriber.DidNotReceive().PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task Publish_honors_a_pre_canceled_token_without_touching_redis() {
		var (broadcaster, subscriber) = Build();
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		var act = async () => await broadcaster.PublishAsync("auth-events", ReadOnlyMemory<byte>.Empty, cts.Token);

		await act.Should().ThrowAsync<OperationCanceledException>();
		await subscriber.DidNotReceive().PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task A_registered_scope_namespaces_the_publish_channel() {
		var (broadcaster, subscriber) = Build(CoordinationScope.For("MyApp", "Production"));
		subscriber
			.PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
			.Returns(1L);

		await broadcaster.PublishAsync("auth-events", ReadOnlyMemory<byte>.Empty);

		await subscriber.Received(1).PublishAsync(
			RedisChannel.Literal("cirreum:coordination:MyApp:Production:signal:auth-events"),
			Arg.Any<RedisValue>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task A_registered_scope_namespaces_the_subscribe_channel() {
		var (broadcaster, subscriber) = Build(CoordinationScope.For("MyApp", "Production"));

		await broadcaster.SubscribeAsync("auth-events", (_, _) => ValueTask.CompletedTask);

		await subscriber.Received(1).SubscribeAsync(
			RedisChannel.Literal("cirreum:coordination:MyApp:Production:signal:auth-events"),
			Arg.Any<Action<RedisChannel, RedisValue>>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task Subscribe_registers_on_the_prefixed_literal_channel() {
		var (broadcaster, subscriber) = Build();

		await broadcaster.SubscribeAsync("auth-events", (_, _) => ValueTask.CompletedTask);

		await subscriber.Received(1).SubscribeAsync(PrefixedChannel, Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>());
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task Subscribe_with_a_null_or_blank_channel_is_rejected_without_touching_redis(string? channel) {
		var (broadcaster, subscriber) = Build();

		var act = async () => await broadcaster.SubscribeAsync(channel!, (_, _) => ValueTask.CompletedTask);

		await act.Should().ThrowAsync<ArgumentException>();
		await subscriber.DidNotReceive().SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>(), Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task Subscribe_with_a_null_handler_is_rejected() {
		var (broadcaster, _) = Build();

		var act = async () => await broadcaster.SubscribeAsync("auth-events", null!);

		await act.Should().ThrowAsync<ArgumentNullException>();
	}

	[Fact]
	public async Task A_received_signal_reaches_the_handler_with_its_payload() {
		var (broadcaster, subscriber) = Build();
		Action<RedisChannel, RedisValue>? callback = null;
		subscriber
			.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Do<Action<RedisChannel, RedisValue>>(a => callback = a), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

		await broadcaster.SubscribeAsync("auth-events", (payload, _) => {
			received.TrySetResult(payload.ToArray());
			return ValueTask.CompletedTask;
		});
		callback!.Invoke(PrefixedChannel, Encoding.UTF8.GetBytes("signal"));

		var seen = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
		seen.Should().Equal(Encoding.UTF8.GetBytes("signal"));
	}

	[Fact]
	public async Task Signals_are_delivered_one_at_a_time_in_arrival_order() {
		var (broadcaster, subscriber) = Build();
		Action<RedisChannel, RedisValue>? callback = null;
		subscriber
			.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Do<Action<RedisChannel, RedisValue>>(a => callback = a), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var order = new List<string>();
		var inFlight = 0;
		var overlapped = false;
		var all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		await broadcaster.SubscribeAsync("auth-events", async (payload, _) => {
			if (Interlocked.Increment(ref inFlight) > 1) {
				overlapped = true;
			}
			await Task.Yield();
			lock (order) {
				order.Add(Encoding.UTF8.GetString(payload.Span));
				if (order.Count == 3) {
					all.TrySetResult();
				}
			}
			Interlocked.Decrement(ref inFlight);
		});
		callback!.Invoke(PrefixedChannel, Encoding.UTF8.GetBytes("first"));
		callback.Invoke(PrefixedChannel, Encoding.UTF8.GetBytes("second"));
		callback.Invoke(PrefixedChannel, Encoding.UTF8.GetBytes("third"));

		await all.Task.WaitAsync(TimeSpan.FromSeconds(5));
		order.Should().Equal("first", "second", "third");
		overlapped.Should().BeFalse();
	}

	[Fact]
	public async Task A_faulted_handler_does_not_tear_down_the_subscription() {
		var (broadcaster, subscriber) = Build();
		Action<RedisChannel, RedisValue>? callback = null;
		subscriber
			.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Do<Action<RedisChannel, RedisValue>>(a => callback = a), Arg.Any<CommandFlags>())
			.Returns(Task.CompletedTask);
		var survived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

		await broadcaster.SubscribeAsync("auth-events", (payload, _) => {
			if (Encoding.UTF8.GetString(payload.Span) == "poison") {
				throw new InvalidOperationException("handler fault");
			}
			survived.TrySetResult(payload.ToArray());
			return ValueTask.CompletedTask;
		});
		callback!.Invoke(PrefixedChannel, Encoding.UTF8.GetBytes("poison"));
		callback.Invoke(PrefixedChannel, Encoding.UTF8.GetBytes("after"));

		var seen = await survived.Task.WaitAsync(TimeSpan.FromSeconds(5));
		seen.Should().Equal(Encoding.UTF8.GetBytes("after"));
	}

}
