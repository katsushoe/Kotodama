using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kotodama;

/// <summary>入力文から順序を持たない原子的な語彙を抽出します。</summary>
public interface ITermExtractor
{
    /// <summary>入力文を永続化せず、正規化した語彙と出現回数を返します。</summary>
    IReadOnlyList<ExtractedTerm> Extract(string text);
}

/// <summary>正規化済み語彙と順序を持たない出現回数です。</summary>
public sealed record ExtractedTerm(string CanonicalName, int OccurrenceCount);

/// <summary>OSに依存しない決定的なUnicode語彙抽出器です。</summary>
public sealed partial class DeterministicTermExtractor : ITermExtractor
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "in", "is", "it", "of", "on", "or", "that", "the", "to", "was", "were", "with",
        "これ", "それ", "あれ", "ここ", "そこ", "ため", "もの", "こと", "です", "ます", "でした", "ました", "する", "した", "して", "いる", "ある", "なる"
    };

    /// <inheritdoc />
    public IReadOnlyList<ExtractedTerm> Extract(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (CredentialPattern().IsMatch(text))
            throw new ArgumentException("Input contains credential-like content and cannot be persisted.", nameof(text));

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TokenPattern().Matches(text.Normalize(NormalizationForm.FormKC)))
        {
            var term = NormalizeToken(match.Value);
            if (term is null || StopWords.Contains(term)) continue;
            counts[term] = counts.GetValueOrDefault(term) + 1;
        }

        return counts.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new ExtractedTerm(x.Key, x.Value)).ToArray();
    }

    private static string? NormalizeToken(string token)
    {
        if (Uri.TryCreate(token, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return $"{uri.Scheme.ToLowerInvariant()}://{uri.IdnHost.ToLowerInvariant()}";

        var value = token.Trim('\'', '’', '-', '_', '.');
        if (value.Length < 2 || value.Length > KnowledgeStore.MaximumEntityNameLength) return null;
        if (value.All(char.IsDigit) || value.All(IsPunctuationOrSymbol)) return null;
        if (value.All(c => c <= 0x7f && char.IsLetter(c))) return value.ToLowerInvariant();
        return value;
    }

    private static bool IsPunctuationOrSymbol(char value) =>
        CharUnicodeInfo.GetUnicodeCategory(value) is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol;

    [GeneratedRegex(@"https?://[^\s<>']+|[vV]?\d+(?:\.\d+){1,3}(?:[-+][A-Za-z0-9._-]+)?|[A-Za-z0-9]+(?:[_-][A-Za-z0-9]+)+|[A-Za-z][A-Za-z0-9'’]*|\p{IsCJKUnifiedIdeographs}+|\p{IsKatakana}+|\p{IsHiragana}+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(?i)(?:-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\b(?:password|passwd|api[_-]?key|access[_-]?token|client[_-]?secret)\s*[:=]\s*\S+|\b(?:sk-|ghp_|github_pat_|AKIA)[A-Za-z0-9_-]{12,})", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialPattern();
}
