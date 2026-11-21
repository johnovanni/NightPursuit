using NightPursuit.Core.Audio;
using NightPursuit.Core.Missions;

namespace NightPursuit.Core.Tests;

public class MusicMissionTests
{
    const string Head = """{ "id": "m", "title": "T", "briefing": "B", "road": { "seed": 7 }, "approachLength": 6000, "waves": [ { "atDistance": 100, "kind": "Runner", "count": 1, "spawnOffset": 400 } ]""";

    static MissionDefinition Load(string extra) => MissionLoader.Load(Head + extra + " }", "m.json");

    [Fact]
    public void Loader_reads_music_as_a_name_or_an_object_with_volume()
    {
        Assert.Null(Load("").Music);

        var both = Load(""", "music": { "theme": "glass.mp3", "boss": { "file": "lantern.wav", "volume": 0.8 } }""");
        Assert.Equal(new MissionMusic(new MusicTrack("glass.mp3"), new MusicTrack("lantern.wav", 0.8)), both.Music);

        var partial = Load(""", "music": { "theme": { "file": "glass.mp3" }, "boss": "" }""");
        Assert.Equal(new MissionMusic(new MusicTrack("glass.mp3"), null), partial.Music);

        Assert.Equal(new MissionMusic(null, null), Load(""", "music": { }""").Music);
        Assert.Equal(2.0, Load(""", "music": { "theme": { "file": "a.mp3", "volume": 2 } }""").Music!.Theme!.Volume);
    }

    [Theory]
    [InlineData(""", "music": { "theme": "songs/glass.mp3" }""", "music.theme must be a plain file name with no folders (got 'songs/glass.mp3')")]
    [InlineData(""", "music": { "theme": "..\\glass.mp3" }""", "music.theme must be a plain file name with no folders")]
    [InlineData(""", "music": { "boss": "C:\\glass.mp3" }""", "music.boss must be a plain file name with no folders")]
    [InlineData(""", "music": { "intro": "a.mp3" }""", "music.intro is not a known field (theme, boss)")]
    [InlineData(""", "music": { "theme": { "file": "a.mp3", "loud": 1 } }""", "music.theme.loud is not a known field (file, volume)")]
    [InlineData(""", "music": { "theme": { "file": "a.mp3", "volume": 2.5 } }""", "music.theme.volume must be from 0 to 2 (got 2.5)")]
    [InlineData(""", "music": { "theme": { "file": "a.mp3", "volume": -1 } }""", "music.theme.volume must be from 0 to 2 (got -1)")]
    [InlineData(""", "music": { "theme": { "volume": 1 } }""", "music.theme.file is missing")]
    [InlineData(""", "music": { "theme": 5 }""", "music.theme must be a file name or an object")]
    [InlineData(""", "music": "a.mp3" """, "music must be an object")]
    public void Loader_rejects_a_music_file_with_a_folder_or_an_unknown_field(string extra, string message)
    {
        var ex = Assert.Throws<MissionFormatException>(() => Load(extra));
        Assert.StartsWith("m.json: " + message, ex.Message);
    }

    [Fact]
    public void The_shipped_missions_load_with_no_music_yet()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "content", "missions");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(8, files.Length);
        foreach (var file in files) Assert.NotNull(MissionLoader.LoadFile(file));
    }
}
