using IntegrationIncidentInvestigator.Core.Models;

namespace IntegrationIncidentInvestigator.Core.Services;

public sealed class FileClassifier
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    };

    public IncidentFile Classify(string fullPath, long sizeBytes, DateTime lastModifiedUtc)
    {
        var name = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var lowerName = name.ToLowerInvariant();
        var category = lowerName == "problem.txt" ? FileCategory.ProblemDescription
            : ImageExtensions.Contains(extension) ? FileCategory.Screenshot
            : extension is ".err" or ".error" or ".exception" or ".stacktrace" ? FileCategory.ErrorOrException
            : extension == ".log" && lowerName.Contains("integration") ? FileCategory.IntegrationLog
            : extension == ".log" ? FileCategory.ApplicationLog
            : extension == ".json" ? FileCategory.Json
            : extension == ".xml" ? FileCategory.Xml
            : extension == ".csv" ? FileCategory.Csv
            : extension is ".html" or ".htm" ? FileCategory.Html
            : extension is ".config" or ".ini" or ".yaml" or ".yml" ? FileCategory.Configuration
            : extension == ".txt" ? FileCategory.Text
            : FileCategory.Unknown;

        var likelyRelevant = category is FileCategory.ProblemDescription or FileCategory.IntegrationLog or FileCategory.ErrorOrException or FileCategory.Json or FileCategory.Xml or FileCategory.Screenshot or FileCategory.Html;
        var reason = category switch
        {
            FileCategory.ProblemDescription => "Human-written incident description.",
            FileCategory.IntegrationLog => "Integration log likely contains processing events and errors.",
            FileCategory.ErrorOrException => "Error or exception report may contain direct failure evidence.",
            FileCategory.Html => "HTML diagnostic audit may contain validation errors; read as source text without executing scripts.",
            FileCategory.Json or FileCategory.Xml or FileCategory.Csv => "Structured data may contain request, response, or error details.",
            FileCategory.Screenshot => "Screenshot may show a relevant error or system state; review before sharing.",
            FileCategory.ApplicationLog => "Application log may provide relevant context; review its contents.",
            FileCategory.Configuration => "Configuration can contain secrets; excluded from investigation by default.",
            _ => "File type is not recognized for automatic text extraction."
        };

        var include = category is not (FileCategory.Configuration or FileCategory.Unknown);
        var attach = category is FileCategory.IntegrationLog or FileCategory.ErrorOrException;
        return new IncidentFile(fullPath, name, sizeBytes, lastModifiedUtc, category, likelyRelevant, reason, include, attach);
    }
}
