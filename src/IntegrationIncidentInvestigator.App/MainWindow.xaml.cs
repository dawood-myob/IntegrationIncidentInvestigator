using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using IntegrationIncidentInvestigator.App.ViewModels;
using Microsoft.Win32;

namespace IntegrationIncidentInvestigator.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public MainWindowViewModel ViewModel { get; }

    private void Browse_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the incident source folder",
            InitialDirectory = Directory.Exists(ViewModel.SourceFolder) ? ViewModel.SourceFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.SourceFolder = dialog.FolderName;
        }
    }

    private void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        ScreenshotImage.Source = null;
        if (FilesGrid.SelectedItem is not IncidentFileViewModel file || !file.IsImage || !File.Exists(file.File.FullPath))
        {
            return;
        }

        if (file.File.SizeBytes > ViewModel.MaxImagePreviewBytes)
        {
            ViewModel.ErrorMessage = $"The selected image exceeds the {ViewModel.MaxImagePreviewBytes / (1024 * 1024)} MB preview limit. It was not sent to the AI provider.";
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 600;
            image.UriSource = new Uri(file.File.FullPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            ScreenshotImage.Source = image;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or FormatException or InvalidOperationException)
        {
            ViewModel.ErrorMessage = "The selected image could not be previewed. It was not sent to the AI provider.";
        }
    }

    private void CopyTeams_Click(object sender, RoutedEventArgs eventArgs) => CopyText(ViewModel.TeamsSummary);

    private void CopyJira_Click(object sender, RoutedEventArgs eventArgs) => CopyText(ViewModel.SelectedJiraText);

    private void CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            ViewModel.StatusMessage = "Content copied to the clipboard.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ViewModel.ErrorMessage = "Clipboard is unavailable. Try copying again.";
        }
    }
}
