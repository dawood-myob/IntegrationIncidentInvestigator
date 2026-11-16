namespace IntegrationIncidentInvestigator.Application.Contracts;

public sealed record AiProviderAvailability(string Provider, bool? IsAvailable, string Status, string Details);

public interface IAiProviderStatusService
{
    Task<IReadOnlyList<AiProviderAvailability>> CheckAsync(CancellationToken cancellationToken);
}
