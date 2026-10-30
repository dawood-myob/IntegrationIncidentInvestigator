using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Infrastructure.Services;

public sealed class OpenAiCompatibleLlmClient(IHttpClientFactory httpClientFactory, IOptions<AiProviderSettings> settings, ILogger<OpenAiCompatibleLlmClient> logger) : ILlmClient
{
    private const string HttpClientName = "incident-investigation-ai";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var configuration = settings.Value;
        if (!Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.IsLoopback && endpoint.Scheme == Uri.UriSchemeHttp)))
        {
            throw new InvalidOperationException("Configure AI:Endpoint with an HTTPS chat-completions URL (HTTP is allowed only for a local development server).");
        }

        var apiKey = Environment.GetEnvironmentVariable(configuration.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"Set the {configuration.ApiKeyEnvironmentVariable} environment variable before using the configured AI provider.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = JsonContent.Create(new
        {
            model = string.IsNullOrWhiteSpace(request.Model) ? configuration.Model : request.Model,
            max_completion_tokens = configuration.MaxOutputTokens,
            response_format = request.ResponseSchema is { } schema
                ? (object)new
                {
                    type = "json_schema",
                    json_schema = new { name = "incident_investigation", strict = true, schema }
                }
                : new { type = "json_object" },
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserContent }
            }
        }, options: JsonOptions);

        using var httpClient = httpClientFactory.CreateClient(HttpClientName);
        using var response = await SendAsync(httpClient, message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Configured AI provider returned HTTP {StatusCode}.", (int)response.StatusCode);
            throw new HttpRequestException($"The AI provider returned HTTP {(int)response.StatusCode} ({response.StatusCode}). Check provider configuration and try again.");
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0
            || choices[0].ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The AI provider response did not contain a message. No investigation output was generated.");
        }

        var choice = choices[0];
        if (choice.TryGetProperty("finish_reason", out var finishReason))
        {
            if (finishReason.ValueKind != JsonValueKind.String || finishReason.GetString() != "stop")
            {
                throw new InvalidDataException("The AI response was incomplete or blocked. Reduce selected evidence or check the provider's output limits. No investigation output was generated.");
            }
        }

        if (!choice.TryGetProperty("message", out var messageElement)
            || messageElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The AI provider response did not contain a message. No investigation output was generated.");
        }

        if (messageElement.TryGetProperty("refusal", out var refusal) && refusal.ValueKind != JsonValueKind.Null)
        {
            throw new InvalidDataException("The AI provider declined the investigation request. No investigation output was generated.");
        }

        if (!messageElement.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(content.GetString()))
        {
            throw new InvalidDataException("The AI provider response did not contain a message. No investigation output was generated.");
        }

        return content.GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The AI provider request timed out. Check connectivity or provider availability and retry.", exception);
        }
    }
}
