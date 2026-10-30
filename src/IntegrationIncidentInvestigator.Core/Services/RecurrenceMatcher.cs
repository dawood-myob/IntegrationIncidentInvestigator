using System.Text.RegularExpressions;
using IntegrationIncidentInvestigator.Core.Models;

namespace IntegrationIncidentInvestigator.Core.Services;

public sealed partial class RecurrenceMatcher
{
    public IReadOnlyList<HistoricalMatch> FindMatches(
        string currentSystem,
        string currentIntegration,
        string currentErrorSignature,
        IReadOnlyList<HistoricalIncident> history,
        double threshold)
    {
        if (threshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "Similarity threshold must be between 0 and 1.");
        }

        var currentTokens = Tokens(currentErrorSignature);
        if (currentTokens.Count == 0)
        {
            return [];
        }

        var matches = new List<HistoricalMatch>();

        foreach (var incident in history)
        {
            var reasons = new List<string>();
            var integrationMatch = !string.IsNullOrWhiteSpace(currentIntegration)
                && string.Equals(currentIntegration.Trim(), incident.IntegrationName.Trim(), StringComparison.OrdinalIgnoreCase);
            if (integrationMatch)
            {
                reasons.Add("Same integration.");
            }
            else if (!string.IsNullOrWhiteSpace(currentIntegration))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentSystem)
                && string.Equals(currentSystem.Trim(), incident.SystemName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("Same system.");
            }

            var historicalTokens = Tokens(incident.ErrorSignature);
            var union = currentTokens.Union(historicalTokens).Count();
            var similarity = union == 0 ? 0 : (double)currentTokens.Intersect(historicalTokens).Count() / union;
            if (!string.IsNullOrWhiteSpace(incident.ErrorFingerprint)
                && string.Equals(incident.ErrorFingerprint, new ErrorFingerprinter().CreateFingerprint(currentSystem, currentIntegration, currentErrorSignature), StringComparison.OrdinalIgnoreCase))
            {
                similarity = Math.Max(similarity, 0.9);
                reasons.Add("Matching normalized error fingerprint.");
            }

            if (similarity >= threshold)
            {
                reasons.Add($"Error-message token similarity is {similarity:P0}.");
                matches.Add(new HistoricalMatch(incident, Math.Min(1, similarity), reasons));
            }
        }

        return matches.OrderByDescending(match => match.Similarity).Take(5).ToArray();
    }

    private static HashSet<string> Tokens(string value) => TokenPattern().Matches(value.ToLowerInvariant())
        .Select(match => match.Value)
        .Where(token => token.Length > 2)
        .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex("[a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
