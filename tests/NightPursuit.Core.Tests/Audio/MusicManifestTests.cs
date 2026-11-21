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
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "title": "a/b.mp3" }""")]
    [InlineData("""{ "title": { "file": "a.mp3", "volume": 3 } }""")]
    [InlineData("""{ "title": 5 }""")]
    [InlineData("""{ "tittle": "a.mp3" }""")]
    public void Bad_music_json_reports_one_problem(string json)
    {
        Write(json);
        var (m, problems) = MusicManifest.Load(_dir);
        Assert.Equal(new[] { "MUSIC LIST COULD NOT BE READ" }, problems);
        Assert.Empty(m.Tracks());
    }
}
