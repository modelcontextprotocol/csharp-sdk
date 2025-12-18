using Microsoft.Extensions.AI;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Regression tests for specific issues that were reported and fixed.
/// </summary>
public class RegressionTests
{
    /// <summary>
    /// Regression test for GitHub issue: ToJsonObject fails when dictionary values contain anonymous types.
    /// This is a sampling pipeline regression from version 0.5.0-preview.1.
    /// </summary>
    [Fact]
    public void Issue_AnonymousTypes_InAdditionalProperties_ShouldNotThrow()
    {
        // Exact minimal repro from the issue
        AIContent c = new()
        {
            AdditionalProperties = new()
            {
                ["data"] = new { X = 1.0, Y = 2.0 }
            }
        };

        // This should not throw NotSupportedException
        var exception = Record.Exception(() => c.ToContentBlock());
        
        Assert.Null(exception);
    }
}
