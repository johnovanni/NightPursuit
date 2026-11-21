using System.Text.RegularExpressions;
using System.Text.Json;

namespace NightPursuit.Core.Session;

public sealed record StoreResult(bool Ok, string? Problem = null);

public interface IGameStore
{
    StoreResult SaveSettings(GameSettings s);
    StoreResult SaveHighScores(HighScoreTable t);
}

/// <summary>Keeps saves in memory (tests and diagnostics); never touches the disk.</summary>
public sealed class MemoryGameStore : IGameStore
{
    public bool FailSaves { get; set; }
    public int SettingsSaves { get; private set; }
    public int HighScoreSaves { get; private set; }
    public GameSettings? LastSettings { get; private set; }
    public HighScoreTable? LastHighScores { get; private set; }

    public StoreResult SaveSettings(GameSettings s)
    {
        if (FailSaves) return new StoreResult(false, "COULD NOT SAVE SETTINGS: TEST FAILURE");
        SettingsSaves++; LastSettings = s.Clone();
        return new StoreResult(true);
    }

    public StoreResult SaveHighScores(HighScoreTable t)
    {
        if (FailSaves) return new StoreResult(false, "COULD NOT SAVE HIGH SCORES: TEST FAILURE");
        HighScoreSaves++;
        var copy = new HighScoreTable();
        foreach (var e in t.Entries) copy.Add(e);
        LastHighScores = copy;
        return new StoreResult(true);
    }
}

/// <summary>settings.json and highscores.json in one folder (created on the first save).</summary>
public sealed class FileGameStore : IGameStore
{
    const string SettingsName = "settings.json", ScoresName = "highscores.json";
    static readonly string[] SchemeNames = { "RampPedal", "SpeedLever" };

    readonly string _dir;
    readonly Action<string, string> _replace;
    // Files that were unreadable and could not be copied to .bad.json: never overwrite them.
    bool _settingsLocked, _scoresLocked;

    public FileGameStore(string directory) : this(directory, null) { }

    /// <summary>The second argument replaces the final temp-file move (a test seam).</summary>
    public FileGameStore(string directory, Action<string, string>? replaceFile)
    {
        _dir = directory;
        _replace = replaceFile ?? ((tmp, path) => File.Move(tmp, path, overwrite: true));
    }

    static bool IsFileProblem(Exception e) =>
        e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException;

    static string Reason(Exception e)
    {
        switch (e)
        {
            case UnauthorizedAccessException: return "ACCESS DENIED";
            case JsonException: return "NOT VALID JSON";
            case DirectoryNotFoundException: return "FOLDER NOT WRITABLE";
            case IOException io when (io.HResult & 0xFFFF) is 32 or 33: return "FILE IN USE";   // sharing / lock violation
        }
        var m = (e.Message ?? "").Replace("\r", " ").Replace("\n", " ");
        // Drop any path (a drive path or a rooted one), which only crowds out the cause.
        m = Regex.Replace(m, @"'?[A-Za-z]:[/\x5C][^'""]*'?|'?[/\x5C][^\s'""]+'?", "");
        m = Regex.Replace(m, @"\s+", " ").Trim();
        return m.Length > 80 ? m[..80] : m;
    }

    // ---- loading ----

    public (GameSettings Settings, string? Problem) LoadSettings()
    {
        var path = Path.Combine(_dir, SettingsName);
        var s = new GameSettings();
        if (!File.Exists(path)) return (s, null);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("not an object");
            if (root.TryGetProperty("throttleScheme", out var th))
            {
                if (th.ValueKind == JsonValueKind.String)
                {
                    int i = Array.IndexOf(SchemeNames, th.GetString());
                    if (i >= 0) s.SchemeIndex = i;
                }
                else if (th.ValueKind == JsonValueKind.Number && th.TryGetInt32(out int n) && (n == 0 || n == 1)) s.SchemeIndex = n;
            }
            if (root.TryGetProperty("lighterSteering", out var ls) && (ls.ValueKind == JsonValueKind.True || ls.ValueKind == JsonValueKind.False))
                s.LighterSteering = ls.GetBoolean();
            if (root.TryGetProperty("screenShake", out var sh) && (sh.ValueKind == JsonValueKind.True || sh.ValueKind == JsonValueKind.False))
                s.ScreenShake = sh.GetBoolean();
            if (root.TryGetProperty("textSize", out var ts) && ts.ValueKind == JsonValueKind.String)
            {
                var text = ts.GetString();
                foreach (var size in Enum.GetValues<TextSize>())
                    if (size.ToString() == text) s.TextSize = size;
            }
            if (root.TryGetProperty("soundOn", out var so) && (so.ValueKind == JsonValueKind.True || so.ValueKind == JsonValueKind.False))
                s.SoundOn = so.GetBoolean();
            if (root.TryGetProperty("musicVolume", out var mv) && ReadVolume(mv) is int music) s.MusicVolume = music;
            if (root.TryGetProperty("effectsVolume", out var ev) && ReadVolume(ev) is int effects) s.EffectsVolume = effects;
            if (root.TryGetProperty("lastInitials", out var li) && li.ValueKind == JsonValueKind.String
                && CleanInitials(li.GetString()) is { } ini)
                s.LastInitials = ini;
            return (s, null);
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            bool kept = KeepBad(path);
            _settingsLocked = !kept;
            return (new GameSettings(), "SETTINGS FILE COULD NOT BE READ. USING DEFAULTS " + Bracket(kept, "settings.bad.json"));
        }
    }

    public (HighScoreTable Table, string? Problem) LoadHighScores()
    {
        var path = Path.Combine(_dir, ScoresName);
        var table = new HighScoreTable();
        if (!File.Exists(path)) return (table, null);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("scores", out var scores) || scores.ValueKind != JsonValueKind.Array)
                throw new JsonException("no scores list");
            foreach (var el in scores.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (!el.TryGetProperty("initials", out var i) || i.ValueKind != JsonValueKind.String) continue;
                var ini = CleanInitials(i.GetString());
                if (ini is null) continue;
                if (!el.TryGetProperty("score", out var sc) || sc.ValueKind != JsonValueKind.Number || !sc.TryGetInt32(out int score) || score < 0) continue;
                if (!el.TryGetProperty("missionReached", out var mr) || mr.ValueKind != JsonValueKind.Number
                    || !mr.TryGetInt32(out int mission) || mission < 1 || mission > Campaign.MissionIds.Length) continue;
                bool finished = el.TryGetProperty("finished", out var f) && f.ValueKind == JsonValueKind.True;
                table.Add(new HighScoreEntry(ini, score, mission, finished));
            }
            return (table, null);
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            bool kept = KeepBad(path);
            _scoresLocked = !kept;
            return (new HighScoreTable(), "HIGH SCORES FILE COULD NOT BE READ. STARTING A NEW TABLE " + Bracket(kept, "highscores.bad.json"));
        }
    }

    // A volume is a JSON number, clamped to 0-10; anything else is ignored.
    static int? ReadVolume(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Number) return null;
        if (el.TryGetInt32(out int n)) return Math.Clamp(n, 0, 10);
        return el.TryGetDouble(out double d) ? (int)Math.Clamp(Math.Round(d), 0, 10) : null;
    }

    static string? CleanInitials(string? s)
    {
        if (s is null || s.Length != 3) return null;
        s = s.ToUpperInvariant();
        foreach (char c in s) if (c < 'A' || c > 'Z') return null;
        return s;
    }

    static string Bracket(bool kept, string badName) =>
        kept ? "(OLD FILE KEPT AS " + badName + ")" : "(OLD FILE COULD NOT BE KEPT, SO IT WILL NOT BE OVERWRITTEN)";

    static bool KeepBad(string path)
    {
        try
        {
            var bad = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".bad.json");
            File.Copy(path, bad, overwrite: true);
            return true;
        }
        catch (Exception e) when (IsFileProblem(e)) { return false; }
    }

    // ---- saving ----

    public StoreResult SaveSettings(GameSettings s)
    {
        if (_settingsLocked) return new StoreResult(false, "COULD NOT SAVE SETTINGS: THE OLD FILE COULD NOT BE KEPT");
        try
        {
            Write(SettingsName, w =>
            {
                w.WriteStartObject();
                w.WriteNumber("version", 1);
                w.WriteString("throttleScheme", SchemeNames[Math.Clamp(s.SchemeIndex, 0, 1)]);
                w.WriteBoolean("lighterSteering", s.LighterSteering);
                w.WriteBoolean("screenShake", s.ScreenShake);
                w.WriteString("textSize", s.TextSize.ToString());
                w.WriteBoolean("soundOn", s.SoundOn);
                w.WriteNumber("musicVolume", Math.Clamp(s.MusicVolume, 0, 10));
                w.WriteNumber("effectsVolume", Math.Clamp(s.EffectsVolume, 0, 10));
                w.WriteString("lastInitials", s.LastInitials);
                w.WriteEndObject();
            });
            return new StoreResult(true);
        }
        catch (Exception e) when (IsFileProblem(e)) { return new StoreResult(false, "COULD NOT SAVE SETTINGS: " + Reason(e)); }
    }

    public StoreResult SaveHighScores(HighScoreTable t)
    {
        if (_scoresLocked) return new StoreResult(false, "COULD NOT SAVE HIGH SCORES: THE OLD FILE COULD NOT BE KEPT");
        try
        {
            Write(ScoresName, w =>
            {
                w.WriteStartObject();
                w.WriteNumber("version", 1);
                w.WriteStartArray("scores");
                foreach (var e in t.Entries)
                {
                    w.WriteStartObject();
                    w.WriteString("initials", e.Initials);
                    w.WriteNumber("score", e.Score);
                    w.WriteNumber("missionReached", e.MissionReached);
                    w.WriteBoolean("finished", e.Finished);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            });
            return new StoreResult(true);
        }
        catch (Exception e) when (IsFileProblem(e)) { return new StoreResult(false, "COULD NOT SAVE HIGH SCORES: " + Reason(e)); }
    }

    void Write(string name, Action<Utf8JsonWriter> body)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, name);
        var tmp = path + ".tmp";
        try
        {
            using (var fs = File.Create(tmp))
            using (var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
            {
                body(w);
                w.Flush();
            }
            _replace(tmp, path);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }
}
