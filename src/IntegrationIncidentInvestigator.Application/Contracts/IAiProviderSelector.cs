namespace IntegrationIncidentInvestigator.Application.Contracts;

public interface IAiProviderSelector
{
    string CurrentProvider { get; }
    string CurrentModel { get; }
    IReadOnlyList<string> Models { get; }
    void Select(string provider);
    void SelectModel(string model);
}
