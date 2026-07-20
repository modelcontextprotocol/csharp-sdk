using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using static ModelContextProtocol.Protocol.ElicitRequestParams;

namespace ShowProducerServer;

[McpServerToolType]
public sealed class ShowProducerTools
{
    [McpServerTool(Name = "produce_show_package")]
    [Description("Produce a compact show package. Task-aware clients can poll, provide input, or cancel while it runs.")]
    public static async Task<string> ProduceShowPackage(
        McpServer server,
        [Description("Name of the event")] string eventName,
        [Description("Number of major show beats, from 2 through 6")] int beats = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        if (beats is < 2 or > 6)
        {
            throw new McpException("The number of show beats must be between 2 and 6.");
        }

        if (server.ClientCapabilities?.Elicitation?.Form is null)
        {
            throw new McpException("produce_show_package requires a client with form elicitation support.");
        }

        ElicitResult direction = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = $"Creative sign-off for \"{eventName}\"",
            RequestedSchema = new RequestSchema
            {
                Properties =
                {
                    ["heroMessage"] = new StringSchema
                    {
                        Title = "Hero message",
                        Description = "Short phrase revealed at the climax",
                        MinLength = 2,
                        MaxLength = 60,
                    },
                    ["energy"] = new UntitledSingleSelectEnumSchema
                    {
                        Title = "Energy",
                        Enum = ["elegant", "cinematic", "playful", "maximum"],
                        Default = "cinematic",
                    },
                    ["includePyrotechnics"] = new BooleanSchema
                    {
                        Title = "Include pyrotechnics",
                        Default = true,
                    },
                },
                Required = ["heroMessage", "energy", "includePyrotechnics"],
            },
        }, cancellationToken);

        if (!direction.IsAccepted)
        {
            return $"Show production {direction.Action}.";
        }

        IDictionary<string, JsonElement> values =
            direction.Content ?? throw new McpException("The accepted creative sign-off was empty.");
        string heroMessage = GetRequiredString(values, "heroMessage");
        string energy = GetRequiredString(values, "energy");
        bool includePyrotechnics = GetRequiredBoolean(values, "includePyrotechnics");

        // Each phase stands in for real rendering, media transcode, or vendor coordination work.
        for (int phase = 0; phase < 4; phase++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken);
        }

        var runSheet = new StringBuilder()
            .AppendLine($"SHOW PACKAGE: {eventName.ToUpperInvariant()}")
            .AppendLine($"Creative energy: {energy}")
            .AppendLine($"Effects: {(includePyrotechnics ? "Pyrotechnics + lighting + projection" : "Lighting + projection")}")
            .AppendLine()
            .AppendLine("RUN OF SHOW");

        for (int beat = 1; beat <= beats; beat++)
        {
            int elapsedSeconds = (beat - 1) * 15;
            int minutes = elapsedSeconds / 60;
            int seconds = elapsedSeconds % 60;
            string cue = beat == beats ? $"Reveal: {heroMessage}" : $"Beat {beat}: build {energy} momentum";
            runSheet.AppendLine($"{minutes:00}:{seconds:00}  {cue}");
        }

        return runSheet
            .AppendLine()
            .AppendLine("Delivery: stage cues, safety notes, media manifest, and operator handoff complete.")
            .ToString();
    }

    private static string GetRequiredString(IDictionary<string, JsonElement> values, string name)
    {
        if (!values.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new McpException($"The elicitation response field '{name}' must be a string.");
        }

        return value.GetString()!;
    }

    private static bool GetRequiredBoolean(IDictionary<string, JsonElement> values, string name)
    {
        if (!values.TryGetValue(name, out JsonElement value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new McpException($"The elicitation response field '{name}' must be a Boolean.");
        }

        return value.GetBoolean();
    }
}
