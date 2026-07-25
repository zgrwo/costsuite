using Xunit;

namespace BomAddIn.UnitTests;

/// <summary>
/// Collection fixture that serializes all UDF tests.
/// UDF functions use the static Container class, which holds a single IServiceProvider.
/// Tests reconfigure the container per-test-class, so parallel execution would cause races.
/// </summary>
[CollectionDefinition("UDF")]
public class UdfTestCollection
{
}
