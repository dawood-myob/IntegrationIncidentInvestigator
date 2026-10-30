using System.Net.Http.Json;
using System.Text.Json;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Infrastructure.Services;

public sealed class ClaudeLlmClient(IHttpClientFactory httpClientFactory, IOptions<AiProviderSettings> settings,
    ILogger<ClaudeLlmClient> logger) : ILlmClient
{
    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var configuration = settings.Value;
        if (!Uri.TryCreate(configuration.Claude.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Configure AI:Claude:Endpoint with an HTTPS Anthropic Messages URL.");
        }

        var key = Environment.GetEnvironmentVariable(configuration.Claude.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"Set {configuration.Claude.ApiKeyEnvironmentVariable} before using Claude. A Claude chat subscription is not an API key.");
        }

        if (request.ResponseSchema is not { } schema)
        {
            throw new InvalidOperationException("Claude investigation requests require a response schema.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Headers.Add("x-api-key", key);
        message.Headers.Add("anthropic-version", "2023-06-01");
        message.Content = JsonContent.Create(new
        {
            model = request.Model,
            max_tokens = configuration.MaxOutputTokens,
            system = request.SystemPrompt,
            messages = new[] { new { role = "user", content = request.UserContent } },
            output_config = new { format = new { type = "json_schema", schema } }
        });

        using var httpClient = httpClientFactory.CreateClient("incident-investigation-ai");
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The Claude investigation timed out. Reduce selected evidence or retry later.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Claude returned HTTP {StatusCode}.", (int)response.StatusCode);
                throw new HttpRequestException($"Claude returned HTTP {(int)response.StatusCode}. Check the API key, model availability, billing, and structured-output support.");
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("stop_reason", out var stop) || stop.ValueKind != JsonValueKind.String || stop.GetString() != "end_turn")
            {
                throw new InvalidDataException("Claude returned an incomplete or refused response. Check output limits or reduce selected evidence. No output was generated.");
            }

            if (!root.TryGetProperty("content", out var blocks) || blocks.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Claude did not return text content. No output was generated.");
            }

            var text = string.Concat(blocks.EnumerateArray()
                .Where(block => block.ValueKind == JsonValueKind.Object
                    && block.TryGetProperty("type", out var type) && type.GetString() == "text")
                .Select(block => block.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : ""));
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidDataException("Claude did not return text content. No output was generated.");
            }

            return text;
        }
    }
}
