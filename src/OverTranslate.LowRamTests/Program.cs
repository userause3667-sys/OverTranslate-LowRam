using System.Text.Json;
using OverTranslate.Models;
using OverTranslate.Services.Performance;

// Self-contained test runner for the Low-RAM Edition's pure logic. No test framework: it compiles
// the pure source files directly (see LowRamTests.csproj) and exits non-zero on any failure, so
// run-tests.ps1 / run-tests.sh can use it as-is with nothing but the .NET SDK.
int passed = 0, failed = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) { passed++; Console.WriteLine($"  PASS  {name}"); }
    else { failed++; Console.WriteLine($"  FAIL  {name} {detail}"); }
}
const long GiB = 1024L * 1024 * 1024;

Console.WriteLine("== PerformanceProfile");
Check("3.9 GB -> UltraLow", PerformanceProfile.LevelForRam((long)(3.9 * GiB)) == PerformanceLevel.UltraLow);
Check("4 GB -> UltraLow", PerformanceProfile.LevelForRam(4 * GiB) == PerformanceLevel.UltraLow);
Check("5.9 GB -> UltraLow", PerformanceProfile.LevelForRam((long)(5.9 * GiB)) == PerformanceLevel.UltraLow);
Check("6 GB -> Balanced", PerformanceProfile.LevelForRam(6 * GiB) == PerformanceLevel.Balanced);
Check("8 GB -> Balanced", PerformanceProfile.LevelForRam(8 * GiB) == PerformanceLevel.Balanced);
Check("12 GB -> Quality", PerformanceProfile.LevelForRam(12 * GiB) == PerformanceLevel.Quality);
Check("32 GB -> Quality", PerformanceProfile.LevelForRam(32 * GiB) == PerformanceLevel.Quality);
Check("unknown RAM (0) -> Balanced", PerformanceProfile.LevelForRam(0) == PerformanceLevel.Balanced);
Check("explicit mode wins over RAM", PerformanceProfile.Resolve(PerformanceMode.Quality, 4 * GiB) == PerformanceLevel.Quality);
Check("Auto resolves from RAM", PerformanceProfile.Resolve(PerformanceMode.Auto, 4 * GiB) == PerformanceLevel.UltraLow);
Check("Quality == original behaviour (60 s idle, no slow-down, 400 cache)",
    PerformanceProfile.Quality is { OcrIdleRelease.TotalSeconds: 60, BackoffMax.Ticks: 0, SessionCacheLimit: 400, TrimAfterRelease: false });
Check("UltraLow is strictly leaner than Balanced than Quality",
    PerformanceProfile.UltraLow.OcrIdleRelease < PerformanceProfile.Balanced.OcrIdleRelease
    && PerformanceProfile.Balanced.OcrIdleRelease < PerformanceProfile.Quality.OcrIdleRelease
    && PerformanceProfile.UltraLow.SessionCacheLimit < PerformanceProfile.Balanced.SessionCacheLimit);
var user = new PerformanceSettings { Mode = PerformanceMode.UltraLow, StaticScanMaxDelayMs = 0 };
Check("user delay 0 switches slow-down off", PerformanceProfile.From(user, 4 * GiB).BackoffMax == TimeSpan.Zero);
user.StaticScanMaxDelayMs = 99999;
Check("user delay clamped to 2000", PerformanceProfile.From(user, 4 * GiB).BackoffMax.TotalMilliseconds == 2000);
var q = new PerformanceSettings { Mode = PerformanceMode.Quality, StaticScanMaxDelayMs = 300 };
var qp = PerformanceProfile.From(q, 16 * GiB);
Check("user can enable slow-down on Quality", qp.BackoffMax.TotalMilliseconds == 300 && qp.BackoffStartPolls < 100 && qp.BackoffStep > TimeSpan.Zero);

Console.WriteLine("== Settings JSON compatibility");
var old = JsonSerializer.Deserialize<PerformanceSettings>("{}")!;
Check("missing keys -> defaults", old.Mode == PerformanceMode.Auto && old.PauseWhenSourceInactive && old.UseTranslationCache);
var withMode = JsonSerializer.Deserialize<PerformanceSettings>("{\"Mode\":\"UltraLow\"}")!;
Check("enum read from string", withMode.Mode == PerformanceMode.UltraLow);
Check("enum written as string", JsonSerializer.Serialize(new PerformanceSettings { Mode = PerformanceMode.Quality }).Contains("\"Quality\""));

Console.WriteLine("== ScanBackoff");
var b = new ScanBackoff(PerformanceProfile.UltraLow);
var delays = Enumerable.Range(0, 40).Select(_ => b.OnIdlePoll()).ToList();
Check("no delay before start threshold", delays.Take(19).All(d => d == TimeSpan.Zero));
Check("first delay at poll 20 is one step", delays[19] == TimeSpan.FromMilliseconds(75));
Check("delay grows", delays[20] > delays[19]);
Check("delay capped at 450 ms", delays.Max() == TimeSpan.FromMilliseconds(450) && delays[^1] == TimeSpan.FromMilliseconds(450));
b.OnActivity();
Check("activity resets to normal rhythm", b.OnIdlePoll() == TimeSpan.Zero && b.IdlePolls == 1);
var qb = new ScanBackoff(PerformanceProfile.Quality);
Check("Quality never slows down", Enumerable.Range(0, 5000).All(_ => qb.OnIdlePoll() == TimeSpan.Zero));
var big = new ScanBackoff(PerformanceProfile.UltraLow with { BackoffStep = TimeSpan.MaxValue });
Check("overflow-safe", Enumerable.Range(0, 100).Select(_ => big.OnIdlePoll()).Max() == PerformanceProfile.UltraLow.BackoffMax);

Console.WriteLine("== SourceInactivityTracker");
var clock = new FakeClock();
var t = new SourceInactivityTracker(clock);
var s0 = t.Evaluate(true, TimeSpan.FromSeconds(30));
Check("active: run", s0 is { Skip: false, ReleaseModel: false, Resumed: false });
var s1 = t.Evaluate(false, TimeSpan.FromSeconds(30));
Check("inactive: skip, no release yet", s1 is { Skip: true, ReleaseModel: false });
clock.Advance(TimeSpan.FromSeconds(29));
Check("29 s: still no release", t.Evaluate(false, TimeSpan.FromSeconds(30)).ReleaseModel == false);
clock.Advance(TimeSpan.FromSeconds(2));
Check("31 s: release raised", t.Evaluate(false, TimeSpan.FromSeconds(30)) is { Skip: true, ReleaseModel: true });
Check("release raised once per spell", t.Evaluate(false, TimeSpan.FromSeconds(30)).ReleaseModel == false);
Check("resume raised after a release", t.Evaluate(true, TimeSpan.FromSeconds(30)) is { Skip: false, Resumed: true });
Check("resume raised once", t.Evaluate(true, TimeSpan.FromSeconds(30)).Resumed == false);
t.Evaluate(false, TimeSpan.FromSeconds(30)); clock.Advance(TimeSpan.FromSeconds(5));
Check("short minimise: no resume event (model was kept)", t.Evaluate(true, TimeSpan.FromSeconds(30)).Resumed == false);
t.Evaluate(false, TimeSpan.FromSeconds(30)); clock.Advance(TimeSpan.FromSeconds(31)); t.Evaluate(false, TimeSpan.FromSeconds(30));
t.Evaluate(true, TimeSpan.FromSeconds(30)); t.Evaluate(false, TimeSpan.FromSeconds(30));
Check("a second spell starts its own clock", t.Evaluate(false, TimeSpan.FromSeconds(30)).ReleaseModel == false);

Console.WriteLine("== TranslationCacheStore");
Check("normalize collapses whitespace", TranslationCacheStore.Normalize("  Hello \t\n  world  ") == "Hello world");
Check("normalize NFKC (full-width)", TranslationCacheStore.Normalize("ＡＢＣ　１２３") == "ABC 123");
Check("normalize keeps case", TranslationCacheStore.Normalize("HEALTH") == "HEALTH");
Check("key ignores spacing differences", TranslationCacheStore.Key("g", "EN", "VI", "a  b") == TranslationCacheStore.Key("g", "EN", "VI", "a b"));
Check("key separates engines and targets",
    TranslationCacheStore.Key("g", "EN", "VI", "x") != TranslationCacheStore.Key("m", "EN", "VI", "x")
    && TranslationCacheStore.Key("g", "EN", "VI", "x") != TranslationCacheStore.Key("g", "EN", "JA", "x"));
Check("long text not cacheable", !TranslationCacheStore.IsCacheable(new string('a', 501)) && TranslationCacheStore.IsCacheable("ok"));

var dir = Path.Combine(Path.GetTempPath(), "lowram-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
var file = Path.Combine(dir, "cache.json");
var cclock = new FakeClock();
var cache = new TranslationCacheStore(file, 100, TimeSpan.FromDays(10), cclock);
cache.Set("k1", "một");
Check("set/get", cache.TryGet("k1", out var v1) && v1 == "một");
Check("miss", !cache.TryGet("nope", out _));
cache.Flush();
Check("flush writes file", File.Exists(file));
var reloaded = new TranslationCacheStore(file, 100, TimeSpan.FromDays(10), cclock);
Check("persists across instances", reloaded.TryGet("k1", out var v2) && v2 == "một" && reloaded.Count == 1);
cclock.Advance(TimeSpan.FromDays(11));
Check("expired entry is a miss", !reloaded.TryGet("k1", out _));
var small = new TranslationCacheStore(Path.Combine(dir, "small.json"), 20, TimeSpan.Zero, cclock);
for (var i = 0; i < 20; i++) { small.Set("k" + i, "v" + i); cclock.Advance(TimeSpan.FromSeconds(1)); }
small.TryGet("k0", out _); cclock.Advance(TimeSpan.FromSeconds(1));   // k0 is now recently used
small.Set("extra", "x");
Check("bounded by max entries", small.Count <= 20, $"count={small.Count}");
Check("LRU keeps recently used", small.TryGet("k0", out _));
Check("LRU drops least recently used", !small.TryGet("k1", out _));
for (var i = 0; i < 1000; i++) small.Set("bulk" + i, "v");
Check("stays bounded under load", small.Count <= 20, $"count={small.Count}");
small.Clear();
Check("clear empties memory and disk", small.Count == 0 && !File.Exists(Path.Combine(dir, "small.json")));
File.WriteAllText(Path.Combine(dir, "bad.json"), "{ not json");
var bad = new TranslationCacheStore(Path.Combine(dir, "bad.json"), 10, TimeSpan.Zero);
Check("corrupt file -> empty cache, no throw", bad.Count == 0);
bad.Set("a", "b"); bad.Flush();
Check("corrupt file replaced on save", new TranslationCacheStore(Path.Combine(dir, "bad.json"), 10, TimeSpan.Zero).Count == 1);
cache.Set("blank", "   ");
Check("empty translation is never stored", !cache.TryGet("blank", out _));
Directory.Delete(dir, true);

Console.WriteLine("== GameGlossary");
const string gl = "# comment\nHealth = Máu\nInventory => Túi đồ\nQuest\tNhiệm vụ\nHealth Potion = Bình máu\n[VI]\nRespawn = Hồi sinh\n[JA]\nRespawn = リスポーン\n[*]\n体力 = Máu\n";
var vi = GameGlossary.Parse(gl, "VI");
var ja = GameGlossary.Parse(gl, "JA");
Check("section filtering counts", vi.Count == 6 && ja.Count == 6, $"vi={vi.Count} ja={ja.Count}");
Check("whole line: case-insensitive", vi.TryTranslateWhole("HEALTH", out var w1) && w1 == "Máu");
Check("whole line: trailing punctuation + spaces", vi.TryTranslateWhole("  Inventory: ", out var w2) && w2 == "Túi đồ");
Check("whole line: all separators parse", vi.TryTranslateWhole("quest", out var w3) && w3 == "Nhiệm vụ");
Check("target-specific entry applies only to its target", vi.TryTranslateWhole("Respawn", out var w4) && w4 == "Hồi sinh" && ja.TryTranslateWhole("Respawn", out var w5) && w5 == "リスポーン");
Check("not a whole-line match", !vi.TryTranslateWhole("Open Inventory", out _));
Check("embedded term substituted", vi.SubstituteTerms("Open your Inventory now") == "Open your Túi đồ now");
Check("longest term first", vi.SubstituteTerms("Drink a Health Potion") == "Drink a Bình máu");
Check("whole words only (Latin)", vi.SubstituteTerms("A Request was made") == "A Request was made");
Check("CJK term matches inside text", vi.SubstituteTerms("体力が減った") == "Máuが減った");
Check("no terms -> text unchanged", vi.SubstituteTerms("Nothing here") == "Nothing here");
Check("empty glossary is a no-op", GameGlossary.Empty.SubstituteTerms("Health") == "Health" && !GameGlossary.Empty.TryTranslateWhole("Health", out _));
Check("garbage input does not throw", GameGlossary.Parse("=\n= =\n[\n]\n\u0000", "VI").Count == 0);
Check("regex metacharacters in terms are literal", GameGlossary.Parse("C++ (beta) = X", "VI").SubstituteTerms("Try C++ (beta) today").Contains('X'));

Console.WriteLine("== PerformanceProfile: priority");
Check("UltraLow and Balanced lower priority, Quality does not",
    PerformanceProfile.UltraLow.LowerPriority && PerformanceProfile.Balanced.LowerPriority && !PerformanceProfile.Quality.LowerPriority);
Check("settings: LowerProcessPriority defaults to null (follow profile)", new PerformanceSettings().LowerProcessPriority is null);
Check("settings: old file without the key still reads", JsonSerializer.Deserialize<PerformanceSettings>("{\"Mode\":\"Balanced\"}")!.LowerProcessPriority is null);

Console.WriteLine("== GameProfiles");
const string gp = """
{
  // comment is allowed
  "profiles": [
    { "name": "Off", "enabled": false, "process": "off.exe" },
    { "name": "Alpha", "process": ["Alpha.exe", "AlphaLauncher"], "mode": "ultralow", "staticScanMaxDelayMs": 9999,
      "pauseWhenNotForeground": true, "lowerPriority": false, "glossary": "alpha.txt", },
    { "name": "Beta", "process": "beta.exe", "titleContains": "Chapter", "mode": "Quality" },
    { "name": "TitleOnly", "titleContains": "Moon Game" },
    { "name": "NoMatch" },
    { "name": "BadMode", "process": "bad.exe", "mode": "Turbo", "glossary": "..\\evil.txt" },
    "not an object"
  ]
}
""";
var parsed = GameProfiles.Parse(gp);
Check("usable profiles counted (disabled/invalid skipped)", parsed.Profiles.Count == 4, $"count={parsed.Profiles.Count}");
Check("problems reported (no matcher, bad mode, bad glossary, non-object)", parsed.Problems.Count == 4, string.Join(" | ", parsed.Problems));
var alpha = parsed.Profiles[0];
Check("process list normalised", alpha.Processes.SequenceEqual(new[] { "alpha", "alphalauncher" }));
Check("mode parsed case-insensitively", alpha.Mode == PerformanceMode.UltraLow);
Check("delay clamped to 2000", alpha.StaticScanMaxDelayMs == 2000);
Check("booleans and glossary read", alpha.PauseWhenNotForeground == true && alpha.LowerPriority == false && alpha.Glossary == "alpha.txt");
var badMode = parsed.Profiles.Single(p => p.Name == "BadMode");
Check("bad mode ignored, unsafe glossary path rejected", badMode.Mode is null && badMode.Glossary is null);

Check("match: exe name with path and case", GameProfiles.Match(parsed.Profiles, @"C:\Games\ALPHA.EXE", null)?.Name == "Alpha");
Check("match: second process name", GameProfiles.Match(parsed.Profiles, "AlphaLauncher", "x")?.Name == "Alpha");
Check("match: process + title needs both", GameProfiles.Match(parsed.Profiles, "beta", "Menu") is null
    && GameProfiles.Match(parsed.Profiles, "beta", "Story - chapter 2")?.Name == "Beta");
Check("match: title only", GameProfiles.Match(parsed.Profiles, "whatever", "The MOON GAME window")?.Name == "TitleOnly");
Check("match: nothing for an unknown window", GameProfiles.Match(parsed.Profiles, "notepad", "Untitled") is null);
Check("match: unknown process never matches a process rule", GameProfiles.Match(parsed.Profiles, null, null) is null);
Check("match: first enabled wins", GameProfiles.Match(GameProfiles.Parse("""[{"name":"A","process":"x"},{"name":"B","process":"x"}]""").Profiles, "x", null)?.Name == "A");

var baseSettings = new PerformanceSettings { Mode = PerformanceMode.Auto, StaticScanMaxDelayMs = 100, UseGameGlossary = false };
var applied = GameProfiles.Apply(baseSettings, alpha);
Check("apply: profile fields override", applied.Mode == PerformanceMode.UltraLow && applied.StaticScanMaxDelayMs == 2000
    && applied.PauseWhenSourceNotForeground && applied.LowerProcessPriority == false);
Check("apply: other settings carried over", applied.UseGameGlossary == false && applied.PauseWhenSourceInactive == baseSettings.PauseWhenSourceInactive);
Check("apply: input not modified", baseSettings.Mode == PerformanceMode.Auto && baseSettings.StaticScanMaxDelayMs == 100);
var partial = GameProfiles.Apply(baseSettings, GameProfiles.Parse("""[{"process":"p","pauseWhenNotForeground":true}]""").Profiles[0]);
Check("apply: unset fields keep the user's value", partial.Mode == PerformanceMode.Auto && partial.StaticScanMaxDelayMs == 100 && partial.PauseWhenSourceNotForeground);
Check("apply: no profile returns the same settings", ReferenceEquals(GameProfiles.Apply(baseSettings, null), baseSettings));
Check("profile mode feeds the resolved level", PerformanceProfile.From(applied, 32 * GiB).Level == PerformanceLevel.UltraLow);

Check("bad JSON: reported, no throw", GameProfiles.Parse("{ nope").Profiles.Count == 0 && GameProfiles.Parse("{ nope").Problems.Count == 1);
Check("no profiles array: reported", GameProfiles.Parse("{}").Problems.Count == 1);
Check("empty/blank file: nothing, no problem", GameProfiles.Parse("  ").Problems.Count == 0);
Check("bare array form accepted", GameProfiles.Parse("""[{"process":"z.exe"}]""").Profiles.Count == 1);
Check("safe file name", GameProfiles.SafeFileName("a.txt") == "a.txt" && GameProfiles.SafeFileName("a/b.txt") is null && GameProfiles.SafeFileName("..") is null && GameProfiles.SafeFileName("  ") is null);
Check("shipped template parses with its example switched off", GameProfiles.Parse(
    """
    {
      // c
      "profiles": [ { "name": "x", "enabled": false, "process": "TenGame.exe" } ]
    }
    """).Profiles.Count == 0);

Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

sealed class FakeClock : TimeProvider
{
    private long _ticks = 1_000_000;
    private DateTimeOffset _utc = new(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => _utc;
    public void Advance(TimeSpan by) { _ticks += by.Ticks; _utc += by; }
}
