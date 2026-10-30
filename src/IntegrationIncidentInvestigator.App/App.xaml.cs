using System.Windows;
using IntegrationIncidentInvestigator.Application.Models;
using IntegrationIncidentInvestigator.Application.Services;
using IntegrationIncidentInvestigator.App.ViewModels;
using IntegrationIncidentInvestigator.Core.Models;
using IntegrationIncidentInvestigator.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IntegrationIncidentInvestigator.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, configuration) => configuration
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true))
                .ConfigureServices((context, services) =>
                {
                    services.AddOptions<AiProviderSettings>()
                        .Bind(context.Configuration.GetSection("AI"))
                        .Validate(value => AiProviders.Supported.Contains(value.Provider, StringComparer.OrdinalIgnoreCase),
                            "AI:Provider must be OpenAICompatible, Claude, or GitHubCopilot.")
                        .Validate(value => value.MaxOutputTokens is >= 1_024 and <= 32_768 && value.TimeoutSeconds is >= 30 and <= 600,
                            "AI:MaxOutputTokens must be 1024-32768 and AI:TimeoutSeconds must be 30-600.")
                        .ValidateOnStart();
                    services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<InvestigationSettings>, InvestigationSettingsValidator>();
                    services.AddOptions<InvestigationSettings>()
                        .Bind(context.Configuration.GetSection("Investigation"))
                        .ValidateOnStart();
                    services.AddIncidentInvestigation();
                    services.AddIncidentInfrastructure();
                    services.AddTransient<MainWindowViewModel>();
                    services.AddTransient<MainWindow>();
                })
                .Build();

            await _host.StartAsync();
            _host.Services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception exception)
        {
            _host?.Dispose();
            MessageBox.Show($"The application could not start: {exception.Message}", "Integration Incident Investigator", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs eventArgs)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        base.OnExit(eventArgs);
    }
}
