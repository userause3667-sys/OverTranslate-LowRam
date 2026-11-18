using System.Windows;
using OverTranslate.Models;
using OverTranslate.Services;
using OverTranslate.Services.Performance;
using OverTranslate.Services.Providers;

int passed = 0, failed = 0;
void Check(string n, bool ok, string d = "") { if (ok) { passed++; Console.WriteLine("  PASS  " + n); } else { failed++; Console.WriteLine("  FAIL  " + n + " " + d); } }

var settings = SettingsService.Instance.Current;
Console.WriteLine("settings loaded; Performance.Mode=" + settings.Performance.Mode);
Console.WriteLine("cache path=" + TranslationAssist.CachePath);
Directory.CreateDirectory(Path.GetDirectoryName(TranslationAssist.CachePath)!);
TranslationAssist.ClearCache();
File.Delete(TranslationAssist.GlossaryPath);

var sent = new List<string>();
int calls = 0;
bool? engineFlag = null;
Func<List<OcrTextBlock>, Task<(List<TranslatedBlock>, string)>> send = blocks =>
{
    calls++;
    sent.AddRange(blocks.Select(b => b.Text));
    var list = blocks.Select(b => new TranslatedBlock(b.Text, "T(" + b.Text + ")", b.Bounds)).ToList();
    return Task.FromResult((list, "EN"));
};
OcrTextBlock Blk(string t) => new(t, new Rect(0, 0, 10, 10));
Task<(List<TranslatedBlock> Blocks, string DetectedLang)> Run(string src, string tgt, params string[] lines) =>
    TranslationAssist.RunAsync(lines.Select(Blk).ToList(), src, tgt, TranslationProvider.Google, null, send, used => engineFlag = used);

Console.WriteLine("== no glossary: cache");
var r1 = await Run("EN", "VI", "Hello", "World");
Check("first call reaches the engine with both lines", calls == 1 && sent.SequenceEqual(new[] { "Hello", "World" }));
Check("results in order", r1.Blocks.Select(b => b.TranslatedText).SequenceEqual(new[] { "T(Hello)", "T(World)" }));
sent.Clear(); calls = 0;
var r2 = await Run("EN", "VI", "Hello", "World");
Check("second identical call never reaches the engine", calls == 0, $"calls={calls}");
Check("cached answers identical + ordered", r2.Blocks.Select(b => b.TranslatedText).SequenceEqual(new[] { "T(Hello)", "T(World)" }));
Check("engineUsed(false) reported", engineFlag == false);
Check("detected language falls back to source", r2.DetectedLang == "EN");
var r3 = await Run("EN", "VI", "Hello", "New line", "World");
Check("mixed: only the new line is sent", calls == 1 && sent.SequenceEqual(new[] { "New line" }), string.Join("|", sent));
Check("mixed: order preserved", r3.Blocks.Select(b => b.TranslatedText).SequenceEqual(new[] { "T(Hello)", "T(New line)", "T(World)" }));
Check("mixed: original text kept", r3.Blocks.Select(b => b.OriginalText).SequenceEqual(new[] { "Hello", "New line", "World" }));
sent.Clear(); calls = 0;
await Run("EN", "VI", "  Hello  "); 
Check("whitespace variants hit the cache", calls == 0);
await Run("EN", "JA", "Hello");
Check("different target is a miss", calls == 1);
calls = 0;
await Run("AUTO", "VI", "Hello");
Check("automatic source language bypasses the cache", calls == 1);

Console.WriteLine("== glossary");
File.WriteAllText(TranslationAssist.GlossaryPath, "Health = Máu\nInventory = Túi đồ\nHealth Potion = Bình máu\n");
sent.Clear(); calls = 0;
var g1 = await Run("EN", "VI", "Health", "Open Inventory", "Plain text");
Check("whole-line term answered locally", g1.Blocks[0].TranslatedText == "Máu");
Check("only non-glossary lines were sent", sent.SequenceEqual(new[] { "Open Túi đồ", "Plain text" }), string.Join("|", sent));
Check("embedded substitution reached engine; overlay shows the ORIGINAL text", g1.Blocks[1].OriginalText == "Open Inventory" && g1.Blocks[1].TranslatedText == "T(Open Túi đồ)");
Check("order preserved with glossary", g1.Blocks.Select(b => b.OriginalText).SequenceEqual(new[] { "Health", "Open Inventory", "Plain text" }));
sent.Clear(); calls = 0;
var g2 = await Run("EN", "VI", "Health");
Check("glossary-only request makes no engine call", calls == 0 && g2.Blocks[0].TranslatedText == "Máu");
sent.Clear(); calls = 0;
var g3 = await Run("AUTO", "VI", "Health");
Check("glossary-only + automatic source falls back to engine (needs detected language)", calls == 1);

Console.WriteLine("== short engine reply");
Func<List<OcrTextBlock>, Task<(List<TranslatedBlock>, string)>> shortSend = blocks =>
    Task.FromResult((new List<TranslatedBlock> { new(blocks[0].Text, "only-first", blocks[0].Bounds) }, "EN"));
var rs = await TranslationAssist.RunAsync(new[] { "a1", "a2", "a3" }.Select(Blk).ToList(), "EN", "VI", TranslationProvider.Google, null, shortSend, _ => { });
Check("short reply keeps every line, degrades the missing ones", rs.Blocks.Count == 3 && rs.Blocks[0].TranslatedText == "only-first");

Console.WriteLine("== game profiles: file, glossary overlay, priority");
Directory.CreateDirectory(GameProfileStore.GlossaryFolder);
File.WriteAllText(GameProfileStore.FilePath, """
{ "profiles": [
  { "name": "Quest Game", "process": "questgame.exe", "mode": "Balanced", "glossary": "quest.txt", "pauseWhenNotForeground": true }
] }
""");
File.WriteAllText(Path.Combine(GameProfileStore.GlossaryFolder, "quest.txt"), "Health = Sinh lực\nMana = Ma lực\n");
File.WriteAllText(TranslationAssist.GlossaryPath, "Health = Máu\nGold = Vàng\n");

Check("store finds the profile by process", GameProfileStore.Find("QuestGame", "x")?.Name == "Quest Game");
Check("store finds nothing for another game", GameProfileStore.Find("other", "x") is null);
Check("store reports no problems for a good file", GameProfileStore.Problems.Count == 0);

sent.Clear(); calls = 0;
var noGame = await Run("EN", "VI", "Health", "Mana");
Check("no game profile: global glossary only", noGame.Blocks[0].TranslatedText == "Máu" && noGame.Blocks[1].TranslatedText == "T(Mana)");

settings.Performance.Mode = PerformanceMode.Auto;
PerformanceManager.SetGameProfile(GameProfileStore.Find("questgame", null));
Check("game profile becomes active", PerformanceManager.ActiveGameProfile?.Name == "Quest Game");
Check("game profile's mode drives the level", PerformanceManager.Current.Level == PerformanceLevel.Balanced);
Check("effective settings carry the override", PerformanceManager.EffectiveSettings.PauseWhenSourceNotForeground);
Check("user's own settings are untouched", !settings.Performance.PauseWhenSourceNotForeground && settings.Performance.Mode == PerformanceMode.Auto);

calls = 0;
var withGame = await Run("EN", "VI", "Health", "Mana", "Gold");
Check("game glossary wins over the global one for the same word", withGame.Blocks[0].TranslatedText == "Sinh lực");
Check("game-only entry is used", withGame.Blocks[1].TranslatedText == "Ma lực");
Check("global-only entry still applies", withGame.Blocks[2].TranslatedText == "Vàng");
Check("all three answered locally", calls == 0);

PerformanceManager.SetGameProfile(null);
Check("clearing restores the user's level", PerformanceManager.ActiveGameProfile is null && PerformanceManager.Current.Level == PerformanceProfile.For(PerformanceProfile.Resolve(PerformanceMode.Auto, SystemMemory.TotalPhysicalBytes)).Level);
var afterGame = await Run("EN", "VI", "Health");
Check("after the game ends the global glossary is back", afterGame.Blocks[0].TranslatedText == "Máu");

File.WriteAllText(GameProfileStore.FilePath, "{ \"profiles\": [ { \"name\": \"Broken\" } ] }");
File.SetLastWriteTimeUtc(GameProfileStore.FilePath, DateTime.UtcNow.AddMinutes(1));
Check("a profile that cannot match is reported, not applied", GameProfileStore.Find("anything", null) is null && GameProfileStore.Problems.Count == 1);
File.WriteAllText(GameProfileStore.FilePath, "{ not json");
File.SetLastWriteTimeUtc(GameProfileStore.FilePath, DateTime.UtcNow.AddMinutes(2));
Check("a broken file never throws", GameProfileStore.Find("anything", null) is null);
File.Delete(GameProfileStore.FilePath);
File.SetLastWriteTimeUtc(TranslationAssist.GlossaryPath, DateTime.UtcNow.AddMinutes(3));
Check("missing file is fine", GameProfileStore.Find("anything", null) is null);
var ensured = GameProfileStore.EnsureFile();
Check("EnsureFile writes a template that parses with nothing active", File.Exists(ensured) && GameProfiles.Parse(File.ReadAllText(ensured)).Problems.Count == 0 && GameProfiles.Parse(File.ReadAllText(ensured)).Profiles.Count == 0);

// Priority: only the direction down, only from Normal, and only back what we moved. The CI-less
// sandbox can lower its own priority (nice), which is all this needs.
using (var me = System.Diagnostics.Process.GetCurrentProcess())
{
    me.PriorityClass = System.Diagnostics.ProcessPriorityClass.Normal;
    settings.Performance.Mode = PerformanceMode.UltraLow;
    PerformanceManager.Refresh();
    me.Refresh();
    Check("UltraLow lowers a normal-priority process", me.PriorityClass == System.Diagnostics.ProcessPriorityClass.BelowNormal, me.PriorityClass.ToString());
    settings.Performance.Mode = PerformanceMode.Quality;
    PerformanceManager.Refresh();
    me.Refresh();
    Check("Quality puts back only what was lowered", me.PriorityClass == System.Diagnostics.ProcessPriorityClass.Normal, me.PriorityClass.ToString());
    settings.Performance.Mode = PerformanceMode.UltraLow;
    settings.Performance.LowerProcessPriority = false;
    PerformanceManager.Refresh();
    me.Refresh();
    Check("the user's own switch (false) overrides the profile", me.PriorityClass == System.Diagnostics.ProcessPriorityClass.Normal, me.PriorityClass.ToString());
    settings.Performance.LowerProcessPriority = null;
    settings.Performance.Mode = PerformanceMode.Auto;
    PerformanceManager.Refresh();
}

Console.WriteLine("== switches");
settings.Performance.UseTranslationCache = false; settings.Performance.UseGameGlossary = false;
calls = 0;
await Run("EN", "VI", "Hello", "Health");
Check("both off: pass-through, everything sent", calls == 1 && sent.Contains("Health"));
settings.Performance.UseTranslationCache = true; settings.Performance.UseGameGlossary = true;

TranslationAssist.Flush();
Check("cache file written on flush", File.Exists(TranslationAssist.CachePath));
Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
