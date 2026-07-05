namespace Cirreum.Coordination.Redis;

using Cirreum.Coordination;
using StackExchange.Redis;

/// <summary>
/// Redis-backed <see cref="IRequestThrottle"/>. A fixed-window counter evaluated atomically by a server-side
/// Lua script: <c>INCR</c> the key, and on the first hit (<c>current == 1</c>) anchor the window with
/// <c>PEXPIRE</c> so it does not slide; the script returns the post-increment count and the remaining
/// window (<c>PTTL</c>) in one round trip, so the count is lost-update-free across every instance sharing
/// the connection. Keys carry the registered <see cref="CoordinationScope"/> when one is present, so
/// applications and environments sharing an instance never share windows — a colliding key string (an IP
/// address, say) counts separately per scope. Blind: it owns no connection configuration, resolving the
/// application-provided <see cref="IConnectionMultiplexer"/> from DI.
/// </summary>
internal sealed class RedisRequestThrottle(IConnectionMultiplexer connection, CoordinationScope? scope = null) : IRequestThrottle {

	private readonly string _keyPrefix = RedisCoordinationKeySpace.RootFor(scope) + "throttle:";

	// Fixed window, evaluated atomically server-side. INCR is atomic; PEXPIRE (re)arms the window only on the
	// first hit (current == 1) OR whenever the key is found without an expiry (ttl < 0) — so the window is
	// anchored at the first request and never slides, yet a key that ever loses its TTL (an out-of-band
	// anomaly the INCR+PEXPIRE path should preclude) self-heals to a fresh window instead of staying stuck
	// over the limit forever. The ttl is re-read after PEXPIRE so the returned value is accurate; returning
	// count + ttl in one script keeps the whole record-and-read atomic — no read-modify-write race.
	private const string FixedWindowScript =
		"""
		local current = redis.call('INCR', KEYS[1])
		local ttl = redis.call('PTTL', KEYS[1])

		if current == 1 or ttl < 0 then
			redis.call('PEXPIRE', KEYS[1], ARGV[1])
			ttl = redis.call('PTTL', KEYS[1])
		end

		return { current, ttl }
		""";

	/// <inheritdoc />
	public async ValueTask<ThrottleOutcome> RecordAsync(string key, TimeSpan window, long limit, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		if (window <= TimeSpan.Zero) {
			throw new ArgumentOutOfRangeException(nameof(window), window, "Throttle window must be positive.");
		}
		if (limit <= 0) {
			throw new ArgumentOutOfRangeException(nameof(limit), limit, "Throttle limit must be positive.");
		}

		cancellationToken.ThrowIfCancellationRequested();

		var windowMilliseconds = (long)Math.Ceiling(window.TotalMilliseconds);
		var database = connection.GetDatabase();

		var raw = await database
			.ScriptEvaluateAsync(FixedWindowScript, [this._keyPrefix + key], [(RedisValue)windowMilliseconds])
			.ConfigureAwait(false);

		var result = (RedisResult[]?)raw
			?? throw new InvalidOperationException("The throttle script returned an unexpected (non-array) result.");

		if (result.Length != 2) {
			throw new InvalidOperationException($"The throttle script returned {result.Length} values; expected 2 (count, ttl).");
		}

		var count = (long)result[0];                 // post-increment hit count in the current window
		var remainingMilliseconds = (long)result[1]; // PTTL of the window key, in milliseconds

		if (count <= limit) {
			return new ThrottleOutcome(count, Allowed: true, RetryAfter: null);
		}

		// RetryAfter from the window key's PTTL. Positive is the normal case; exactly 0 means the window is
		// resetting this instant (retry immediately is correct). The script self-heals a missing expiry, so a
		// negative PTTL should never reach here — as defense-in-depth we still fail safe with a full-window
		// backoff rather than advising "retry now", which would hot-loop against a window stuck over the limit.
		var retryAfter = remainingMilliseconds switch {
			> 0 => TimeSpan.FromMilliseconds(remainingMilliseconds),
			0 => TimeSpan.Zero,
			_ => TimeSpan.FromMilliseconds(windowMilliseconds),
		};
		return new ThrottleOutcome(count, Allowed: false, retryAfter);
	}

}
