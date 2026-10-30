using Microsoft.Extensions.DependencyInjection;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using IntegrationIncidentInvestigator.Core.Models;
using IntegrationIncidentInvestigator.Core.Services;

namespace IntegrationIncidentInvestigator.Application.Services;

public static class InvestigationPipelineRegistration
{
    public static IServiceCollection AddIncidentInvestigation(this IServiceCollection services)
    {
        services.AddSingleton<FileClassifier>();
        services.AddSingleton<ErrorFingerprinter>();
        services.AddSingleton<RecurrenceMatcher>();
        services.AddSingleton<InvestigationResponseParser>();
        services.AddSingleton<IInvestigationPromptBuilder, InvestigationPromptBuilder>();
        services.AddSingleton<IIncidentInvestigationService, IncidentInvestigationService>();
        services.AddSingleton<IInvestigationArtifactWriter, InvestigationArtifactFormatter>();
        services.Configure<InvestigationSettings>(_ => { });
        services.Configure<AiProviderSettings>(_ => { });
        return services;
    }
}
