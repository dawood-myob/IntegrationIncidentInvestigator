using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Core.Models;
using IntegrationIncidentInvestigator.Core.Services;
using Microsoft.Extensions.Logging;

namespace IntegrationIncidentInvestigator.Infrastructure.Services;

public sealed class IncidentFileScanner(FileClassifier classifier, ILogger<IncidentFileScanner> logger) : IIncidentFileScanner
{
    private static readonly HashSet<string> GeneratedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "investigation.json", "investigation.txt", "teams-summary.txt", "jira.txt", "jira-comment.txt", "attachment-recommendations.txt"
    };

    public Task<IReadOnlyList<IncidentFile>> ScanAsync(string sourceFolder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        if (!Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException("The selected incident folder does not exist or cannot be accessed.");
        }

        return Task.Run(() => Scan(sourceFolder, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<IncidentFile> Scan(string sourceFolder, CancellationToken cancellationToken)
    {
        var files = new List<IncidentFile>();
        var directories = new Stack<string>();
        directories.Push(sourceFolder);

        while (directories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (GeneratedFiles.Contains(Path.GetFileName(path)))
                    {
                        continue;
                    }

                    try
                    {
                        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        var info = new FileInfo(path);
                        files.Add(classifier.Classify(info.FullName, info.Length, info.LastWriteTimeUtc));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        logger.LogWarning("Unable to inspect incident file {FileName}: {ErrorType}", Path.GetFileName(path), exception.GetType().Name);
                        files.Add(new IncidentFile(path, Path.GetFileName(path), 0, DateTime.MinValue,
                            FileCategory.Unknown, false, "File metadata could not be read.", false, false,
                            "File metadata could not be read."));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (string.Equals(directory, sourceFolder, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("The selected incident folder could not be enumerated.", exception);
                }

                AddDirectoryWarning(directory, exception, files);
            }

            try
            {
                foreach (var subdirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) == 0)
                        {
                            directories.Push(subdirectory);
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        AddDirectoryWarning(subdirectory, exception, files);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AddDirectoryWarning(directory, exception, files);
            }
        }

        return files.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void AddDirectoryWarning(string path, Exception exception, ICollection<IncidentFile> files)
    {
        logger.LogWarning("Unable to enumerate incident folder {FolderName}: {ErrorType}", Path.GetFileName(path), exception.GetType().Name);
        var name = $"[Folder not scanned] {Path.GetFileName(path)}";
        files.Add(new IncidentFile(path, name, 0, DateTime.MinValue, FileCategory.Unknown, false,
            "This subfolder could not be read; its files were not included.", false, false,
            "This subfolder could not be read."));
    }
}
