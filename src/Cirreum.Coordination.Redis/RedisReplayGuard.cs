namespace Cirreum.Coordination.Redis;

using Cirreum.Coordination;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Redis-backed <see cref="IReplayGuard"/>. Claims a token with an atomic <c>SET key value NX PX ttl</c> —
/// the set succeeds only when the key is absent, so exactly one caller wins per token while the claim is
/// live; Redis evicts the key at the PX deadline, re-opening the token once the window elapses. Coordinates
/// replay protection across every instance sharing the connection (unlike the single-instance in-memory guard).
/// Keys carry the registered <see cref="CoordinationScope"/> when one is present, so applications and
/// environments sharing an instance never share claims. Blind: it owns no connection configuration,
/// resolving the application-provided <see cref="IConnectionMultiplexer"/> from DI.
/// </summary>
internal sealed class RedisReplayGuard(IConnectionMultiplexer connection, CoordinationScope? scope = null) : IReplayGuard {

	private readonly string _keyPrefix = RedisCoordinationKeySpace.RootFor(scope) + "replay:";

	/// <inheritdoc />
	public async ValueTask<bool> TryClaimAsync(
		string token,
		TimeSpan ttl,
		CancellationToken cancellationToken = default) {

		ArgumentException.ThrowIfNullOrWhiteSpace(token);

		if (ttl <= TimeSpan.Zero) {
			throw new ArgumentOutOfRangeException(
				nameof(ttl),
				ttl,
				"Replay claim TTL must be positive.");
		}

		cancellationToken.ThrowIfCancellationRequested();

		var database = connection.GetDatabase();

		return await database
			.StringSetAsync(
				this.ToReplayKey(token),
				"1",
				ttl,
				keepTtl: false,
				when: When.NotExists)
			.ConfigureAwait(false);
	}

	private string ToReplayKey(string token) {
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
		return this._keyPrefix + Convert.ToHexString(hash);
	}

}