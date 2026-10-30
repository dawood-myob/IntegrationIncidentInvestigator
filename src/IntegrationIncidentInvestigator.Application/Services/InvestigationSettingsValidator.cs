using IntegrationIncidentInvestigator.Core.Models;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Application.Services;

public sealed class InvestigationSettingsValidator : IValidateOptions<InvestigationSettings>
{
    public ValidateOptionsResult Validate(string? name, InvestigationSettings options)
    {
        var errors = new List<string>();
        if (options.MaxFileSizeMB is < 1 or > 1_024)
        {
            errors.Add("Investigation:MaxFileSizeMB must be between 1 and 1024.");
        }

        if (options.MaxContextCharacters is < 1_000 or > 1_000_000)
        {
            errors.Add("Investigation:MaxContextCharacters must be between 1000 and 1000000.");
        }

        if (options.SimilarityThreshold is < 0 or > 1)
        {
            errors.Add("Investigation:SimilarityThreshold must be between 0 and 1.");
        }

        if (options.MaxImagePreviewMB is < 1 or > 1_024)
        {
            errors.Add("Investigation:MaxImagePreviewMB must be between 1 and 1024.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
