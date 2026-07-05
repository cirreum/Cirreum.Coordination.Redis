namespace Cirreum.Coordination.Redis;

using Cirreum.Coordination;
using StackExchange.Redis;
using System.Threading.Channels;

/// <summary>
/// Redis-backed <see cref="ISignalBroadcaster"/> over Redis pub/sub (<see cref="ISubscriber"/>). Publishes
/// to <c>cirreum:coordination:signal:{channel}</c> — or <c>cirreum:coordination:{scope}:signal:{channel}</c>
/// when a <see cref="CoordinationScope"/> is registered, so applications and environments sharing an
/// instance never hear each other's signals — with native pub/sub semantics: at-most-once, unbuffered, to
/// currently-connected subscribers only, matching the contract's live-signal (not durable message) charter.
/// Signals reach every instance sharing the connection (unlike the single-instance in-memory broadcaster).
/// Blind: it owns no connection configuration, resolving the application-provided
/// <see cref="IConnectionMultiplexer"/> from DI.
/// </summary>
/// <remarks>
/// Each subscription drains through its own sequential pump (a single-reader queue fed from the Redis
/// callback): the handler runs one signal at a time, in arrival order, off the Redis callback thread. A
/// faulted handler is isolated per-signal and never tears down the subscription — the adapter is
/// deliberately logger-free (blind), so failure reporting belongs to the handler itself.
/// </remarks>
internal sealed class RedisSignalBroadcaster(IConnectionMultiplexer connection, CoordinationScope? scope = null) : ISignalBroadcaster {

	private readonly string _channelPrefix = RedisCoordinationKeySpace.RootFor(scope) + "signal:";

	/// <inheritdoc />
	public async ValueTask PublishAsync(
		string channel,
		ReadOnlyMemory<byte> payload,
		CancellationToken cancellationToken = default) {

		ArgumentException.ThrowIfNullOrWhiteSpace(channel);
		cancellationToken.ThrowIfCancellationRequested();

		await connection
			.GetSubscriber()
			.PublishAsync(this.ToRedisChannel(channel), payload)
			.ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async ValueTask SubscribeAsync(
		string channel,
		Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> handler,
		CancellationToken cancellationToken = default) {

		ArgumentException.ThrowIfNullOrWhiteSpace(channel);
		ArgumentNullException.ThrowIfNull(handler);
		cancellationToken.ThrowIfCancellationRequested();

		// The pump starts before the Redis subscribe so no signal can arrive without a drain in place; it
		// then lives for the application's lifetime, matching the contract's no-unsubscribe model.
		var queue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
		_ = PumpAsync(queue.Reader, handler);

		await connection
			.GetSubscriber()
			.SubscribeAsync(
				this.ToRedisChannel(channel),
				(_, value) => queue.Writer.TryWrite((byte[]?)value ?? []))
			.ConfigureAwait(false);
	}

	private static async Task PumpAsync(
		ChannelReader<byte[]> reader,
		Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> handler) {

		await foreach (var payload in reader.ReadAllAsync().ConfigureAwait(false)) {
			try {
				await handler(payload, CancellationToken.None).ConfigureAwait(false);
			} catch {
				// A faulted handler is isolated per-signal; the subscription must survive it.
			}
		}
	}

	private RedisChannel ToRedisChannel(string channel) => RedisChannel.Literal(this._channelPrefix + channel);

}
