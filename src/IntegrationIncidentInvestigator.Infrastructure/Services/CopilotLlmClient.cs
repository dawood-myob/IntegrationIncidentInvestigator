using System.ComponentModel;
using GitHub.Copilot;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Infrastructure.Services;

public sealed class CopilotLlmClient(IOptions<AiProviderSettings> settings, ILogger<CopilotLlmClient> logger) : ILlmClient
{
    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken)
    {
        var options = CreateClientOptions(settings.Value.Copilot.HomeDirectory);
        await using var client = new CopilotClient(options);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await client.StartAsync(timeout.Token);
            var authentication = await client.GetAuthStatusAsync(timeout.Token);
            return authentication.IsAuthenticated;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The GitHub Copilot sign-in status check timed out.", exception);
        }
    }

    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var options = CreateClientOptions(settings.Value.Copilot.HomeDirectory);
        await using var client = new CopilotClient(options);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Value.TimeoutSeconds));
        try
        {
            await client.StartAsync(timeout.Token);
            var authentication = await client.GetAuthStatusAsync(timeout.Token);
            if (!authentication.IsAuthenticated)
            {
                throw new UnauthorizedAccessException($"GitHub Copilot is not signed in for this SDK runtime. In PowerShell, set COPILOT_HOME to '{options.BaseDirectory}' and COPILOT_DISABLE_KEYTAR to '1', then run 'copilot login --device-code' using the interactive CLI and complete browser authorization. See documentation/configuration.md. No incident evidence was sent.");
            }

            await using var session = await client.CreateSessionAsync(CreateSessionConfig(request), timeout.Token);
            try
            {
#pragma warning disable GHCP001 // Structured output is experimental in the pinned Copilot SDK.
                var response = await session.SendAndWaitAsync(new MessageOptions
                {
                    Prompt = request.UserContent,
                    ResponseSchema = request.ResponseSchema
                }, TimeSpan.FromSeconds(settings.Value.TimeoutSeconds), timeout.Token);
#pragma warning restore GHCP001
                return !string.IsNullOrWhiteSpace(response?.Data.Content)
                    ? response.Data.Content
                    : throw new InvalidDataException("GitHub Copilot did not return an investigation response.");
            }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await session.AbortAsync(cleanup.Token);
                throw;
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The GitHub Copilot investigation timed out. Check CLI sign-in and model access, or reduce evidence.", exception);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception)
        {
            logger.LogWarning("GitHub Copilot request failed ({ErrorType}).", exception.GetType().Name);
            throw new InvalidOperationException("GitHub Copilot could not complete the request. Install/sign in to Copilot CLI with 'copilot login', check your Copilot subscription and selected model, then retry. Microsoft 365 Copilot credentials cannot be used here.", exception);
        }
    }

    public static CopilotClientOptions CreateClientOptions(string? homeDirectory = null)
    {
        // Visual Studio can inject its own COPILOT_HOME into debugged applications.
        var copilotHome = string.IsNullOrWhiteSpace(homeDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot")
            : Path.GetFullPath(homeDirectory);
        var token = Environment.GetEnvironmentVariable("COPILOT_GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token) && OperatingSystem.IsWindows())
        {
            token = Environment.GetEnvironmentVariable("COPILOT_GITHUB_TOKEN", EnvironmentVariableTarget.User);
        }

        // The SDK replaces the entire child environment, rather than overlaying these values.
        var environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.OrdinalIgnoreCase);
        environment.Remove("COPILOT_SDK_AUTH_TOKEN");
        environment.Remove("COPILOT_RUNTIME_PROCESS_FILE_LOGGING");
        environment["COPILOT_GITHUB_TELEMETRY_DISABLED"] = "true";
        return new CopilotClientOptions
        {
            Mode = CopilotClientMode.Empty,
            BaseDirectory = copilotHome,
            GitHubToken = string.IsNullOrWhiteSpace(token) ? null : token,
            UseLoggedInUser = string.IsNullOrWhiteSpace(token),
            LogLevel = CopilotLogLevel.None,
            Environment = environment,
            WorkingDirectory = AppContext.BaseDirectory
        };
    }

    public static SessionConfig CreateSessionConfig(LlmRequest request) => new()
    {
        Model = request.Model,
        AvailableTools = [],
        SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace, Content = request.SystemPrompt },
        EnableSessionStore = false,
        EnableSessionTelemetry = false,
        InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
        Memory = new MemoryConfiguration { Enabled = false }
    };
}
