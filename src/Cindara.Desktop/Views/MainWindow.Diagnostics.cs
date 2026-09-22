using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Cindara.Core.Diagnostics;
using Cindara.Desktop.Localization;

namespace Cindara.Desktop.Views;

public partial class MainWindow
{
    private bool _diagnosticsOpen;
    private bool _exporting;

    private void OnDiagnostics(object? sender, RoutedEventArgs args) => ShowDiagnostics();

    private void DiagnosticsPage(string title)
    {
        if (!_diagnosticsOpen)
        {
            BeginModal(title);
            _diagnosticsOpen = true;
        }
        else
        {
            ModalActions.Children.Clear();
            ModalTitle.Text = title;
            AutomationProperties.SetName(ModalOverlay, title);
            _navigation.Forget("modal");
        }
    }

    private void DiagnosticText(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.Classes.Add("caption");
        ModalActions.Children.Add(block);
    }

    private DiagnosticEnvironment GetDiagnosticEnvironment()
    {
        return new DiagnosticEnvironment(
            typeof(App).Assembly.GetName().Version ?? new Version(1, 0),
            Environment.Version,
            typeof(Application).Assembly.GetName().Version ?? new Version(0, 0),
            OperatingSystem.IsWindows() ? DiagnosticPlatform.Windows
                : OperatingSystem.IsLinux() ? DiagnosticPlatform.Linux
                : OperatingSystem.IsMacOS() ? DiagnosticPlatform.MacOS : DiagnosticPlatform.Other,
            RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => DiagnosticArchitecture.X64,
                Architecture.Arm64 => DiagnosticArchitecture.Arm64,
                _ => DiagnosticArchitecture.Other,
            },
            DiagnosticRenderer.Skia,
            _controllerInput.IsAvailable,
            _controllerInput.ConnectedGamepads,
            _diagnostics?.StorageError is null);
    }

    private void ShowDiagnostics()
    {
        if (_diagnostics is null || _closed)
        {
            return;
        }

        DiagnosticsPage(Loc.Get("Diagnostics.Title"));
        DiagnosticText(Loc.Get("Diagnostics.Privacy"));
        var environment = GetDiagnosticEnvironment();
        DiagnosticText(Loc.Format("Diagnostics.Environment", environment.AppVersion, environment.RuntimeVersion,
            environment.Platform, environment.Architecture, environment.AvaloniaVersion, environment.Renderer,
            Loc.Get(environment.ControllerAvailable ? "Diagnostics.Available" : "Diagnostics.Unavailable"),
            environment.ConnectedControllers, environment.ControllerBackend));
        DiagnosticText(Loc.Get("Diagnostics.PlaybackUnavailable"));
        DiagnosticText(Loc.Get(environment.DiagnosticStorageAvailable ? "Diagnostics.StorageReady" : "Diagnostics.StorageFailed"));
        DiagnosticText(Loc.Get("Diagnostics.RecentErrors"));
        if (_diagnostics.RecentErrors.Count == 0)
        {
            DiagnosticText(Loc.Get("Diagnostics.NoErrors"));
        }
        else
        {
            var errors = string.Join("\n", _diagnostics.RecentErrors.Reverse().Select(entry =>
                $"{entry.Timestamp:u}  {entry.Area}/{entry.Action}: {entry.Outcome} "
                + $"[{string.Join(", ", entry.Errors)}]"));
            AddModalButton(Loc.Get("Diagnostics.RecentErrors"),
                () => ShowDiagnosticTextPage(Loc.Get("Diagnostics.RecentErrors"), errors, 0, ShowDiagnostics));
        }
        AddModalButton(Loc.Get("Diagnostics.Preview"), PreviewSupportBundle);
        AddModalButton(Loc.Get("Action.Back"), DismissModal);
        FocusModal();
    }

    private void PreviewSupportBundle()
    {
        if (_diagnostics is null)
        {
            return;
        }

        try
        {
            var bundle = SupportBundle.Create(_diagnostics, GetDiagnosticEnvironment());
            var name = $"cindara-support-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture)}.zip";
            var destination = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cindara", "support-bundles", name);
            ShowBundlePreview(bundle, destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            ShowDiagnosticFailure(DiagnosticAction.PreviewBundle, exception);
        }
    }

    private void ShowBundlePreview(SupportBundle bundle, string destination)
    {
        DiagnosticsPage(Loc.Get("Diagnostics.Preview"));
        DiagnosticText(Loc.Get("Diagnostics.PreviewNotice"));
        DiagnosticText(Loc.Format("Diagnostics.Destination", destination));
        foreach (var (name, content) in bundle.Files)
        {
            AddModalButton(Loc.Format("Diagnostics.File", name, Encoding.UTF8.GetByteCount(content)),
                () => ShowBundleFile(bundle, destination, name, 0));
        }

        AddModalButton(Loc.Get("Diagnostics.Export"), async () => await ExportSupportBundleAsync(bundle, destination));
        AddModalButton(Loc.Get("Action.Cancel"), ShowDiagnostics);
        FocusModal();
    }

    private void ShowBundleFile(SupportBundle bundle, string destination, string name, int page)
        => ShowDiagnosticTextPage(name, bundle.Files[name], page, () => ShowBundlePreview(bundle, destination));

    private void ShowDiagnosticTextPage(string name, string content, int page, Action back)
    {
        const int pageSize = 500;
        var pages = Math.Max(1, (content.Length + pageSize - 1) / pageSize);
        DiagnosticsPage(Loc.Format("Diagnostics.FilePage", name, page + 1, pages));
        DiagnosticText(content.Substring(page * pageSize, Math.Min(pageSize, content.Length - page * pageSize)));
        if (page > 0)
        {
            AddModalButton(Loc.Get("Diagnostics.PreviousPage"), () => ShowDiagnosticTextPage(name, content, page - 1, back));
        }

        if (page + 1 < pages)
        {
            AddModalButton(Loc.Get("Diagnostics.NextPage"), () => ShowDiagnosticTextPage(name, content, page + 1, back));
        }

        AddModalButton(Loc.Get("Action.Back"), back);
        FocusModal();
    }

    private async Task ExportSupportBundleAsync(SupportBundle bundle, string destination)
    {
        if (_exporting)
        {
            return;
        }

        _exporting = true;
        DiagnosticsPage(Loc.Get("Diagnostics.Exporting"));
        DiagnosticText(Loc.Format("Diagnostics.Destination", destination));
        try
        {
            await bundle.ExportAsync(destination);
            _diagnostics?.Record(DiagnosticArea.Support, DiagnosticAction.ExportBundle, DiagnosticOutcome.Completed);
            if (!_closed)
            {
                DiagnosticsPage(Loc.Get("Diagnostics.Exported"));
                DiagnosticText(Loc.Format("Diagnostics.Destination", destination));
                DiagnosticText(Loc.Get("Diagnostics.ShareNotice"));
                AddModalButton(Loc.Get("Action.Back"), ShowDiagnostics);
                FocusModal();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!_closed)
            {
                ShowDiagnosticFailure(DiagnosticAction.ExportBundle, exception);
            }
            else
            {
                _diagnostics?.Record(DiagnosticArea.Support, DiagnosticAction.ExportBundle,
                    DiagnosticOutcome.Failed, DiagnosticLevel.Error, exception);
            }
        }
        finally
        {
            _exporting = false;
        }
    }

    private void ShowDiagnosticFailure(DiagnosticAction action, Exception exception)
    {
        _diagnostics?.Record(DiagnosticArea.Support, action, DiagnosticOutcome.Failed, DiagnosticLevel.Error, exception);
        DiagnosticsPage(Loc.Get("Diagnostics.Failed"));
        DiagnosticText(Loc.Get("Diagnostics.FailureHelp"));
        AddModalButton(Loc.Get("Action.Back"), ShowDiagnostics);
        FocusModal();
    }
}
