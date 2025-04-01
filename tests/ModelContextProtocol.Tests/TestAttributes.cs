// Uncomment to disable parallel test execution for the whole assembly
//[assembly: CollectionBehavior(DisableTestParallelization = true)]

/// <summary>
/// Enables test classes to individually be attributed as [Collection(nameof(DisableParallelization))]
/// to have those tests run non-concurrently with any other tests.
/// </summary>
[CollectionDefinition(nameof(DisableParallelization), DisableParallelization = true)]
public sealed class DisableParallelization;