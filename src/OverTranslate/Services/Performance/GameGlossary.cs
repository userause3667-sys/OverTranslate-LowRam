using System.Text;
using System.Text.RegularExpressions;

namespace OverTranslate.Services.Performance;

/// <summary>
/// The user's own list of game terms — <c>Health = Máu</c>, <c>Inventory = Túi đồ</c> — applied around
/// machine translation. No model and no network: a text file and a dictionary.
/// </summary>
/// <remarks>
/// <para><b>File format</b> (<c>glossary.txt</c>, UTF-8, one entry per line):
/// <c>source = target</c> (also <c>=&gt;</c> or a tab between them); lines starting with <c>#</c> or
/// <c>;</c> are comments; a line like <c>[VI]</c> makes the entries below it apply only when
/// translating into that target language code, and <c>[*]</c> (the default at the top of the file) puts
/// them back to all targets. An entry under the exact target language is used in preference to one
/// under <c>[*]</c> for the same source.</para>
///
/// <para><b>Two ways a term is used.</b> A line that is <i>only</i> a term — what a menu label or a HUD
/// word is — is answered from the glossary outright and never reaches an engine. A term inside a
/// longer sentence is swapped for its target before the engine sees the sentence, because an engine
/// leaves a word it does not know in place and translates around it; that is a hint, not a guarantee,
/// and is the reason the glossary is best at names, places, items and fixed terms.</para>
///
/// <para>Matching ignores case. Terms written in a spaced script (Latin, Cyrillic…) match whole words
/// only, so "Quest" does not fire inside "Request"; terms in CJK and Thai scripts, which have no
/// spaces, match anywhere.</para>
/// </remarks>
internal sealed class GameGlossary
{
    public static readonly GameGlossary Empty = new([], null);

    private readonly Dictionary<string, string> _exact;
    private readonly Regex? _embedded;
    private readonly Dictionary<string, string> _embeddedTargets;

    /// <summary>Number of entries in force for the target this was built for.</summary>
    public int Count => _exact.Count;

    private GameGlossary(Dictionary<string, string> exact, Regex? embedded, Dictionary<string, string>? embeddedTargets = null)
    {
        _exact = exact;
        _embedded = embedded;
        _embeddedTargets = embeddedTargets ?? [];
    }

    /// <summary>
    /// Reads glossary text and keeps the entries that apply to <paramref name="targetLanguage"/>.
    /// A malformed line is skipped; nothing here throws on bad input.
    /// </summary>
    public static GameGlossary Parse(string? text, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(text)) return Empty;

        var general = new Dictionary<string, string>(StringComparer.Ordinal);
        var specific = new Dictionary<string, string>(StringComparer.Ordinal);
        var section = "*";

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] is '#' or ';') continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                if (section.Length == 0) section = "*";
                continue;
            }

            var split = SplitEntry(line);
            if (split is null) continue;

            var (source, target) = split.Value;
            var key = Fold(source);
            if (key.Length == 0 || target.Length == 0) continue;

            if (section == "*") general[key] = target;
            else if (section.Equals(targetLanguage, StringComparison.OrdinalIgnoreCase)) specific[key] = target;
            else continue;

        }

        foreach (var (key, target) in specific) general[key] = target;
        if (general.Count == 0) return Empty;

        // Longest terms first, so "Health Potion" is taken before "Health".
        var ordered = general.Keys.OrderByDescending(key => key.Length).ToList();
        var spaced = ordered.Where(IsSpacedScript).Select(Regex.Escape);
        var unspaced = ordered.Where(key => !IsSpacedScript(key)).Select(Regex.Escape);

        var alternatives = new List<string>();
        if (unspaced.Any()) alternatives.Add("(?:" + string.Join('|', unspaced) + ")");
        if (spaced.Any())
            alternatives.Add(@"(?<![\p{L}\p{N}_])(?:" + string.Join('|', spaced) + @")(?![\p{L}\p{N}_])");

        // Both halves are ordered longest first within themselves; a combined alternation of the two
        // kinds is tried left to right, which is what the explicit order above gives.
        var pattern = string.Join('|', alternatives);
        var regex = new Regex(
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(250));

        return new GameGlossary(general, regex, general);
    }

    /// <summary>
    /// The glossary's answer when <paramref name="line"/> is nothing but a term (ignoring case, spacing
    /// and trailing punctuation); false otherwise.
    /// </summary>
    public bool TryTranslateWhole(string line, out string translation)
    {
        translation = "";
        if (_exact.Count == 0) return false;

        var key = Fold(line.Trim().TrimEnd('.', ':', '!', '?', '…', '。', '！', '？', '：'));
        return _exact.TryGetValue(key, out translation!);
    }

    /// <summary>The text with every term swapped for its target. Returns it unchanged when none occurs.</summary>
    public string SubstituteTerms(string text)
    {
        if (_embedded is null || string.IsNullOrEmpty(text)) return text;

        try
        {
            return _embedded.Replace(text, match =>
                _embeddedTargets.TryGetValue(Fold(match.Value), out var target) ? target : match.Value);
        }
        catch (RegexMatchTimeoutException)
        {
            return text;
        }
    }

    /// <summary>The form two spellings of one term share: normalised, one space between words, lower case.</summary>
    internal static string Fold(string text) =>
        TranslationCacheStore.Normalize(text).ToLowerInvariant();

    private static (string Source, string Target)? SplitEntry(string line)
    {
        foreach (var separator in new[] { "=>", "\t", "=" })
        {
            var index = line.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0) continue;

            var source = line[..index].Trim();
            var target = line[(index + separator.Length)..].Trim();
            if (source.Length > 0 && target.Length > 0) return (source, target);
        }

        return null;
    }

    // Scripts written without spaces between words, where a "whole word" cannot be told from the text.
    private static bool IsSpacedScript(string term)
    {
        foreach (var c in term)
        {
            if (c is >= '฀' and <= '๿'          // Thai
                or >= '぀' and <= 'ヿ'            // Hiragana, Katakana
                or >= '㐀' and <= '鿿'            // CJK ideographs
                or >= '가' and <= '힯'            // Hangul syllables
                or >= '豈' and <= '﫿')           // CJK compatibility ideographs
                return false;
        }

        return true;
    }
}
