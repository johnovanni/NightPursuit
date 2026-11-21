using System.Text.Json;
using NightPursuit.Core.Session;
using Xunit;

namespace NightPursuit.Core.Tests;

public sealed class GameStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "np-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        try { if (File.Exists(_dir)) File.Delete(_dir); } catch { }
    }

    string P(string name) => Path.Combine(_dir, name);
    void Write(string name, string text) { Directory.CreateDirectory(_dir); File.WriteAllText(P(name), text); }

    [Fact]
    public void Missing_files_give_defaults_without_a_problem()
    {
        var store = new FileGameStore(_dir);
        var (s, sp) = store.LoadSettings();
        var (t, tp) = store.LoadHighScores();
        Assert.Null(sp); Assert.Null(tp);
        Assert.Equal(0, s.SchemeIndex);
        Assert.True(s.LighterSteering); Assert.True(s.ScreenShake);
        Assert.Equal(TextSize.Normal, s.TextSize);
        Assert.Equal("AAA", s.LastInitials);
        Assert.Equal(1.0, s.TextScale);
        Assert.Empty(t.Entries);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var store = new FileGameStore(_dir);
        var s = new GameSettings { SchemeIndex = 1, LighterSteering = false, ScreenShake = false, TextSize = TextSize.Largest, LastInitials = "JMA" };
        Assert.True(store.SaveSettings(s).Ok);
        var (r, problem) = store.LoadSettings();
        Assert.Null(problem);
        Assert.Equal(1, r.SchemeIndex);
        Assert.False(r.LighterSteering); Assert.False(r.ScreenShake);
        Assert.Equal(TextSize.Largest, r.TextSize);
        Assert.Equal(1.5, r.TextScale);
        Assert.Equal("JMA", r.LastInitials);
        using var doc = JsonDocument.Parse(File.ReadAllText(P("settings.json")));
        Assert.Equal("SpeedLever", doc.RootElement.GetProperty("throttleScheme").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void New_settings_have_sound_on_music_7_effects_8()
    {
        var s = new GameSettings();
        Assert.True(s.SoundOn); Assert.Equal(7, s.MusicVolume); Assert.Equal(8, s.EffectsVolume);
        var c = new GameSettings { SoundOn = false, MusicVolume = 3, EffectsVolume = 2 }.Clone();
        Assert.False(c.SoundOn); Assert.Equal(3, c.MusicVolume); Assert.Equal(2, c.EffectsVolume);
    }

    [Fact]
    public void Sound_settings_save_and_load()
    {
        var store = new FileGameStore(_dir);
        Assert.True(store.SaveSettings(new GameSettings { SoundOn = false, MusicVolume = 3, EffectsVolume = 10 }).Ok);
        var (r, problem) = store.LoadSettings();
        Assert.Null(problem);
        Assert.False(r.SoundOn); Assert.Equal(3, r.MusicVolume); Assert.Equal(10, r.EffectsVolume);
        using var doc = JsonDocument.Parse(File.ReadAllText(P("settings.json")));
        Assert.False(doc.RootElement.GetProperty("soundOn").GetBoolean());
        Assert.Equal(3, doc.RootElement.GetProperty("musicVolume").GetInt32());
        Assert.Equal(10, doc.RootElement.GetProperty("effectsVolume").GetInt32());
    }

    [Fact]
    public void An_old_settings_file_without_sound_fields_loads_the_defaults()
    {
        Write("settings.json", "{ \"version\": 1, \"screenShake\": false }");
        var (s, problem) = new FileGameStore(_dir).LoadSettings();
        Assert.Null(problem);
        Assert.False(s.ScreenShake);
        Assert.True(s.SoundOn); Assert.Equal(7, s.MusicVolume); Assert.Equal(8, s.EffectsVolume);
    }

    [Fact]
    public void Volume_42_loads_as_10_and_minus_3_as_0()
    {
        Write("settings.json", "{ \"musicVolume\": 42, \"effectsVolume\": -3 }");
        var (s, _) = new FileGameStore(_dir).LoadSettings();
        Assert.Equal(10, s.MusicVolume); Assert.Equal(0, s.EffectsVolume);
    }

    [Fact]
    public void A_volume_of_the_wrong_type_keeps_the_default()
    {
        Write("settings.json", "{ \"musicVolume\": \"loud\", \"effectsVolume\": true, \"soundOn\": 1 }");
        var (s, problem) = new FileGameStore(_dir).LoadSettings();
        Assert.Null(problem);
        Assert.Equal(7, s.MusicVolume); Assert.Equal(8, s.EffectsVolume); Assert.True(s.SoundOn);
    }

    [Fact]
    public void Text_scale_follows_text_size_and_clone_copies_everything()
    {
        Assert.Equal(1.25, new GameSettings { TextSize = TextSize.Large }.TextScale);
        var c = new GameSettings { SchemeIndex = 1, LighterSteering = false, ScreenShake = false, TextSize = TextSize.Large, LastInitials = "ZZZ" }.Clone();
        Assert.Equal(1, c.SchemeIndex); Assert.False(c.LighterSteering); Assert.False(c.ScreenShake);
        Assert.Equal(TextSize.Large, c.TextSize); Assert.Equal("ZZZ", c.LastInitials);
    }

    [Fact]
    public void High_scores_round_trip()
    {
        var store = new FileGameStore(_dir);
        var t = new HighScoreTable();
        t.Add(new HighScoreEntry("JMA", 12345, 3, false));
        t.Add(new HighScoreEntry("ZED", 50000, 6, true));
        Assert.True(store.SaveHighScores(t).Ok);
        var (r, problem) = store.LoadHighScores();
        Assert.Null(problem);
        Assert.Equal(2, r.Entries.Count);
        Assert.Equal(new HighScoreEntry("ZED", 50000, 6, true), r.Entries[0]);
        Assert.Equal(new HighScoreEntry("JMA", 12345, 3, false), r.Entries[1]);
    }

    [Fact]
    public void High_score_table_keeps_the_best_10_and_ranks_ties_below()
    {
        var t = new HighScoreTable();
        for (int i = 1; i <= 11; i++) t.Add(new HighScoreEntry("AAA", i * 100, 1, false));
        Assert.Equal(10, t.Entries.Count);
        Assert.Equal(1100, t.Entries[0].Score);
        Assert.Equal(200, t.Entries[9].Score);
        for (int i = 1; i < 10; i++) Assert.True(t.Entries[i - 1].Score >= t.Entries[i].Score);

        Assert.Equal(2, t.RankFor(1100));
        int rank = t.Add(new HighScoreEntry("NEW", 1100, 1, false));
        Assert.Equal(2, rank);
        Assert.Equal("AAA", t.Entries[0].Initials);
        Assert.Equal("NEW", t.Entries[1].Initials);
        Assert.Equal(10, t.Entries.Count);

        Assert.False(t.Qualifies(0));
        int lowest = t.Entries[9].Score;
        Assert.False(t.Qualifies(lowest));
        Assert.Equal(0, t.RankFor(lowest));
        Assert.True(t.Qualifies(lowest + 1));
        Assert.Equal(10, t.RankFor(lowest + 1));
        Assert.True(new HighScoreTable().Qualifies(1));
        Assert.False(new HighScoreTable().Qualifies(0));
    }

    [Fact]
    public void Unreadable_high_scores_are_kept_as_bad_and_a_new_table_starts()
    {
        Write("highscores.json", "{ not json");
        var original = File.ReadAllBytes(P("highscores.json"));
        var store = new FileGameStore(_dir);
        var (t, problem) = store.LoadHighScores();
        Assert.Empty(t.Entries);
        Assert.Equal("HIGH SCORES FILE COULD NOT BE READ. STARTING A NEW TABLE (OLD FILE KEPT AS highscores.bad.json)", problem);
        Assert.Equal(original, File.ReadAllBytes(P("highscores.bad.json")));

        t.Add(new HighScoreEntry("JMA", 10, 1, false));
        Assert.True(store.SaveHighScores(t).Ok);
        Assert.Null(store.LoadHighScores().Problem);
        Assert.Equal(original, File.ReadAllBytes(P("highscores.bad.json")));
    }

    [Fact]
    public void Unreadable_settings_are_kept_as_bad()
    {
        Write("settings.json", "[1,2");
        var (s, problem) = new FileGameStore(_dir).LoadSettings();
        Assert.Equal("SETTINGS FILE COULD NOT BE READ. USING DEFAULTS (OLD FILE KEPT AS settings.bad.json)", problem);
        Assert.Equal(0, s.SchemeIndex);
        Assert.True(File.Exists(P("settings.bad.json")));
    }

    [Fact]
    public void If_the_bad_copy_fails_the_old_file_is_not_overwritten()
    {
        Write("settings.json", "{ nope");
        Directory.CreateDirectory(P("settings.bad.json"));   // a folder in the way, so the copy fails
        var store = new FileGameStore(_dir);
        var (_, problem) = store.LoadSettings();
        Assert.Equal("SETTINGS FILE COULD NOT BE READ. USING DEFAULTS (OLD FILE COULD NOT BE KEPT, SO IT WILL NOT BE OVERWRITTEN)", problem);
        var r = store.SaveSettings(new GameSettings());
        Assert.False(r.Ok);
        Assert.Equal("COULD NOT SAVE SETTINGS: THE OLD FILE COULD NOT BE KEPT", r.Problem);
        Assert.Equal("{ nope", File.ReadAllText(P("settings.json")));
    }

    [Fact]
    public void Out_of_range_settings_fall_back_per_field()
    {
        Write("settings.json", "{ \"version\": 1, \"textSize\": \"Huge\", \"throttleScheme\": 7, \"screenShake\": false }");
        var (s, problem) = new FileGameStore(_dir).LoadSettings();
        Assert.Null(problem);
        Assert.Equal(TextSize.Normal, s.TextSize);
        Assert.Equal(0, s.SchemeIndex);
        Assert.False(s.ScreenShake);
        Assert.True(s.LighterSteering);
    }

    [Fact]
    public void Bad_high_score_entries_are_skipped()
    {
        Write("highscores.json", @"{ ""version"": 1, ""scores"": [
          { ""initials"": ""ab1"", ""score"": 500, ""missionReached"": 1, ""finished"": false },
          { ""initials"": ""NEG"", ""score"": -5, ""missionReached"": 1, ""finished"": false },
          { ""initials"": ""MSN"", ""score"": 700, ""missionReached"": 9, ""finished"": false },
          { ""initials"": ""abc"", ""score"": 900, ""missionReached"": 2, ""finished"": true }
        ] }");
        var (t, problem) = new FileGameStore(_dir).LoadHighScores();
        Assert.Null(problem);
        var e = Assert.Single(t.Entries);
        Assert.Equal(new HighScoreEntry("ABC", 900, 2, true), e);
    }

    [Fact]
    public void A_failed_save_reports_a_problem_and_does_not_throw()
    {
        File.WriteAllText(_dir, "I am a file, not a folder");
        var store = new FileGameStore(_dir);
        var r = store.SaveSettings(new GameSettings());
        Assert.False(r.Ok);
        Assert.StartsWith("COULD NOT SAVE SETTINGS: ", r.Problem);
        Assert.True(r.Problem!.Length <= "COULD NOT SAVE SETTINGS: ".Length + 80);
        var r2 = store.SaveHighScores(new HighScoreTable());
        Assert.False(r2.Ok);
        Assert.StartsWith("COULD NOT SAVE HIGH SCORES: ", r2.Problem);
    }

    [Theory]
    [InlineData("access", "ACCESS DENIED")]
    [InlineData("lock", "FILE IN USE")]
    [InlineData("folder", "FOLDER NOT WRITABLE")]
    [InlineData("json", "NOT VALID JSON")]
    public void A_failed_save_gives_a_short_reason(string kind, string reason)
    {
        Exception ex = kind switch
        {
            "access" => new UnauthorizedAccessException(@"Access to the path 'C:\x\settings.json.tmp' is denied."),
            "lock" => new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020)),
            "folder" => new DirectoryNotFoundException(@"Could not find a part of the path 'C:\x\y'."),
            _ => new System.Text.Json.JsonException("bad"),
        };
        var r = new FileGameStore(_dir, (_, _) => throw ex).SaveSettings(new GameSettings());
        Assert.Equal("COULD NOT SAVE SETTINGS: " + reason, r.Problem);
    }

    [Fact]
    public void A_failed_save_reason_drops_the_path_and_newlines()
    {
        var ex = new IOException("Cannot write to 'C:/Users/someone/AppData/NightPursuit/settings.json'." + Environment.NewLine + "The disk is full.");
        var r = new FileGameStore(_dir, (_, _) => throw ex).SaveSettings(new GameSettings());
        Assert.Equal("COULD NOT SAVE SETTINGS: Cannot write to . The disk is full.", r.Problem);
    }

    [Fact]
    public void Saving_writes_through_a_temp_file()
    {
        var store = new FileGameStore(_dir);
        Assert.True(store.SaveSettings(new GameSettings { LastInitials = "OLD" }).Ok);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        using (JsonDocument.Parse(File.ReadAllText(P("settings.json")))) { }

        // A save that fails at the final move leaves the previous file intact and readable.
        string? tmpSeen = null;
        var failing = new FileGameStore(_dir, (tmp, path) =>
        {
            tmpSeen = tmp;
            Assert.True(File.Exists(tmp));
            throw new IOException("disk went away");
        });
        var r = failing.SaveSettings(new GameSettings { LastInitials = "NEW" });
        Assert.False(r.Ok);
        Assert.Equal("COULD NOT SAVE SETTINGS: disk went away", r.Problem);
        Assert.NotNull(tmpSeen);
        Assert.EndsWith(".tmp", tmpSeen);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        var (kept, problem) = store.LoadSettings();
        Assert.Null(problem);
        Assert.Equal("OLD", kept.LastInitials);
    }

    [Fact]
    public void Memory_store_counts_saves_and_can_fail()
    {
        var m = new MemoryGameStore();
        var s = new GameSettings { LastInitials = "QQQ" };
        Assert.True(m.SaveSettings(s).Ok);
        Assert.True(m.SaveHighScores(new HighScoreTable()).Ok);
        Assert.Equal(1, m.SettingsSaves); Assert.Equal(1, m.HighScoreSaves);
        Assert.Equal("QQQ", m.LastSettings!.LastInitials);
        m.FailSaves = true;
        var r = m.SaveSettings(s);
        Assert.False(r.Ok); Assert.StartsWith("COULD NOT SAVE SETTINGS", r.Problem);
        Assert.StartsWith("COULD NOT SAVE HIGH SCORES", m.SaveHighScores(new HighScoreTable()).Problem);
    }
}
