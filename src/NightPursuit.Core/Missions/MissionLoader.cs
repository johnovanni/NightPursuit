using System.Text.Json;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Missions;

/// <summary>Reads mission definitions from JSON (camelCase names, enum values as strings).</summary>
public static class MissionLoader
{
    public static MissionDefinition LoadFile(string path)
    {
        var name = Path.GetFileName(path);
        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new MissionFormatException($"{name}: cannot read mission file ({e.Message})");
        }
        return Load(json, name);
    }

    public static MissionDefinition Load(string json, string sourceName)
    {
        if (json is null) throw new MissionFormatException($"{sourceName}: mission text is missing");
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException e) { throw new MissionFormatException($"{sourceName}: not valid JSON ({e.Message})"); }

        using (doc)
            return new Reader(sourceName).Read(doc.RootElement);
    }

    const double MinAllowedRadius = 50;
    const double CarSpacing = 40;   // matches MissionDirector
    const double BehindNear = -30, BehindFar = -450, AheadNear = 30, AheadFar = 1400;

    sealed class Reader(string source)
    {
        MissionFormatException Fail(string message) => new($"{source}: {message}");

        public MissionDefinition Read(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object) throw Fail("the mission must be a JSON object");

            string id = Str(root, "id", "id");
            string title = Str(root, "title", "title");
            string briefing = Str(root, "briefing", "briefing");
            var road = ReadRoad(Required(root, "road", "road"));
            double approach = Num(root, "approachLength", "approachLength");
            if (approach <= 0) throw Fail("approachLength must be greater than 0");
            int oil = root.TryGetProperty("startingOil", out _) ? Int(root, "startingOil", "startingOil") : 5;
            if (oil is < 0 or > 5) throw Fail($"startingOil must be from 0 to 5 (got {oil})");

            var wavesEl = Required(root, "waves", "waves");
            if (wavesEl.ValueKind != JsonValueKind.Array) throw Fail("waves must be an array");
            var waves = new List<WaveDefinition>();
            foreach (var el in wavesEl.EnumerateArray())
                waves.Add(ReadWave(el, waves.Count, waves.Count > 0 ? waves[^1].AtDistance : double.NegativeInfinity));

            return new MissionDefinition(id, title, briefing, road, approach, oil, waves);
        }

        TrackProfile ReadRoad(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) throw Fail("road must be an object");
            var d = new TrackProfile(0);
            int seed = Int(el, "seed", "road.seed");
            double min = Opt(el, "minRadius", "road.minRadius", d.MinRadius);
            double max = Opt(el, "maxRadius", "road.maxRadius", d.MaxRadius);
            double hill = Opt(el, "hillAmplitude", "road.hillAmplitude", d.HillAmplitude);
            int segments = el.TryGetProperty("segments", out _) ? Int(el, "segments", "road.segments") : d.Segments;
            if (min < MinAllowedRadius) throw Fail($"road.minRadius must be at least {MinAllowedRadius} (got {min})");
            if (max < min) throw Fail("road.maxRadius must not be less than road.minRadius");
            if (hill < 0) throw Fail("road.hillAmplitude must not be negative");
            if (segments is < 100 or > 20000) throw Fail($"road.segments must be from 100 to 20000 (got {segments})");
            return new TrackProfile(seed, min, max, hill, segments);
        }

        WaveDefinition ReadWave(JsonElement el, int index, double previousAt)
        {
            string p = $"waves[{index}]";
            if (el.ValueKind != JsonValueKind.Object) throw Fail($"{p} must be an object");

            double at = Num(el, "atDistance", $"{p}.atDistance");
            if (at < 0) throw Fail($"{p}.atDistance must not be negative");
            if (at < previousAt) throw Fail($"{p}.atDistance {at} comes before the previous wave ({previousAt}); waves must be in ascending order");

            var kindEl = Required(el, "kind", $"{p}.kind");
            if (kindEl.ValueKind != JsonValueKind.String) throw Fail($"{p}.kind must be a string");
            string kindText = kindEl.GetString() ?? "";
            if (!Enum.TryParse<AgentKind>(kindText, ignoreCase: true, out var kind)
                || !Enum.IsDefined(kind) || kindText.Length == 0 || char.IsDigit(kindText[0]))
                throw Fail($"{p}.kind '{kindText}' is not a known enemy type");

            int count = Int(el, "count", $"{p}.count");
            if (count is < 1 or > 10) throw Fail($"{p}.count must be from 1 to 10 (got {count})");
            double offset = Num(el, "spawnOffset", $"{p}.spawnOffset");
            CheckSpawnWindow(p, offset, count);
            return new WaveDefinition(at, kind, count, offset);
        }

        /// <summary>A wave's first and last car must both appear clear of the player: 30 to 450 m behind, or 30 to 1400 m ahead.</summary>
        void CheckSpawnWindow(string p, double offset, int count)
        {
            double last = offset + (count - 1) * CarSpacing * (offset >= 0 ? 1 : -1);
            bool ahead = offset >= AheadNear && offset <= AheadFar && last >= AheadNear && last <= AheadFar;
            bool behind = offset <= BehindNear && offset >= BehindFar && last <= BehindNear && last >= BehindFar;
            if (!ahead && !behind)
                throw Fail($"{p}.spawnOffset {offset} with count {count} puts cars from {offset} to {last} m; " +
                           $"all must be between {BehindFar} and {BehindNear} (behind) or between {AheadNear} and {AheadFar} (ahead)");
        }

        JsonElement Required(JsonElement obj, string name, string path) =>
            obj.TryGetProperty(name, out var v) ? v : throw Fail($"{path} is missing");

        string Str(JsonElement obj, string name, string path)
        {
            var v = Required(obj, name, path);
            if (v.ValueKind != JsonValueKind.String) throw Fail($"{path} must be a string");
            return v.GetString()!;
        }

        double Num(JsonElement obj, string name, string path)
        {
            var v = Required(obj, name, path);
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out double d) || !double.IsFinite(d))
                throw Fail($"{path} must be a number");
            return d;
        }

        double Opt(JsonElement obj, string name, string path, double fallback) =>
            obj.TryGetProperty(name, out _) ? Num(obj, name, path) : fallback;

        int Int(JsonElement obj, string name, string path)
        {
            var v = Required(obj, name, path);
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int i)) throw Fail($"{path} must be a whole number");
            return i;
        }
    }
}
