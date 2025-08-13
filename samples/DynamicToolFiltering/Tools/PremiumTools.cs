using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Security.Cryptography;
using System.Text;

namespace DynamicToolFiltering.Tools;

/// <summary>
/// Premium tools that require subscription or premium access.
/// These tools provide advanced functionality with higher resource usage.
/// </summary>
public class PremiumTools
{
    /// <summary>
    /// Generate cryptographically secure random bytes.
    /// </summary>
    [McpServerTool(Name = "premium_generate_secure_random", Description = "Generate cryptographically secure random bytes")]
    public static async Task<CallToolResult> GenerateSecureRandomAsync(
        [Description("Number of bytes to generate (1-1024)")] int byteCount = 32,
        [Description("Output format: hex, base64, or bytes")] string format = "hex",
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        
        if (byteCount < 1 || byteCount > 1024)
        {
            return CallToolResult.FromError("Byte count must be between 1 and 1024");
        }

        using var rng = RandomNumberGenerator.Create();
        var randomBytes = new byte[byteCount];
        rng.GetBytes(randomBytes);

        var output = format.ToLowerInvariant() switch
        {
            "hex" => Convert.ToHexString(randomBytes).ToLowerInvariant(),
            "base64" => Convert.ToBase64String(randomBytes),
            "bytes" => string.Join(",", randomBytes),
            _ => Convert.ToHexString(randomBytes).ToLowerInvariant()
        };

        var result = new
        {
            ByteCount = byteCount,
            Format = format,
            RandomData = output,
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            Entropy = randomBytes.Length * 8 // bits of entropy
        };

        return CallToolResult.FromContent(
            TextContent.Create($"Secure Random Data: {System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }

    /// <summary>
    /// Perform advanced text analysis with metrics.
    /// </summary>
    [McpServerTool(Name = "premium_analyze_text", Description = "Perform advanced text analysis with detailed metrics")]
    public static async Task<CallToolResult> AnalyzeTextAsync(
        [Description("Text to analyze")] string text,
        [Description("Analysis depth: basic, standard, comprehensive")] string depth = "standard",
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(200, cancellationToken); // Simulate analysis time
        
        if (string.IsNullOrEmpty(text))
        {
            return CallToolResult.FromError("Text cannot be empty");
        }

        var words = text.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?' }, 
            StringSplitOptions.RemoveEmptyEntries);
        
        var sentences = text.Split(new[] { '.', '!', '?' }, 
            StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        var basicMetrics = new
        {
            CharacterCount = text.Length,
            WordCount = words.Length,
            SentenceCount = sentences.Length,
            ParagraphCount = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length,
            AverageWordsPerSentence = sentences.Length > 0 ? (double)words.Length / sentences.Length : 0,
            AverageCharactersPerWord = words.Length > 0 ? words.Average(w => w.Length) : 0
        };

        var analysis = new Dictionary<string, object>
        {
            ["BasicMetrics"] = basicMetrics
        };

        if (depth is "standard" or "comprehensive")
        {
            var wordFrequency = words
                .GroupBy(w => w.ToLowerInvariant())
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count());

            var readabilityMetrics = new
            {
                LongestWord = words.OrderByDescending(w => w.Length).FirstOrDefault()?.Length ?? 0,
                ShortestWord = words.OrderBy(w => w.Length).FirstOrDefault()?.Length ?? 0,
                UniqueWords = words.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                LexicalDiversity = words.Length > 0 ? (double)words.Distinct(StringComparer.OrdinalIgnoreCase).Count() / words.Length : 0
            };

            analysis["WordFrequency"] = wordFrequency;
            analysis["ReadabilityMetrics"] = readabilityMetrics;
        }

        if (depth == "comprehensive")
        {
            var advancedMetrics = new
            {
                CapitalLetters = text.Count(char.IsUpper),
                LowercaseLetters = text.Count(char.IsLower),
                Digits = text.Count(char.IsDigit),
                Punctuation = text.Count(char.IsPunctuation),
                Whitespace = text.Count(char.IsWhiteSpace),
                VowelCount = text.Count(c => "aeiouAEIOU".Contains(c)),
                ConsonantCount = text.Count(c => char.IsLetter(c) && !"aeiouAEIOU".Contains(c))
            };

            analysis["AdvancedMetrics"] = advancedMetrics;
        }

        analysis["AnalysisDepth"] = depth;
        analysis["AnalyzedAt"] = DateTime.UtcNow.ToString("O");

        return CallToolResult.FromContent(
            TextContent.Create($"Text Analysis: {System.Text.Json.JsonSerializer.Serialize(analysis, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }

    /// <summary>
    /// Generate secure password with customizable complexity.
    /// </summary>
    [McpServerTool(Name = "premium_generate_password", Description = "Generate secure password with customizable complexity")]
    public static async Task<CallToolResult> GeneratePasswordAsync(
        [Description("Password length (8-128)")] int length = 16,
        [Description("Include uppercase letters")] bool includeUppercase = true,
        [Description("Include lowercase letters")] bool includeLowercase = true,
        [Description("Include numbers")] bool includeNumbers = true,
        [Description("Include special characters")] bool includeSpecial = true,
        [Description("Exclude ambiguous characters (0, O, l, I, etc.)")] bool excludeAmbiguous = false,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(150, cancellationToken);
        
        if (length < 8 || length > 128)
        {
            return CallToolResult.FromError("Password length must be between 8 and 128 characters");
        }

        if (!includeUppercase && !includeLowercase && !includeNumbers && !includeSpecial)
        {
            return CallToolResult.FromError("At least one character type must be enabled");
        }

        var characterSets = new List<string>();
        var guaranteedChars = new List<char>();

        if (includeUppercase)
        {
            var upperChars = excludeAmbiguous ? "ABCDEFGHJKLMNPQRSTUVWXYZ" : "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            characterSets.Add(upperChars);
            guaranteedChars.Add(upperChars[RandomNumberGenerator.GetInt32(upperChars.Length)]);
        }

        if (includeLowercase)
        {
            var lowerChars = excludeAmbiguous ? "abcdefghjkmnpqrstuvwxyz" : "abcdefghijklmnopqrstuvwxyz";
            characterSets.Add(lowerChars);
            guaranteedChars.Add(lowerChars[RandomNumberGenerator.GetInt32(lowerChars.Length)]);
        }

        if (includeNumbers)
        {
            var numberChars = excludeAmbiguous ? "23456789" : "0123456789";
            characterSets.Add(numberChars);
            guaranteedChars.Add(numberChars[RandomNumberGenerator.GetInt32(numberChars.Length)]);
        }

        if (includeSpecial)
        {
            var specialChars = excludeAmbiguous ? "!@#$%^&*+-=" : "!@#$%^&*()_+-=[]{}|;:,.<>?";
            characterSets.Add(specialChars);
            guaranteedChars.Add(specialChars[RandomNumberGenerator.GetInt32(specialChars.Length)]);
        }

        var allChars = string.Join("", characterSets);
        var password = new StringBuilder();

        // Add guaranteed characters first
        foreach (var c in guaranteedChars)
        {
            password.Append(c);
        }

        // Fill remaining positions
        for (int i = guaranteedChars.Count; i < length; i++)
        {
            password.Append(allChars[RandomNumberGenerator.GetInt32(allChars.Length)]);
        }

        // Shuffle the password
        var passwordArray = password.ToString().ToCharArray();
        for (int i = passwordArray.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (passwordArray[i], passwordArray[j]) = (passwordArray[j], passwordArray[i]);
        }

        var finalPassword = new string(passwordArray);
        
        // Calculate password strength
        var entropy = Math.Log2(allChars.Length) * length;
        var strengthRating = entropy switch
        {
            < 50 => "Weak",
            < 75 => "Moderate",
            < 100 => "Strong",
            _ => "Very Strong"
        };

        var result = new
        {
            Password = finalPassword,
            Length = length,
            Entropy = Math.Round(entropy, 2),
            StrengthRating = strengthRating,
            CharacterTypes = new
            {
                Uppercase = includeUppercase,
                Lowercase = includeLowercase,
                Numbers = includeNumbers,
                Special = includeSpecial,
                ExcludeAmbiguous = excludeAmbiguous
            },
            GeneratedAt = DateTime.UtcNow.ToString("O")
        };

        return CallToolResult.FromContent(
            TextContent.Create($"Password Generation: {System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }

    /// <summary>
    /// Perform benchmark test to measure system performance.
    /// </summary>
    [McpServerTool(Name = "premium_performance_benchmark", Description = "Perform system performance benchmark")]
    public static async Task<CallToolResult> PerformanceBenchmarkAsync(
        [Description("Benchmark type: cpu, memory, disk, network")] string benchmarkType = "cpu",
        [Description("Test duration in seconds (1-30)")] int durationSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        if (durationSeconds < 1 || durationSeconds > 30)
        {
            return CallToolResult.FromError("Duration must be between 1 and 30 seconds");
        }

        var startTime = DateTime.UtcNow;
        var results = new Dictionary<string, object>();

        switch (benchmarkType.ToLowerInvariant())
        {
            case "cpu":
                results = await BenchmarkCpuAsync(durationSeconds, cancellationToken);
                break;
            case "memory":
                results = await BenchmarkMemoryAsync(durationSeconds, cancellationToken);
                break;
            case "disk":
                results = await BenchmarkDiskAsync(durationSeconds, cancellationToken);
                break;
            default:
                return CallToolResult.FromError($"Unknown benchmark type: {benchmarkType}. Supported types: cpu, memory, disk");
        }

        results["BenchmarkType"] = benchmarkType;
        results["DurationSeconds"] = durationSeconds;
        results["StartTime"] = startTime.ToString("O");
        results["EndTime"] = DateTime.UtcNow.ToString("O");

        return CallToolResult.FromContent(
            TextContent.Create($"Performance Benchmark: {System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }

    private static async Task<Dictionary<string, object>> BenchmarkCpuAsync(int durationSeconds, CancellationToken cancellationToken)
    {
        var operations = 0L;
        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);

        while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
        {
            // Perform CPU-intensive operation
            Math.Sqrt(operations);
            operations++;
            
            if (operations % 10000 == 0)
            {
                await Task.Yield(); // Allow other tasks to run
            }
        }

        return new Dictionary<string, object>
        {
            ["OperationsPerformed"] = operations,
            ["OperationsPerSecond"] = operations / durationSeconds,
            ["BenchmarkScore"] = operations / 1000000.0 // Normalize to millions of operations
        };
    }

    private static async Task<Dictionary<string, object>> BenchmarkMemoryAsync(int durationSeconds, CancellationToken cancellationToken)
    {
        var allocations = 0L;
        var totalMemoryAllocated = 0L;
        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);
        
        while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
        {
            // Allocate and deallocate memory
            var data = new byte[1024]; // 1KB allocation
            allocations++;
            totalMemoryAllocated += data.Length;
            
            if (allocations % 1000 == 0)
            {
                await Task.Yield();
                GC.Collect(0, GCCollectionMode.Optimized, false);
            }
        }

        return new Dictionary<string, object>
        {
            ["AllocationsPerformed"] = allocations,
            ["TotalMemoryAllocated"] = totalMemoryAllocated,
            ["AllocationsPerSecond"] = allocations / durationSeconds,
            ["MemoryThroughputMBps"] = (totalMemoryAllocated / 1024.0 / 1024.0) / durationSeconds
        };
    }

    private static async Task<Dictionary<string, object>> BenchmarkDiskAsync(int durationSeconds, CancellationToken cancellationToken)
    {
        var operations = 0L;
        var tempFile = Path.GetTempFileName();
        var data = new byte[4096]; // 4KB blocks
        RandomNumberGenerator.Fill(data);
        
        try
        {
            var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);
            
            using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose);
            
            while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
            {
                await fileStream.WriteAsync(data, cancellationToken);
                operations++;
                
                if (operations % 100 == 0)
                {
                    await fileStream.FlushAsync(cancellationToken);
                    await Task.Yield();
                }
            }
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* Ignore cleanup errors */ }
        }

        return new Dictionary<string, object>
        {
            ["WriteOperations"] = operations,
            ["TotalBytesWritten"] = operations * data.Length,
            ["OperationsPerSecond"] = operations / durationSeconds,
            ["ThroughputMBps"] = (operations * data.Length / 1024.0 / 1024.0) / durationSeconds
        };
    }
}