using ModelContextProtocol.ExtensionMethods;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests.ExtensionMethods;

public class ElicitResultExtensionsTests
{
    [Fact]
    public void IsAccepted_Throws_ArgumentNullException_If_Result_Is_Null()
    {
        // Arrange
        ElicitResult? result = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => result!.IsAccepted());
    }

    [Fact]
    public void IsDeclined_Throws_ArgumentNullException_If_Result_Is_Null()
    {
        // Arrange
        ElicitResult? result = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => result!.IsDeclined());
    }

    [Fact]
    public void IsCancelled_Throws_ArgumentNullException_If_Result_Is_Null()
    {
        // Arrange
        ElicitResult? result = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => result!.IsCancelled());
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("ACCEPT")]
    [InlineData("Accept")]
    [InlineData("AccEpt")]
    public void IsAccepted_Returns_True_For_VariousActions(string action)
    {
        // Arrange
        var result = new ElicitResult { Action = action };

        // Act & Assert
        Assert.True(result.IsAccepted());
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    [InlineData("unknown")]
    public void IsAccepted_Returns_False_For_NonAcceptedActions(string action)
    {
        // Arrange
        var result = new ElicitResult { Action = action };

        // Act & Assert
        Assert.False(result.IsAccepted());
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("cancel")]
    [InlineData("unknown")]
    public void IsDeclined_Returns_False_For_NonDeclinedActions(string action)
    {
        // Arrange
        var result = new ElicitResult { Action = action };

        // Act & Assert
        Assert.False(result.IsDeclined());
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("decline")]
    [InlineData("unknown")]
    public void IsCancelled_Returns_False_For_NonCancelledActions(string action)
    {
        // Arrange
        var result = new ElicitResult { Action = action };

        // Act & Assert
        Assert.False(result.IsCancelled());
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("DECLINE")]
    [InlineData("Decline")]
    [InlineData("DecLine")]
    public void IsDeclined_Returns_True_For_VariousActions(string action)
    {
        // Arrange
        var result = new ElicitResult { Action = action };

        // Act & Assert
        Assert.True(result.IsDeclined());
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("CANCEL")]
    [InlineData("Cancel")]
    [InlineData("CanCel")]
    public void IsCancelled_Returns_True_For_VariousActions(string action)
    {
        // Arrange
        var result = new ElicitResult { Action = action };

        // Act & Assert
        Assert.True(result.IsCancelled());
    }
}