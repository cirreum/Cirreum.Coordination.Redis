namespace Cirreum.Coordination.Redis;

using Cirreum.Coordination;

/// <summary>
/// Builds the keyspace root every adapter in this package prefixes its keys and channels with:
/// <c>cirreum:coordination:</c> unscoped, or <c>cirreum:coordination:{scope}:</c> when a
/// <see cref="CoordinationScope"/> is registered. The scope sits directly after the root so one
/// backend-side access rule covers everything a scoped application touches — key glob
/// <c>~cirreum:coordination:MyApp:Production:*</c>, channel pattern
/// <c>&amp;cirreum:coordination:MyApp:Production:signal:*</c>.
/// </summary>
internal static class RedisCoordinationKeySpace {

	private const string Root = "cirreum:coordination:";

	internal static string RootFor(CoordinationScope? scope) =>
		scope is null ? Root : $"{Root}{scope.Value}:";

}
