using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreMcpPerSessionTools.Tools;

/// <summary>
/// Calculator tools for mathematical operations
/// </summary>
[McpServerToolType]
public sealed class CalculatorTool
{
    [McpServerTool, Description("Performs basic arithmetic calculations (addition, subtraction, multiplication, division).")]
    public static string Calculate([Description("Mathematical expression to evaluate (e.g., '5 + 3', '10 - 2', '4 * 6', '15 / 3')")] string expression)
    {
        try
        {
            // Simple calculator for demo purposes - supports basic operations
            expression = expression.Trim();
            
            if (expression.Contains("+"))
            {
                var parts = expression.Split('+');
                if (parts.Length == 2 && double.TryParse(parts[0].Trim(), out var a) && double.TryParse(parts[1].Trim(), out var b))
                {
                    return $"{expression} = {a + b}";
                }
            }
            else if (expression.Contains("-"))
            {
                var parts = expression.Split('-');
                if (parts.Length == 2 && double.TryParse(parts[0].Trim(), out var a) && double.TryParse(parts[1].Trim(), out var b))
                {
                    return $"{expression} = {a - b}";
                }
            }
            else if (expression.Contains("*"))
            {
                var parts = expression.Split('*');
                if (parts.Length == 2 && double.TryParse(parts[0].Trim(), out var a) && double.TryParse(parts[1].Trim(), out var b))
                {
                    return $"{expression} = {a * b}";
                }
            }
            else if (expression.Contains("/"))
            {
                var parts = expression.Split('/');
                if (parts.Length == 2 && double.TryParse(parts[0].Trim(), out var a) && double.TryParse(parts[1].Trim(), out var b))
                {
                    if (b == 0)
                        return "Error: Division by zero";
                    return $"{expression} = {a / b}";
                }
            }
            
            return $"Cannot evaluate expression: {expression}. Supported operations: +, -, *, / (e.g., '5 + 3')";
        }
        catch (Exception ex)
        {
            return $"Error evaluating '{expression}': {ex.Message}";
        }
    }

    [McpServerTool, Description("Calculates percentage of a number.")]
    public static string CalculatePercentage(
        [Description("The number to calculate percentage of")] double number,
        [Description("The percentage value")] double percentage)
    {
        var result = (number * percentage) / 100;
        return $"{percentage}% of {number} = {result}";
    }

    [McpServerTool, Description("Calculates the square root of a number.")]
    public static string SquareRoot([Description("The number to find square root of")] double number)
    {
        if (number < 0)
            return "Error: Cannot calculate square root of negative number";
        
        var result = Math.Sqrt(number);
        return $"√{number} = {result}";
    }
}