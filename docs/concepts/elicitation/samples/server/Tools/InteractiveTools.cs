using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using static ModelContextProtocol.Protocol.ElicitRequestParams;

namespace Elicitation.Tools;

[McpServerToolType]
public sealed class InteractiveTools
{
    // <snippet_GuessTheNumber>
    [McpServerTool, Description("A simple game where the user has to guess a number between 1 and 10.")]
    public async Task<string> GuessTheNumber(
        McpServer server, // Get the McpServer from DI container
        CancellationToken cancellationToken
    )
    {
        // Check if the client supports elicitation
        if (server.ClientCapabilities?.Elicitation == null)
        {
            // fail the tool call
            throw new McpException("Client does not support elicitation");
        }

        // First ask the user if they want to play
        var playSchema = new RequestSchema
        {
            Properties =
            {
                ["Answer"] = new BooleanSchema()
            }
        };

        var playResponse = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Do you want to play a game?",
            RequestedSchema = playSchema
        }, cancellationToken);

        // Check if user wants to play
        if (playResponse.Action != "accept" || playResponse.Content?["Answer"].ValueKind != JsonValueKind.True)
        {
            return "Maybe next time!";
        }
        // </snippet_GuessTheNumber>

        // Now ask the user to enter their name
        var nameSchema = new RequestSchema
        {
            Properties =
            {
                ["Name"] = new StringSchema()
                {
                    Description = "Name of the player",
                    MinLength = 2,
                    MaxLength = 50,
                }
            }
        };

        var nameResponse = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "What is your name?",
            RequestedSchema = nameSchema
        }, cancellationToken);

        if (nameResponse.Action != "accept")
        {
            return "Maybe next time!";
        }
        string? playerName = nameResponse.Content?["Name"].GetString();

        // Generate a random number between 1 and 10
        Random random = new();
        int targetNumber = random.Next(1, 11); // 1 to 10 inclusive
        int attempts = 0;

        var message = "Guess a number between 1 and 10";

        while (true)
        {
            attempts++;

            var guessSchema = new RequestSchema
            {
                Properties =
                {
                    ["Guess"] = new NumberSchema()
                    {
                        Type = "integer",
                        Minimum = 1,
                        Maximum = 10,
                    }
                }
            };

            var guessResponse = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = message,
                RequestedSchema = guessSchema
            }, cancellationToken);

            if (guessResponse.Action != "accept")
            {
                return "Maybe next time!";
            }
            int guess = (int)(guessResponse.Content?["Guess"].GetInt32())!;

            // Check if the guess is correct
            if (guess == targetNumber)
            {
                return $"Congratulations {playerName}! You guessed the number {targetNumber} in {attempts} attempts!";
            }
            else if (guess < targetNumber)
            {
                message = $"Your guess is too low! Try again (Attempt #{attempts}):";
            }
            else
            {
                message = $"Your guess is too high! Try again (Attempt #{attempts}):";
            }
        }
    }

    // <snippet_EnumExamples>
    [McpServerTool, Description("Example tool demonstrating various enum schema types")]
    public async Task<string> EnumExamples(
        McpServer server,
        CancellationToken cancellationToken
    )
    {
        // Example 1: UntitledSingleSelectEnumSchema - Simple enum without display titles
        var prioritySchema = new RequestSchema
        {
            Properties =
            {
                ["Priority"] = new UntitledSingleSelectEnumSchema
                {
                    Title = "Priority Level",
                    Description = "Select the priority level",
                    Enum = ["low", "medium", "high", "critical"],
                    Default = "medium"
                }
            }
        };

        var priorityResponse = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Select a priority level:",
            RequestedSchema = prioritySchema
        }, cancellationToken);

        if (priorityResponse.Action != "accept")
        {
            return "Operation cancelled";
        }

        string? priority = priorityResponse.Content?["Priority"].GetString();

        // Example 2: TitledSingleSelectEnumSchema - Enum with custom display titles
        var severitySchema = new RequestSchema
        {
            Properties =
            {
                ["Severity"] = new TitledSingleSelectEnumSchema
                {
                    Title = "Issue Severity",
                    Description = "Select the issue severity level",
                    OneOf =
                    [
                        new EnumSchemaOption { Const = "p0", Title = "P0 - Critical (Immediate attention required)" },
                        new EnumSchemaOption { Const = "p1", Title = "P1 - High (Urgent, within 24 hours)" },
                        new EnumSchemaOption { Const = "p2", Title = "P2 - Medium (Within a week)" },
                        new EnumSchemaOption { Const = "p3", Title = "P3 - Low (As time permits)" }
                    ],
                    Default = "p2"
                }
            }
        };

        var severityResponse = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Select the issue severity:",
            RequestedSchema = severitySchema
        }, cancellationToken);

        if (severityResponse.Action != "accept")
        {
            return "Operation cancelled";
        }

        string? severity = severityResponse.Content?["Severity"].GetString();

        // Example 3: UntitledMultiSelectEnumSchema - Select multiple values
        var tagsSchema = new RequestSchema
        {
            Properties =
            {
                ["Tags"] = new UntitledMultiSelectEnumSchema
                {
                    Title = "Tags",
                    Description = "Select one or more tags",
                    MinItems = 1,
                    MaxItems = 3,
                    Items = new UntitledEnumItemsSchema
                    {
                        Type = "string",
                        Enum = ["bug", "feature", "documentation", "enhancement", "question"]
                    },
                    Default = ["bug"]
                }
            }
        };

        var tagsResponse = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Select up to 3 tags:",
            RequestedSchema = tagsSchema
        }, cancellationToken);

        if (tagsResponse.Action != "accept")
        {
            return "Operation cancelled";
        }

        // For multi-select, the value is an array
        var tags = tagsResponse.Content?["Tags"].EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        // Example 4: TitledMultiSelectEnumSchema - Multi-select with custom titles
        var featuresSchema = new RequestSchema
        {
            Properties =
            {
                ["Features"] = new TitledMultiSelectEnumSchema
                {
                    Title = "Features",
                    Description = "Select desired features",
                    Items = new TitledEnumItemsSchema
                    {
                        AnyOf =
                        [
                            new EnumSchemaOption { Const = "auth", Title = "Authentication & Authorization" },
                            new EnumSchemaOption { Const = "api", Title = "RESTful API" },
                            new EnumSchemaOption { Const = "ui", Title = "Modern UI Components" },
                            new EnumSchemaOption { Const = "db", Title = "Database Integration" }
                        ]
                    }
                }
            }
        };

        var featuresResponse = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Select desired features:",
            RequestedSchema = featuresSchema
        }, cancellationToken);

        if (featuresResponse.Action != "accept")
        {
            return "Operation cancelled";
        }

        var features = featuresResponse.Content?["Features"].EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        return $"Selected: Priority={priority}, Severity={severity}, Tags=[{string.Join(", ", tags ?? [])}], Features=[{string.Join(", ", features ?? [])}]";
    }
    // </snippet_EnumExamples>
}