using System.Text.Json;

namespace NightPursuit.Core.Audio;

/// <summary>One music file and a loudness trim (0 to 2, where 1 leaves it as it is).</summary>
public sealed record MusicTrack(string File, double Volume = 1)
{
    public const double MaxVolume = 2;

    /// <summary>True for a plain file name: no folders, no drive, no odd characters.</summary>
    public static bool IsPlainName(string name) =>
        name.Length > 0 && name.Trim() == name && name is not ("." or "..") &&
        Path.GetFileName(name) == name && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>
    /// Reads a music entry: "" (empty, gives null), "file.mp3", or { "file": "...", "volume": 0.8 }.
    /// On a mistake returns false and says what is wrong, starting with <paramref name="path"/>.
    /// </summary>
    public static bool TryRead(JsonElement el, string path, out MusicTrack? track, out string error)
    {
        track = null;
        error = "";
        string? file;
        double volume = 1;
        if (el.ValueKind == JsonValueKind.String) file = el.GetString();
        else if (el.ValueKind == JsonValueKind.Object)
        {
            file = null;
            bool hasFile = false;
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Name == "file")
                {
                    if (prop.Value.ValueKind != JsonValueKind.String) { error = $"{path}.file must be a string"; return false; }
                    file = prop.Value.GetString();
                    hasFile = true;
                }
                else if (prop.Name == "volume")
                {
                    if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetDouble(out volume) || !double.IsFinite(volume))
                    { error = $"{path}.volume must be a number"; return false; }
                    if (volume < 0 || volume > MaxVolume) { error = $"{path}.volume must be from 0 to {MaxVolume} (got {volume})"; return false; }
                }
                else { error = $"{path}.{prop.Name} is not a known field (file, volume)"; return false; }
            }
            if (!hasFile) { error = $"{path}.file is missing"; return false; }
        }
        else { error = $"{path} must be a file name or an object with file and volume"; return false; }

        if (string.IsNullOrEmpty(file)) return true;   // "" means no music here
        if (!IsPlainName(file)) { error = $"{path} must be a plain file name with no folders (got '{file}')"; return false; }
        track = new MusicTrack(file, volume);
        return true;
    }
}

/// <summary>The screen music named in content/music/music.json. An empty slot is null.</summary>
public sealed class MusicManifest
{
    public const string FileName = "music.json";
    public const string UnreadableProblem = "MUSIC LIST COULD NOT BE READ";

    public MusicTrack? Title { get; init; }
    public MusicTrack? Briefing { get; init; }
    public MusicTrack? Results { get; init; }
    public MusicTrack? GameOver { get; init; }
    public MusicTrack? Finished { get; init; }

    /// <summary>
    /// Reads music.json from <paramref name="musicDir"/>. No file is an empty list with no problem. A file that
    /// cannot be read, or has anything wrong in it, is an empty list and one problem.
    /// </summary>
    public static (MusicManifest Manifest, IReadOnlyList<string> Problems) Load(string musicDir)
    {
        var path = Path.Combine(musicDir, FileName);
        if (!File.Exists(path)) return (new MusicManifest(), Array.Empty<string>());
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Unreadable();
            var slots = new Dictionary<string, MusicTrack?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name is not ("title" or "briefing" or "results" or "gameOver" or "finished")) return Unreadable();
                if (!MusicTrack.TryRead(prop.Value, prop.Name, out var track, out _)) return Unreadable();
                slots[prop.Name] = track;
            }
            var manifest = new MusicManifest
            {
                Title = slots.GetValueOrDefault("title"),
                Briefing = slots.GetValueOrDefault("briefing"),
                Results = slots.GetValueOrDefault("results"),
                GameOver = slots.GetValueOrDefault("gameOver"),
                Finished = slots.GetValueOrDefault("finished"),
            };
            return (manifest, Array.Empty<string>());
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Unreadable();
        }
    }

    static (MusicManifest, IReadOnlyList<string>) Unreadable() => (new MusicManifest(), new[] { UnreadableProblem });

    /// <summary>Every slot's track, empty ones skipped.</summary>
    public IEnumerable<MusicTrack> Tracks() =>
        new[] { Title, Briefing, Results, GameOver, Finished }.OfType<MusicTrack>();
}
