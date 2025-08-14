using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests.Protocol;

public class ElicitResultTests
{
    [Theory]
    [InlineData("accept")]
    [InlineData("Accept")]
    [InlineData("ACCEPT")]
    [InlineData("AccEpt")]
    public void IsAccepted_Returns_True_For_VariousAcceptedActions(string action)
    {
        // Arrange
        var result = new ElicitResult { Action = action };

        // Act
        var isAccepted = result.IsAccepted;

        // Assert
        Assert.True(isAccepted);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("Decline")]
    [InlineData("DECLINE")]
    [InlineData("DecLine")]
    public void IsDeclined_Returns_True_For_VariousDeclinedActions(string action)
    {
        // Arrange
        var result = new ElicitResult { Action = action };

        // Act
        var isDeclined = result.IsDeclined;

        // Assert
        Assert.True(isDeclined);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("Cancel")]
    [InlineData("CANCEL")]
    [InlineData("CanCel")]
    public void IsCancelled_Returns_True_For_VariousCancelledActions(string action)
    {
        // Arrange
        var result = new ElicitResult { Action = action };

        // Act
        var isCancelled = result.IsCanceled;

        // Assert
        Assert.True(isCancelled);
    }

    [Fact]
    public void IsAccepted_Returns_False_For_DefaultAction()
    {
        // Arrange
        var result = new ElicitResult();

        // Act & Assert
        Assert.False(result.IsAccepted);
    }

    [Fact]
    public void IsDeclined_Returns_False_For_DefaultAction()
    {
        // Arrange
        var result = new ElicitResult();

        // Act & Assert
        Assert.False(result.IsDeclined);
    }

    [Fact]
    public void IsCancelled_Returns_True_For_DefaultAction()
    {
        // Arrange
        var result = new ElicitResult();

        // Act & Assert
        Assert.True(result.IsCanceled);
    }

    [Fact]
    public void IsAccepted_Returns_False_For_Null_Action()
    {
        // Arrange
        var result = new ElicitResult { Action = null! };

        // Act & Assert
        Assert.False(result.IsAccepted);
    }

    [Fact]
    public void IsDeclined_Returns_False_For_Null_Action()
    {
        // Arrange
        var result = new ElicitResult { Action = null! };

        // Act & Assert
        Assert.False(result.IsDeclined);
    }

    [Fact]
    public void IsCancelled_Returns_False_For_Null_Action()
    {
        // Arrange
        var result = new ElicitResult { Action = null! };

        // Act & Assert
        Assert.False(result.IsCanceled);
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("decline")]
    [InlineData("cancel")]
    [InlineData("unknown")]
    public void JsonSerialization_ExcludesJsonIgnoredProperties(string action)
    {
        // Arrange
        var result = new ElicitResult { Action = action };

        // Act
        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.DoesNotContain("IsAccepted", json);
        Assert.DoesNotContain("IsDeclined", json);
        Assert.DoesNotContain("IsCanceled", json);
        Assert.Contains($"\"action\":\"{action}\"", json);
    }

    [Theory]
    [InlineData("accept", true, false, false)]
    [InlineData("decline", false, true, false)]
    [InlineData("cancel", false, false, true)]
    [InlineData("unknown", false, false, false)]
    public void JsonRoundTrip_PreservesActionAndComputedProperties(string action, bool isAccepted, bool isDeclined, bool isCancelled)
    {
        // Arrange
        var result = new ElicitResult { Action = action };

        // Act
        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ElicitResult>(json, McpJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(action, deserialized.Action);
        Assert.Equal(isAccepted, deserialized.IsAccepted);
        Assert.Equal(isDeclined, deserialized.IsDeclined);
        Assert.Equal(isCancelled, deserialized.IsCanceled);
    }
}