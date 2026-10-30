using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IntegrationIncidentInvestigator.Core.Services;

public sealed partial class ErrorFingerprinter
{
    public string Normalize(string value)
    {
        var normalized = GuidPattern().Replace(value, "<GUID>");
        normalized = TimestampPattern().Replace(normalized, "<TIMESTAMP>");
        normalized = RequestIdPattern().Replace(normalized, "$1<REQUEST_ID>");
        normalized = EmployeeIdPattern().Replace(normalized, "$1<EMPLOYEE_ID>");
        normalized = LongNumberPattern().Replace(normalized, "<ID>");
        normalized = WhitespacePattern().Replace(normalized, " ").Trim();
        return normalized.ToLowerInvariant();
    }

    public string CreateFingerprint(string system, string integration, string errorSignature)
    {
        var input = string.Join("|", Normalize(system), Normalize(integration), Normalize(errorSignature));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    [GeneratedRegex(@"\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?\b", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"\b((?:request|correlation|transaction|trace)\s*(?:id)?\s*[:=]\s*)[A-Za-z0-9-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequestIdPattern();

    [GeneratedRegex(@"\b((?:employee|worker|person)\s*(?:id)?\s*[:=#]?\s*)\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmployeeIdPattern();

    [GeneratedRegex(@"\b\d{5,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex LongNumberPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
