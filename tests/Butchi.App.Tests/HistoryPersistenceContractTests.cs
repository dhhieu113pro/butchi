using Butchi.App.History;
using Butchi.Core.History;
using Xunit;

namespace Butchi.App.Tests;

public sealed class HistoryPersistenceContractTests
{
    [Fact]
    public void History_store_contract_supports_appending_completed_results()
    {
        var append = typeof(IHistoryStore)
            .GetMethods()
            .SingleOrDefault(method =>
                method.Name == "AppendAsync" &&
                method.GetParameters() is var parameters &&
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(HistoryEntry) &&
                parameters[1].ParameterType == typeof(CancellationToken));

        Assert.NotNull(append);
    }
}
