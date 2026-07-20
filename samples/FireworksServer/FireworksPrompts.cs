using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FireworksServer;

[McpServerPromptType]
public sealed class FireworksPrompts
{
    [McpServerPrompt(Name = "choreograph_fireworks")]
    [Description("Turn an occasion into a concise creative brief and launch a matching fireworks show.")]
    public static string ChoreographFireworks(
        [Description("Event, milestone, or audience to celebrate")] string occasion,
        [Description("Desired emotional tone, such as cinematic, playful, or triumphant")] string mood = "cinematic")
    {
        return $"""
            Design a fireworks show for: {occasion}
            Mood: {mood}

            Pick the strongest matching palette, write a title of at most six words, and create a finale
            message of at most five words. Then call launch_fireworks. Prefer intensity 4 unless the user
            asks for something subtle or explicitly wants the biggest possible finale.
            """;
    }
}
