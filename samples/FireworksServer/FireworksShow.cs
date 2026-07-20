using System.Text.Json.Serialization;

namespace FireworksServer;

[JsonConverter(typeof(JsonStringEnumConverter<FireworksTheme>))]
public enum FireworksTheme
{
    Aurora,
    Cyberpunk,
    Ocean,
    Sunset,
    Celebration,
}

public sealed record FireworksCue(
    int DelayMilliseconds,
    double X,
    double Y,
    string PrimaryColor,
    string SecondaryColor,
    string Shape,
    double Scale);

public sealed record FireworksShow(
    string Id,
    string Title,
    FireworksTheme Theme,
    int Intensity,
    string FinaleMessage,
    IReadOnlyList<FireworksCue> Cues);

public sealed record FireworksSettings(string DashboardUrl);

public sealed class ShowState
{
    private FireworksShow? _latest;

    public FireworksShow? Latest => Volatile.Read(ref _latest);

    public void SetLatest(FireworksShow show) => Volatile.Write(ref _latest, show);
}

public static class FireworksShowFactory
{
    private static readonly IReadOnlyDictionary<FireworksTheme, string[]> Palettes =
        new Dictionary<FireworksTheme, string[]>
        {
            [FireworksTheme.Aurora] = ["#7df9ff", "#8b5cf6", "#34d399", "#f0abfc"],
            [FireworksTheme.Cyberpunk] = ["#ff2bd6", "#00f5ff", "#faff00", "#7c3aed"],
            [FireworksTheme.Ocean] = ["#38bdf8", "#0ea5e9", "#2dd4bf", "#e0f2fe"],
            [FireworksTheme.Sunset] = ["#fb7185", "#f97316", "#fbbf24", "#c084fc"],
            [FireworksTheme.Celebration] = ["#ff3366", "#ffd166", "#06d6a0", "#4cc9f0"],
        };

    private static readonly string[] Shapes = ["ring", "chrysanthemum", "willow", "spiral", "star"];

    public static FireworksShow Create(
        string title,
        FireworksTheme theme,
        int intensity,
        string finaleMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(finaleMessage);

        if (intensity is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(intensity), "Intensity must be between 1 and 5.");
        }

        var random = new Random(HashCode.Combine(title, theme, intensity, finaleMessage));
        string[] palette = GetPalette(theme);
        int cueCount = 8 + (intensity * 4);
        var cues = new List<FireworksCue>(cueCount);

        for (int i = 0; i < cueCount; i++)
        {
            bool finale = i >= cueCount - intensity;
            int delay = finale
                ? 900 + ((cueCount - intensity) * 260) + ((i - cueCount + intensity) * 80)
                : 700 + (i * 260);

            cues.Add(new FireworksCue(
                DelayMilliseconds: delay,
                X: 0.12 + (random.NextDouble() * 0.76),
                Y: 0.12 + (random.NextDouble() * 0.42),
                PrimaryColor: palette[random.Next(palette.Length)],
                SecondaryColor: palette[random.Next(palette.Length)],
                Shape: Shapes[random.Next(Shapes.Length)],
                Scale: (finale ? 1.2 : 0.75) + (random.NextDouble() * 0.55)));
        }

        return new FireworksShow(
            Id: Guid.NewGuid().ToString("N"),
            Title: title.Trim(),
            Theme: theme,
            Intensity: intensity,
            FinaleMessage: finaleMessage.Trim(),
            Cues: cues);
    }

    public static string[] GetPalette(FireworksTheme theme) => Palettes[theme];
}
