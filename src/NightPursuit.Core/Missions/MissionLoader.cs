using System.Text.Json;
using NightPursuit.Core.Audio;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

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
    const double MaxCarsPerKm = 12, MaxPresence = 2;
    const double CarSpacing = 40;   // matches MissionDirector
    const int MaxBonus = 100000;
    const double MinBossHealth = 20, MaxBossHealth = 1000;
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
            bool hasLoadout = root.TryGetProperty("loadout", out var loadoutEl);
            if (hasLoadout && root.TryGetProperty("startingOil", out _)) throw Fail("use loadout.oil instead of startingOil, not both");
            LoadoutDefinition? loadout = hasLoadout ? ReadLoadout(loadoutEl) : null;
            int oil = loadout?.Oil ?? (root.TryGetProperty("startingOil", out _) ? Int(root, "startingOil", "startingOil") : 5);
            if (oil is < 0 or > OilDropper.MaxCharges) throw Fail($"startingOil must be from 0 to {OilDropper.MaxCharges} (got {oil})");

            var wavesEl = Required(root, "waves", "waves");
            if (wavesEl.ValueKind != JsonValueKind.Array) throw Fail("waves must be an array");
            var waves = new List<WaveDefinition>();
            foreach (var el in wavesEl.EnumerateArray())
                waves.Add(ReadWave(el, waves.Count, waves.Count > 0 ? waves[^1].AtDistance : double.NegativeInfinity, approach));

            var crates = new List<CrateDefinition>();
            if (root.TryGetProperty("crates", out var cratesEl))
            {
                if (cratesEl.ValueKind != JsonValueKind.Array) throw Fail("crates must be an array");
                foreach (var el in cratesEl.EnumerateArray())
                    crates.Add(ReadCrate(el, crates.Count, crates.Count > 0 ? crates[^1].AtDistance : 0));
            }

            BossDefinition? boss = root.TryGetProperty("boss", out var bossEl) ? ReadBoss(bossEl) : null;

            TrafficDefinition? traffic = root.TryGetProperty("traffic", out var trafficEl) ? ReadTraffic(trafficEl) : null;
            PoliceDefinition? police = root.TryGetProperty("police", out var policeEl) ? ReadPolice(policeEl) : null;

            MissionMusic? music = root.TryGetProperty("music", out var musicEl) ? ReadMusic(musicEl) : null;

            int bonus = 1000;
            if (root.TryGetProperty("bonus", out var bonusEl))
            {
                if (bonusEl.ValueKind != JsonValueKind.Number || !bonusEl.TryGetInt32(out bonus) || bonus is < 0 or > MaxBonus)
                    throw Fail($"bonus must be a whole number from 0 to {MaxBonus}");
            }

            return new MissionDefinition(id, title, briefing, road, approach, oil, waves, loadout, crates, boss, traffic, police, bonus, music);
        }

        MissionMusic ReadMusic(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) throw Fail("music must be an object");
            MusicTrack? theme = null, boss = null;
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Name is not ("theme" or "boss")) throw Fail($"music.{prop.Name} is not a known field (theme, boss)");
                if (!MusicTrack.TryRead(prop.Value, $"music.{prop.Name}", out var track, out var error)) throw Fail(error);
                if (prop.Name == "theme") theme = track; else boss = track;
            }
            return new MissionMusic(theme, boss);
        }

        TrafficDefinition ReadTraffic(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) throw Fail("traffic must be an object");
            foreach (var prop in el.EnumerateObject())
                if (prop.Name is not "carsPerKm") throw Fail($"traffic.{prop.Name} is not a known field (carsPerKm)");
            return new TrafficDefinition(Ranged(el, "carsPerKm", "traffic.carsPerKm", 0, MaxCarsPerKm));
        }

        /// <summary>No section means no police; a section without presence means presence 1, without startingHeat means 0. Presence 0 needs starting heat 0.</summary>
        PoliceDefinition ReadPolice(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) throw Fail("police must be an object");
            foreach (var prop in el.EnumerateObject())
                if (prop.Name is not ("presence" or "startingHeat"))
                    throw Fail($"police.{prop.Name} is not a known field (presence, startingHeat)");
            double presence = el.TryGetProperty("presence", out _) ? Ranged(el, "presence", "police.presence", 0, MaxPresence) : 1;
            double heat = el.TryGetProperty("startingHeat", out _) ? Ranged(el, "startingHeat", "police.startingHeat", 0, 100) : 0;
            // No police ever come at presence 0, so a starting heat there would mean nothing.
            if (presence == 0 && heat > 0) throw Fail("police.startingHeat must be 0 when police.presence is 0");
            return new PoliceDefinition(presence, heat);
        }

        double Ranged(JsonElement obj, string name, string path, double min, double max)
        {
            double v = Num(obj, name, path);
            if (v < min || v > max) throw Fail($"{path} must be from {min} to {max} (got {v})");
            return v;
        }

        /// <summary>Waves and boss behaviours are enemy types only, never a civilian or police car.</summary>
        void RequireEnemyType(AgentKind kind, string path)
        {
            if (kind is AgentKind.Runner or AgentKind.Blocker or AgentKind.Pursuer or AgentKind.Gunner or AgentKind.Dropper or AgentKind.Armored) return;
            throw Fail($"{path} '{kind}' is not an enemy type (Runner, Blocker, Pursuer, Gunner, Dropper, Armored)");
        }

        BossDefinition ReadBoss(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) throw Fail("boss must be an object");
            foreach (var prop in el.EnumerateObject())
                if (prop.Name is not ("name" or "health" or "phases" or "look"))
                    throw Fail($"boss.{prop.Name} is not a known field (name, health, phases, look)");

            string name = Str(el, "name", "boss.name");
            if (string.IsNullOrWhiteSpace(name)) throw Fail("boss.name must not be empty");
            double health = Num(el, "health", "boss.health");
            if (health is < MinBossHealth or > MaxBossHealth)
                throw Fail($"boss.health must be from {MinBossHealth} to {MaxBossHealth} (got {health})");

            var phasesEl = Required(el, "phases", "boss.phases");
            if (phasesEl.ValueKind != JsonValueKind.Array) throw Fail("boss.phases must be an array");
            int count = phasesEl.GetArrayLength();
            if (count is < 2 or > 3) throw Fail($"boss.phases must have 2 or 3 phases (got {count})");
            var phases = new List<BossPhaseDefinition>();
            foreach (var phaseEl in phasesEl.EnumerateArray())
                phases.Add(ReadPhase(phaseEl, phases.Count, count, phases.Count > 0 ? phases[^1].EndsBelow : null));

            var look = BossLook.Fin;
            if (el.TryGetProperty("look", out var lookEl))
            {
                string lookText = lookEl.ValueKind == JsonValueKind.String ? lookEl.GetString() ?? "" : lookEl.ToString();
                if (lookEl.ValueKind != JsonValueKind.String || !TryEnumName(lookText, out look))
                    throw Fail($"boss.look must be Fin, Twin or Dome, not '{lookText}'");
            }
            return new BossDefinition(name, health, phases, look);
        }

        /// <summary>
        /// One phase: any behaviour but Boss; endsBelow in [0, 1), above 0 for every phase but the last, exactly 0 for
        /// the last, and strictly below the phase before it.
        /// </summary>
        BossPhaseDefinition ReadPhase(JsonElement el, int index, int count, double? previous)
        {
            string p = $"boss.phases[{index}]";
            if (el.ValueKind != JsonValueKind.Object) throw Fail($"{p} must be an object");
            foreach (var prop in el.EnumerateObject())
                if (prop.Name is not ("behaviour" or "endsBelow" or "missiles"))
                    throw Fail($"{p}.{prop.Name} is not a known field (behaviour, endsBelow, missiles)");

            var behaviour = Kind(el, "behaviour", $"{p}.behaviour");
            if (behaviour == AgentKind.Boss) throw Fail($"{p}.behaviour 'Boss' is not a behaviour");
            RequireEnemyType(behaviour, $"{p}.behaviour");

            double endsBelow = Num(el, "endsBelow", $"{p}.endsBelow");
            if (endsBelow is < 0 or >= 1) throw Fail($"{p}.endsBelow must be at least 0 and below 1 (got {endsBelow})");
            bool last = index == count - 1;
            if (!last && endsBelow <= 0) throw Fail($"{p}.endsBelow must be above 0 (only the last phase ends at 0)");
            if (previous is double before && endsBelow >= before)
                throw Fail($"{p}.endsBelow must be below boss.phases[{index - 1}].endsBelow");
            if (last && endsBelow != 0) throw Fail($"{p}.endsBelow must be 0 for the last phase");

            bool missiles = false;
            if (el.TryGetProperty("missiles", out var missilesEl))
            {
                if (missilesEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Fail($"{p}.missiles must be true or false");
                missiles = missilesEl.GetBoolean();
            }
            return new BossPhaseDefinition(behaviour, endsBelow, missiles);
        }

        LoadoutDefinition ReadLoadout(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) throw Fail("loadout must be an object");
            var d = LoadoutDefinition.Default;
            int oil = d.Oil, smoke = d.Smoke, caltrops = d.Caltrops, missiles = d.Missiles, strikes = d.Strikes;
            foreach (var prop in el.EnumerateObject())
            {
                switch (prop.Name)
                {
                    case "oil": oil = Charges(el, "oil", OilDropper.MaxCharges); break;
                    case "smoke": smoke = Charges(el, "smoke", SmokeLauncher.MaxCharges); break;
                    case "caltrops": caltrops = Charges(el, "caltrops", CaltropDropper.MaxCharges); break;
                    case "missiles": missiles = Charges(el, "missiles", MissileLauncher.MaxCharges); break;
                    case "strikes": strikes = Charges(el, "strikes", SatelliteStrike.MaxCharges); break;
                    default: throw Fail($"loadout.{prop.Name} is not a known weapon (oil, smoke, caltrops, missiles, strikes)");
                }
            }
            return new LoadoutDefinition(oil, smoke, caltrops, missiles, strikes);
        }

        int Charges(JsonElement loadout, string name, int capacity)
        {
            int n = Int(loadout, name, $"loadout.{name}");
            if (n < 0 || n > capacity) throw Fail($"loadout.{name} must be from 0 to {capacity} (got {n})");
            return n;
        }

        CrateDefinition ReadCrate(JsonElement el, int index, double previousAt)
        {
            string p = $"crates[{index}]";
            if (el.ValueKind != JsonValueKind.Object) throw Fail($"{p} must be an object");

            double at = Num(el, "atDistance", $"{p}.atDistance");
            if (at < 0) throw Fail($"{p}.atDistance must not be negative");
            if (at < previousAt) throw Fail($"{p}.atDistance {at} comes before the previous crate ({previousAt}); crates must be in ascending order");

            int lane = Int(el, "lane", $"{p}.lane");
            if (lane is < -1 or > 1) throw Fail($"{p}.lane must be -1, 0 or 1 (got {lane})");

            var itemEl = Required(el, "item", $"{p}.item");
            if (itemEl.ValueKind != JsonValueKind.String) throw Fail($"{p}.item must be a string");
            string itemText = itemEl.GetString() ?? "";
            if (!TryEnumName<SupplyItem>(itemText, out var item))
                throw Fail($"{p}.item '{itemText}' is not a known supply item");

            int amount = Int(el, "amount", $"{p}.amount");
            if (amount is < 1 or > 3) throw Fail($"{p}.amount must be from 1 to 3 (got {amount})");
            return new CrateDefinition(at, lane, item, amount);
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

        WaveDefinition ReadWave(JsonElement el, int index, double previousAt, double approach)
        {
            string p = $"waves[{index}]";
            if (el.ValueKind != JsonValueKind.Object) throw Fail($"{p} must be an object");

            double at = Num(el, "atDistance", $"{p}.atDistance");
            if (at < 0) throw Fail($"{p}.atDistance must not be negative");
            if (at < previousAt) throw Fail($"{p}.atDistance {at} comes before the previous wave ({previousAt}); waves must be in ascending order");
            if (at >= approach) throw Fail($"{p}.atDistance {at} must be less than approachLength {approach}");

            var kind = Kind(el, "kind", $"{p}.kind");
            if (kind == AgentKind.Boss) throw Fail($"{p}.kind 'Boss' cannot be a wave (set the boss in \"boss\")");
            RequireEnemyType(kind, $"{p}.kind");

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

        /// <summary>An enemy type by name (any case); numbers and unknown names are rejected.</summary>
        AgentKind Kind(JsonElement obj, string name, string path)
        {
            var v = Required(obj, name, path);
            if (v.ValueKind != JsonValueKind.String) throw Fail($"{path} must be a string");
            string text = v.GetString() ?? "";
            if (!TryEnumName<AgentKind>(text, out var kind))
                throw Fail($"{path} '{text}' is not a known enemy type");
            return kind;
        }

        /// <summary>An enum value by name (any case); empty text, numbers and undefined values are rejected.</summary>
        static bool TryEnumName<T>(string text, out T value) where T : struct, Enum =>
            Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value) && text.Length > 0 && !char.IsDigit(text[0]);

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
