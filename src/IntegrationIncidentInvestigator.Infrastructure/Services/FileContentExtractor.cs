using System.Text;
using System.Text.RegularExpressions;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Core.Models;

namespace IntegrationIncidentInvestigator.Infrastructure.Services;

public sealed partial class FileContentExtractor : IFileContentExtractor
{
    private const int BeforeContextLines = 3;
    private const int AfterContextLines = 3;
    private const string TruncatedLineMarker = " ... <line truncated> ... ";

    public async Task<EvidenceExcerpt> ExtractAsync(IncidentFile file, int maximumFileBytes, int maximumCharacters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        }

        if (file.Category == FileCategory.Screenshot)
        {
            return new EvidenceExcerpt(file.Name, file.Category,
                "Screenshot detected. Visual content was not sent to the AI provider; review the local image preview.",
                "Images are not uploaded in this version. The user can inspect the local screenshot and choose whether to attach it.",
                file.SizeBytes, 0, true);
        }

        var isLog = file.Category is FileCategory.ApplicationLog or FileCategory.IntegrationLog;
        if (!isLog && file.SizeBytes > maximumFileBytes)
        {
            throw new InvalidDataException($"File exceeds the configured {maximumFileBytes / (1024 * 1024)} MB extraction limit.");
        }

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (!isLog && extension is not (".txt" or ".err" or ".error" or ".exception" or ".stacktrace" or ".json" or ".xml" or ".csv" or ".html" or ".htm" or ".config" or ".ini" or ".yaml" or ".yml"))
        {
            return new EvidenceExcerpt(file.Name, file.Category, "No text extractor is registered for this file type.",
                "Unsupported file type; metadata only.", file.SizeBytes, 0);
        }

        await using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (!isLog && stream.Length > maximumFileBytes)
        {
            throw new InvalidDataException($"File exceeds the configured {maximumFileBytes / (1024 * 1024)} MB extraction limit.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 64 * 1024, leaveOpen: false);
        var selected = isLog
            ? await ExtractLogAsync(reader, maximumCharacters, cancellationToken)
            : await ReadBoundedAsync(reader, maximumCharacters, cancellationToken);
        var content = selected.Content.Length > maximumCharacters ? selected.Content[..maximumCharacters] : selected.Content;
        var reason = selected.Reason;
        if (file.Category == FileCategory.Html)
        {
            reason += " HTML source text only; no scripts executed or external resources loaded.";
        }

        return new EvidenceExcerpt(file.Name, file.Category, content, reason, file.SizeBytes, content.Length);
    }

    private static async Task<(string Content, string Reason)> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(limit, 16_384));
        var buffer = new char[8_192];
        while (builder.Length < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit - builder.Length)), cancellationToken);
            if (read == 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
        }

        var truncated = false;
        if (builder.Length >= limit)
        {
            truncated = await reader.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) > 0;
        }

        return (builder.ToString(), truncated ? $"Content truncated at the {limit:N0}-character selection limit." : "Full text content selected.");
    }

    private static async Task<(string Content, string Reason)> ExtractLogAsync(StreamReader reader, int limit, CancellationToken cancellationToken)
    {
        var preceding = new Queue<string>(BeforeContextLines);
        var selected = new List<string>();
        var selectedLength = 0;
        var following = 0;
        var detectedErrors = 0;
        var totalLines = 0;
        var boundedLineReader = new BoundedLogLineReader(reader);
        string? line;

        while ((line = await boundedLineReader.ReadLineAsync(Math.Min(limit, 8_192), cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalLines++;
            if (following > 0)
            {
                AddLine(selected, line, ref selectedLength, limit);
                following--;
            }

            if (ErrorLinePattern().IsMatch(line))
            {
                detectedErrors++;
                foreach (var contextLine in preceding)
                {
                    AddLine(selected, contextLine, ref selectedLength, limit);
                }

                AddLine(selected, line, ref selectedLength, limit);
                following = AfterContextLines;
            }

            if (selectedLength >= limit)
            {
                break;
            }

            if (preceding.Count == BeforeContextLines)
            {
                preceding.Dequeue();
            }

            preceding.Enqueue(line);
        }

        if (selected.Count == 0)
        {
            var tail = new Queue<string>();
            while (preceding.Count > 0)
            {
                tail.Enqueue(preceding.Dequeue());
            }

            return ($"No error-pattern lines detected in {totalLines:N0} streamed log lines.{Environment.NewLine}{string.Join(Environment.NewLine, tail)}",
                "Streamed the log without loading it into memory; no common error markers were detected, so only a bounded tail/context was selected.");
        }

        return (string.Join(Environment.NewLine, selected),
            $"Streamed {totalLines:N0} log lines; selected up to {detectedErrors:N0} error events with surrounding context, bounded to {limit:N0} characters.");
    }

    private sealed class BoundedLogLineReader(StreamReader reader)
    {
        private readonly char[] _buffer = new char[4_096];
        private int _bufferOffset;
        private int _bufferCount;

        public async ValueTask<string?> ReadLineAsync(int maximumLineCharacters, CancellationToken cancellationToken)
        {
            var availableCharacters = Math.Max(0, maximumLineCharacters - TruncatedLineMarker.Length);
            var prefixLimit = availableCharacters / 2;
            var suffixLimit = availableCharacters - prefixLimit;
            var prefix = new StringBuilder(prefixLimit);
            var suffix = new Queue<char>(suffixLimit);
            var sawCharacter = false;
            var truncated = false;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_bufferOffset == _bufferCount)
                {
                    _bufferCount = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken);
                    _bufferOffset = 0;
                    if (_bufferCount == 0)
                    {
                        if (!sawCharacter)
                        {
                            return null;
                        }

                        break;
                    }
                }

                var character = _buffer[_bufferOffset++];
                if (character == '\n')
                {
                    break;
                }

                if (character == '\r')
                {
                    continue;
                }

                sawCharacter = true;
                if (prefix.Length < prefixLimit)
                {
                    prefix.Append(character);
                    continue;
                }

                truncated = true;
                if (suffixLimit == 0)
                {
                    continue;
                }

                if (suffix.Count == suffixLimit)
                {
                    suffix.Dequeue();
                }

                suffix.Enqueue(character);
            }

            return truncated
                ? $"{prefix}{TruncatedLineMarker}{new string(suffix.ToArray())}"
                : prefix.ToString();
        }
    }

    private static void AddLine(List<string> lines, string line, ref int length, int limit)
    {
        var additional = line.Length + Environment.NewLine.Length;
        if (length + additional > limit)
        {
            var remaining = limit - length;
            if (remaining > 0)
            {
                lines.Add(line[..Math.Min(line.Length, remaining)]);
                length += Math.Min(line.Length, remaining);
            }

            return;
        }

        lines.Add(line);
        length += additional;
    }

    [GeneratedRegex(@"\b(error|exception|fail(?:ed|ure)?|timeout|timed out|denied|invalid|fatal|stack trace|http\s*5\d\d)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ErrorLinePattern();
}
