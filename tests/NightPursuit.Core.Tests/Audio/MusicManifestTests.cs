using NightPursuit.Core.Audio;

namespace NightPursuit.Core.Tests.Audio;

public class MusicManifestTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "np-music-" + Guid.NewGuid().ToString("N"));

    public MusicManifestTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    void Write(string json) => File.WriteAllText(Path.Combine(_dir, "music.json"), json);

    [Fact]
    public void Missing_music_json_is_empty_and_silent()
    {
        var (manifest, problems) = MusicManifest.Load(_dir);
        Assert.Empty(problems);
        Assert.Empty(manifest.Tracks());
        Assert.Null(manifest.Title);

        var (none, noProblems) = MusicManifest.Load(Path.Combine(_dir, "nowhere"));
        Assert.Empty(noProblems);
        Assert.Empty(none.Tracks());
    }

    [Fact]
    public void Reads_names_objects_and_empty_slots()
    {
        Write("""{ "title": "", "briefing": "b.mp3", "results": { "file": "r.wav", "volume": 0.8 }, "gameOver": { "file": "" } }""");
        var (m, problems) = MusicManifest.Load(_dir);
        Assert.Empty(problems);
        Assert.Null(m.Title);
        Assert.Equal(new MusicTrack("b.mp3"), m.Briefing);
        Assert.Equal(new MusicTrack("r.wav", 0.8), m.Results);
        Assert.Null(m.GameOver);
        Assert.Null(m.Finished);
    }

    [Fact]
    public void Shipped_music_list_has_every_slot_empty()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "content", "music");
        Assert.True(File.Exists(Path.Combine(dir, "music.json")));
        Assert.True(File.Exists(Path.Combine(dir, "README.md")));
        var (m, problems) = MusicManifest.Load(dir);
        Assert.Empty(problems);
        Assert.Empty(m.Tracks());
    }

    [Theory]
    [InlineData("""{ "title": "a/b.mp3", "briefing": "b.mp3" }""", "MUSIC LIST: TITLE MUST BE A PLAIN FILE NAME WITH NO FOLDERS (GOT 'A/B.MP3')")]
    [InlineData("""{ "title": { "file": "a.mp3", "volume": 3 }, "briefing": "b.mp3" }""", "MUSIC LIST: TITLE VOLUME MUST BE FROM 0 TO 2 (GOT 3)")]
    [InlineData("""{ "title": 5, "briefing": "b.mp3" }""", "MUSIC LIST: TITLE MUST BE A FILE NAME OR AN OBJECT WITH FILE AND VOLUME")]
    [InlineData("""{ "title": "a.ogg", "briefing": "b.mp3" }""", "MUSIC LIST: TITLE MUST BE AN .MP3 OR .WAV FILE (GOT 'A.OGG')")]
    [InlineData("""{ "tittle": "a.mp3", "briefing": "b.mp3" }""", "MUSIC LIST: \"TITTLE\" IS NOT A KNOWN SLOT (TITLE, BRIEFING, RESULTS, GAMEOVER, FINISHED)")]
    public void A_bad_entry_skips_only_its_slot_and_names_it(string json, string problem)
    {
        Write(json);
        var (m, problems) = MusicManifest.Load(_dir);
        Assert.Equal(new[] { problem }, problems);
        Assert.Null(m.Title);
        Assert.Equal(new MusicTrack("b.mp3"), m.Briefing);
    }

    [Fact]
    public void Each_bad_entry_gets_its_own_problem()
    {
        Write("""{ "title": "x.ogg", "results": { "file": "r.mp3", "volume": 9 }, "finished": "f.WAV" }""");
        var (m, problems) = MusicManifest.Load(_dir);
        Assert.Equal(2, problems.Count);
        Assert.StartsWith("MUSIC LIST: RESULTS VOLUME MUST BE FROM 0 TO 2", problems[1]);
        Assert.Equal(new MusicTrack("f.WAV"), m.Finished);
    }

    [Theory]
    [InlineData("{ not json", "MUSIC LIST COULD NOT BE READ (LINE 1)")]
    [InlineData("{\n  \"title\": \"a.mp3\"\n  \"briefing\": \"b.mp3\"\n}", "MUSIC LIST COULD NOT BE READ (LINE 3)")]
    [InlineData("[]", "MUSIC LIST COULD NOT BE READ (IT MUST BE A LIST IN { } BRACES)")]
    public void Unparsable_music_json_is_empty_with_one_problem_and_a_line(string json, string problem)
    {
        Write(json);
        var (m, problems) = MusicManifest.Load(_dir);
        Assert.Equal(new[] { problem }, problems);
        Assert.Empty(m.Tracks());
    }

    [Fact]
    public void Trailing_commas_and_comments_load_fine()
    {
        Write("{\n  // the title tune\n  \"title\": \"a.mp3\",\n  \"briefing\": \"b.MP3\",\n}");
        var (m, problems) = MusicManifest.Load(_dir);
        Assert.Empty(problems);
        Assert.Equal(new MusicTrack("a.mp3"), m.Title);
        Assert.Equal(new MusicTrack("b.MP3"), m.Briefing);
    }
}
