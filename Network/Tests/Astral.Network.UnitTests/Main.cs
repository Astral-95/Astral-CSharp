
[assembly: CollectionBehavior(DisableTestParallelization = true)]
//[assembly: CollectionBehavior(MaxParallelThreads = 1)]

namespace Astral.UnitTests
{
	[CollectionDefinition("DisableParallelizationCollection", DisableParallelization = true)]
	public class DisableParallelizationCollection { }

	[CollectionDefinition("SerializeCollection", DisableParallelization = true)]
	public class SerializeCollection { }
}
