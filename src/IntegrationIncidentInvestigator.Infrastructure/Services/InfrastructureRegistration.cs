using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using IntegrationIncidentInvestigator.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddIncidentInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IIncidentFileScanner, IncidentFileScanner>();
        services.AddSingleton<IFileContentExtractor, FileContentExtractor>();
        services.AddSingleton<IIncidentHistoryRepository>(provider =>
        {
            var configuration = provider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var databasePath = configuration["Storage:Database"] ?? "data/incidents.db";
            return new SqliteIncidentHistoryRepository(databasePath);
        });
        services.AddSingleton<IAiProviderSelector, AiProviderSelector>();
        services.AddHttpClient("incident-investigation-ai", (provider, client) =>
            client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<AiProviderSettings>>().Value.TimeoutSeconds));
        services.AddSingleton<OpenAiCompatibleLlmClient>();
        services.AddSingleton<ClaudeLlmClient>();
        services.AddSingleton<CopilotLlmClient>();
        services.AddSingleton<ILlmClient, ProviderSwitchingLlmClient>();
        return services;
    }
}
