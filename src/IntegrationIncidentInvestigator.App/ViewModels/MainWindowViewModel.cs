using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using IntegrationIncidentInvestigator.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IIncidentFileScanner _scanner;
    private readonly IIncidentInvestigationService _investigator;
    private readonly IInvestigationArtifactWriter _artifactWriter;
    private readonly IAiProviderSelector _providerSelector;
    private readonly InvestigationSettings _settings;
    private readonly ILogger<MainWindowViewModel> _logger;
    private CancellationTokenSource? _cancellation;
    private string _sourceFolder = "";
    private string _problemDescription = "";
    private string _systemName = "";
    private string _integrationName = "";
    private string _selectedJiraMode = "Create New Jira";
    private string _selectedProvider = AiProviders.OpenAICompatible;
    private string _selectedModel = "";
    private string _existingIssueReference = "";
    private string _statusMessage = "Select an incident folder to begin.";
    private string _errorMessage = "";
    private string _progressStage = "Ready";
    private int _progressValue;
    private bool _isBusy;
    private IncidentInvestigationResult? _result;
    private PreparedInvestigation? _prepared;
    private EvidenceExcerpt? _selectedEvidence;

    public MainWindowViewModel(IIncidentFileScanner scanner, IIncidentInvestigationService investigator,
        IInvestigationArtifactWriter artifactWriter, IAiProviderSelector providerSelector,
        IOptions<InvestigationSettings> settings, ILogger<MainWindowViewModel> logger)
    {
        _scanner = scanner;
        _investigator = investigator;
        _artifactWriter = artifactWriter;
        _providerSelector = providerSelector;
        _settings = settings.Value;
        _logger = logger;
        ProviderOptions = AiProviders.Supported.ToArray();
        _selectedProvider = providerSelector.CurrentProvider;
        _selectedModel = providerSelector.CurrentModel;
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(SourceFolder));
        InvestigateCommand = new AsyncRelayCommand(PrepareInvestigationAsync, () => HasScanned && !IsBusy);
        SendEvidenceCommand = new AsyncRelayCommand(SendEvidenceAsync, () => Prepared is not null && !IsBusy && !string.IsNullOrWhiteSpace(SelectedModel));
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsBusy);
        SaveOutputsCommand = new AsyncRelayCommand(SaveOutputsAsync, () => Result is not null && !IsBusy);
        JiraModes = ["Create New Jira", "Comment on Existing Jira"];
    }

    public ObservableCollection<IncidentFileViewModel> Files { get; } = [];
    public string[] JiraModes { get; }
    public string[] ProviderOptions { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand InvestigateCommand { get; }
    public AsyncRelayCommand SendEvidenceCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand SaveOutputsCommand { get; }
    public ObservableCollection<EvidenceExcerpt> PreparedEvidence { get; } = [];
    public bool HasScanned { get; private set; }

    public string SourceFolder { get => _sourceFolder; set { if (SetProperty(ref _sourceFolder, value)) { ScanCommand.NotifyCanExecuteChanged(); InvalidatePrepared(); } } }
    public string ProblemDescription { get => _problemDescription; set { if (SetProperty(ref _problemDescription, value)) InvalidatePrepared(); } }
    public string SystemName { get => _systemName; set { if (SetProperty(ref _systemName, value)) InvalidatePrepared(); } }
    public string IntegrationName { get => _integrationName; set { if (SetProperty(ref _integrationName, value)) InvalidatePrepared(); } }
    public string SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (value == _selectedProvider) return;
            _providerSelector.Select(value);
            SetProperty(ref _selectedProvider, _providerSelector.CurrentProvider);
            OnPropertyChanged(nameof(ModelOptions));
            _selectedModel = _providerSelector.CurrentModel;
            OnPropertyChanged(nameof(SelectedModel));
            OnPropertyChanged(nameof(ProviderHelp));
            SendEvidenceCommand.NotifyCanExecuteChanged();
        }
    }
    public IReadOnlyList<string> ModelOptions => _providerSelector.Models;
    public string SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (SetProperty(ref _selectedModel, value))
            {
                if (!string.IsNullOrWhiteSpace(value)) _providerSelector.SelectModel(value);
                SendEvidenceCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public string ProviderHelp => SelectedProvider switch
    {
        AiProviders.Claude => "Claude API: requires ANTHROPIC_API_KEY and API billing. Sonnet balances cost and quality; Opus is a higher-cost option. Model access depends on your account.",
        AiProviders.GitHubCopilot => "GitHub Copilot SDK: sign in with 'copilot login' and select a model your Copilot plan permits. This is not Microsoft 365 Copilot. No file/shell tools are enabled.",
        _ => "OpenAI API: requires INCIDENT_AI_API_KEY and API billing. Try gpt-4.1 or an available reasoning model for complex evidence. Model suggestions are editable, not guaranteed account entitlements."
    };
    public bool IsIdle => !IsBusy;
    public string SelectedJiraMode { get => _selectedJiraMode; set { if (SetProperty(ref _selectedJiraMode, value)) OnPropertyChanged(nameof(SelectedJiraText)); } }
    public string ExistingIssueReference { get => _existingIssueReference; set { if (SetProperty(ref _existingIssueReference, value)) OnPropertyChanged(nameof(SelectedJiraText)); } }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string ProgressStage { get => _progressStage; set => SetProperty(ref _progressStage, value); }
    public int ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                ScanCommand.NotifyCanExecuteChanged();
                InvestigateCommand.NotifyCanExecuteChanged();
                SendEvidenceCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                SaveOutputsCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public PreparedInvestigation? Prepared
    {
        get => _prepared;
        private set
        {
            if (SetProperty(ref _prepared, value))
            {
                SendEvidenceCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(HasPreparedEvidence));
                OnPropertyChanged(nameof(PreparedProblemDescription));
                OnPropertyChanged(nameof(PreparedIncidentMetadata));
                OnPropertyChanged(nameof(PreparedHistoricalMatches));
            }
        }
    }
    public bool HasPreparedEvidence => Prepared is not null;
    public string PreparedProblemDescription => Prepared?.Context.ProblemDescription ?? "";
    public string PreparedIncidentMetadata => Prepared is null ? "" : $"Incident: {Prepared.Context.IncidentId} | System: {Prepared.Context.SystemName ?? "Unknown"} | Integration: {Prepared.Context.IntegrationName ?? "Unknown"}";
    public IReadOnlyList<HistoricalMatch> PreparedHistoricalMatches => Prepared?.HistoricalMatches ?? [];
    public EvidenceExcerpt? SelectedEvidence { get => _selectedEvidence; set { if (SetProperty(ref _selectedEvidence, value)) OnPropertyChanged(nameof(SelectedEvidenceContent)); } }
    public string SelectedEvidenceContent => SelectedEvidence?.Content ?? "Select an extracted evidence item to inspect the exact text that would be sent.";
    public IncidentInvestigationResult? Result { get => _result; private set { if (SetProperty(ref _result, value)) { SaveOutputsCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(TeamsSummary)); OnPropertyChanged(nameof(RootCause)); OnPropertyChanged(nameof(ConfidenceLabel)); OnPropertyChanged(nameof(RecurrenceLabel)); OnPropertyChanged(nameof(EvidenceItems)); OnPropertyChanged(nameof(RecommendedActions)); OnPropertyChanged(nameof(HistoricalMatches)); OnPropertyChanged(nameof(SelectedJiraText)); } } }
    public bool HasResult => Result is not null;
    public string TeamsSummary => Result?.TeamsSummary ?? "";
    public string RootCause => Result?.RootCause ?? "";
    public string ConfidenceLabel => Result?.RootCauseConfidence.ToString() ?? "Unknown";
    public string RecurrenceLabel => Result is null ? "" : Result.Recurrence.ToString().ToUpperInvariant();
    public IReadOnlyList<string> EvidenceItems => Result?.Evidence ?? [];
    public IReadOnlyList<string> RecommendedActions => Result?.RecommendedActions ?? [];
    public IReadOnlyList<HistoricalMatch> HistoricalMatches => Result?.HistoricalMatches ?? [];
    public long MaxImagePreviewBytes => Math.Clamp(_settings.MaxImagePreviewMB, 1, 1_024) * 1024L * 1024L;
    public string SelectedJiraText => Result is null ? "" : _artifactWriter.GetJiraText(Result with
    {
        ExistingIssueReference = string.IsNullOrWhiteSpace(ExistingIssueReference) ? null : ExistingIssueReference.Trim(),
        Attachments = Files.Select(file => new AttachmentRecommendation(file.Name, file.IncludeAsAttachment, file.AttachmentReason)).ToArray()
    }, SelectedJiraMode == "Comment on Existing Jira" ? JiraMode.CommentOnExisting : JiraMode.CreateNew);

    private async Task ScanAsync()
    {
        ErrorMessage = "";
        Prepared = null;
        PreparedEvidence.Clear();
        SelectedEvidence = null;
        Result = null;
        HasScanned = false;
        OnPropertyChanged(nameof(HasScanned));
        Files.Clear();
        ProblemDescription = "";
        if (!Directory.Exists(SourceFolder))
        {
            ErrorMessage = "Select an existing, accessible incident folder.";
            return;
        }

        IsBusy = true;
        _cancellation = new CancellationTokenSource();
        try
        {
            StatusMessage = "Scanning the selected folder and classifying files...";
            var discovered = await _scanner.ScanAsync(SourceFolder, _cancellation.Token);
            foreach (var file in discovered)
            {
                var fileViewModel = new IncidentFileViewModel(file);
                fileViewModel.PropertyChanged += OnFilePropertyChanged;
                Files.Add(fileViewModel);
            }

            var problemFile = discovered.FirstOrDefault(file => file.Category == FileCategory.ProblemDescription);
            if (problemFile is not null)
            {
                try
                {
                    var sizeLimit = Math.Max(1, _settings.MaxFileSizeMB) * 1024L * 1024L;
                    if (problemFile.SizeBytes > sizeLimit)
                    {
                        ErrorMessage = $"problem.txt exceeds the configured {_settings.MaxFileSizeMB} MB limit and was not loaded.";
                    }
                    else
                    {
                        ProblemDescription = await File.ReadAllTextAsync(problemFile.FullPath, _cancellation.Token);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning("Unable to load problem description ({ErrorType}).", exception.GetType().Name);
                    ErrorMessage = "problem.txt was found but could not be read. You can still enter the description manually.";
                }
            }

            HasScanned = true;
            OnPropertyChanged(nameof(HasScanned));
            InvestigateCommand.NotifyCanExecuteChanged();
            StatusMessage = discovered.Count == 0 ? "The folder is empty. Add evidence or enter a problem description." : $"Found {discovered.Count} files. Review inclusion and attachment choices before preparing evidence.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Folder scan cancelled.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _logger.LogWarning("Incident folder scan failed ({ErrorType}).", exception.GetType().Name);
            ErrorMessage = exception.Message;
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }
    }

    private async Task PrepareInvestigationAsync()
    {
        ErrorMessage = "";
        Result = null;
        Prepared = null;
        PreparedEvidence.Clear();
        IsBusy = true;
        ProgressValue = 0;
        _cancellation = new CancellationTokenSource();
        try
        {
            var context = new IncidentContext(SourceFolder, Path.GetFileName(SourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                ProblemDescription, Files.Select(item => item.File with
                {
                    IncludeInInvestigation = item.IncludeInInvestigation,
                    RecommendAttachment = item.IncludeAsAttachment
                }).ToArray(), SystemName, IntegrationName);
            var progress = new Progress<InvestigationProgress>(update =>
            {
                ProgressStage = update.Stage;
                ProgressValue = (int)Math.Round(update.CompletedSteps * 100d / Math.Max(1, update.TotalSteps));
                StatusMessage = update.Message;
            });

            StatusMessage = "Preparing bounded evidence excerpts. No information is being sent to an AI provider.";
            Prepared = await _investigator.PrepareAsync(context, progress, _cancellation.Token);
            foreach (var excerpt in Prepared.Evidence)
            {
                PreparedEvidence.Add(excerpt);
            }

            SelectedEvidence = PreparedEvidence.FirstOrDefault();
            StatusMessage = $"Prepared {Prepared.Evidence.Count} excerpt(s). Review the problem description and each excerpt before explicitly sending it to the AI provider.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Evidence preparation cancelled. No evidence was sent.";
        }
        catch (Exception exception)
        {
            _logger.LogError("Evidence preparation failed ({ErrorType}).", exception.GetType().Name);
            ErrorMessage = exception.Message;
            StatusMessage = "Evidence preparation did not complete.";
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }
    }

    private async Task SendEvidenceAsync()
    {
        if (Prepared is null)
        {
            return;
        }

        ErrorMessage = "";
        IsBusy = true;
        ProgressValue = 0;
        _cancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<InvestigationProgress>(update =>
            {
                ProgressStage = update.Stage;
                ProgressValue = (int)Math.Round(update.CompletedSteps * 100d / Math.Max(1, update.TotalSteps));
                StatusMessage = update.Message;
            });
            StatusMessage = $"Sending reviewed evidence to {SelectedProvider}, model {SelectedModel}...";
            Result = await _investigator.InvestigateAsync(Prepared, progress, _cancellation.Token);
            ExistingIssueReference = Result.ExistingIssueReference ?? "";
            foreach (var recommendation in Result.Attachments)
            {
                var item = Files.FirstOrDefault(file => string.Equals(file.Name, recommendation.FileName, StringComparison.OrdinalIgnoreCase));
                item?.ApplyRecommendation(recommendation.Recommended, recommendation.Reason);
            }

            StatusMessage = "Investigation complete. Review conclusions, Jira mode, and attachment choices; no external action was taken.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "AI investigation cancelled. No output files were written.";
        }
        catch (Exception exception)
        {
            _logger.LogError("Incident investigation failed ({ErrorType}).", exception.GetType().Name);
            ErrorMessage = exception.Message;
            StatusMessage = "Investigation did not complete; no output files were written.";
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }
    }

    private void OnFilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(IncidentFileViewModel.IncludeInInvestigation))
        {
            InvalidatePrepared();
        }
        else if (eventArgs.PropertyName == nameof(IncidentFileViewModel.IncludeAsAttachment)
            || eventArgs.PropertyName == nameof(IncidentFileViewModel.AttachmentReason))
        {
            OnPropertyChanged(nameof(SelectedJiraText));
        }
    }

    private void InvalidatePrepared()
    {
        if (Prepared is null)
        {
            return;
        }

        Prepared = null;
        PreparedEvidence.Clear();
        SelectedEvidence = null;
        Result = null;
        StatusMessage = "Incident details changed. Prepare and review evidence again before sending.";
    }

    private async Task SaveOutputsAsync()
    {
        if (Result is null)
        {
            return;
        }

        ErrorMessage = "";
        IsBusy = true;
        _cancellation = new CancellationTokenSource();
        try
        {
            var updatedResult = Result with
            {
                ExistingIssueReference = string.IsNullOrWhiteSpace(ExistingIssueReference) ? null : ExistingIssueReference.Trim(),
                Attachments = Files.Select(file => new AttachmentRecommendation(file.Name, file.IncludeAsAttachment, file.AttachmentReason)).ToArray()
            };
            var mode = SelectedJiraMode == "Comment on Existing Jira" ? JiraMode.CommentOnExisting : JiraMode.CreateNew;
            if (mode == JiraMode.CommentOnExisting && string.IsNullOrWhiteSpace(updatedResult.ExistingIssueReference))
            {
                ErrorMessage = "Enter the existing Jira key before saving a Jira comment update.";
                return;
            }

            await _artifactWriter.SaveAsync(SourceFolder, updatedResult, mode, _cancellation.Token);
            if (mode == JiraMode.CommentOnExisting)
            {
                await _investigator.UpdateJiraReferenceAsync(Prepared?.Context.IncidentId ?? Path.GetFileName(SourceFolder),
                    updatedResult.ExistingIssueReference!, _cancellation.Token);
            }

            Result = updatedResult;
            StatusMessage = $"Investigation artifacts saved to {SourceFolder}.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Saving artifacts cancelled.";
        }
        catch (Exception exception)
        {
            _logger.LogError("Saving incident artifacts failed ({ErrorType}).", exception.GetType().Name);
            ErrorMessage = exception.Message;
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }
    }
}
