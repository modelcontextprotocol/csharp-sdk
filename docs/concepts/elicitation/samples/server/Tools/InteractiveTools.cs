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
        CancellationToken token
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
        }, token);

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
        }, token);

        if (nameResponse.Action != "accept")
        {
            return "Maybe next time!";
        }
        string? playerName = nameResponse.Content?["Name"].GetString();

        // Generate a random number between 1 and 10
        Random random = new Random();
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
            }, token);

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
}