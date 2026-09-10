using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Rune.Voice;

public partial class LauncherWindow
{
    private RuneUpdate? availableUpdate;
    private bool checkingUpdate;
    private readonly CancellationTokenSource updateLifetime = new();

    private async Task CheckRuneUpdate(bool manual)
    {
        if (checkingUpdate) return;
        if (!File.Exists(Path.Combine(RuneUpdates.Root, "release.json"))) { UpdateStatus.Text = "Update checks are available in installed Rune releases."; return; }
        checkingUpdate = true; UpdateRuneButton.IsEnabled = false; UpdateStatus.Text = "Checking for Rune updates…"; RefreshNotifications();
        try
        {
            availableUpdate = await RuneUpdates.Check(RuneUpdates.Root, updateLifetime.Token);
            notificationUpdateFailed = false;
            UpdateStatus.Text = availableUpdate == null ? "Rune " + RuneUpdates.Current(RuneUpdates.Root) + " · no newer in-app update available" : "Rune " + availableUpdate.Version + " is available · app and game plugin";
            UpdateRuneButton.Content = availableUpdate == null ? "Check for updates" : "Update Rune";
        }
        catch (OperationCanceledException) { if (!updateLifetime.IsCancellationRequested) { notificationUpdateFailed = true; UpdateStatus.Text = "Update check timed out. Try again later."; } }
        catch (Exception) { notificationUpdateFailed = true; UpdateStatus.Text = manual ? "Could not check for updates. Check your connection and retry." : "Update check unavailable · you can retry here later"; }
        finally { checkingUpdate = false; UpdateRuneButton.IsEnabled = true; RefreshNotifications(); }
    }
    private async void UpdateRune_Click(object sender, RoutedEventArgs e)
    {
        if (availableUpdate == null) { await CheckRuneUpdate(true); return; }
        var update = availableUpdate;
        var body = new StackPanel();
        body.Children.Add(DialogText("Rune " + update.Version + " updates the desktop app and Rune's game plugin together. Your companions, conversations, profiles, settings and installed voice models are kept."));
        var status = DialogText("Close Valheim before installing. Rune will save your current companion edits, close briefly and reopen.", true); status.Margin = new Thickness(0, 14, 0, 14); body.Children.Add(status);
        var buttons = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var notes = DialogButton("Release notes"); var cancel = DialogButton("Cancel"); var install = DialogButton("Download update");
        buttons.Children.Add(notes); buttons.Children.Add(cancel); buttons.Children.Add(install); body.Children.Add(buttons);
        var dialog = CreateRuneDialog("Update Rune", body);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
        bool downloading = false; string? stage = null;
        dialog.Closed += (_, _) => cancellation.Cancel();
        cancel.Click += (_, _) => dialog.Close();
        notes.Click += (_, _) => Process.Start(new ProcessStartInfo(update.Notes) { UseShellExecute = true });
        install.Click += async (_, _) =>
        {
            if (downloading) return;
            if (RuneUpdates.GameRunning()) { status.Text = "Save and close Valheim, then click again. No files have been changed."; return; }
            downloading = true; install.IsEnabled = false;
            try
            {
                stage ??= await RuneUpdates.Download(update, new Progress<string>(text => status.Text = text), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (RuneUpdates.GameRunning()) { status.Text = "Update ready. Save and close Valheim, then click Install update."; install.Content = "Install update"; return; }
                if (selectedProfile != null) await runtime.ShellSaveProfile(DraftProfile());
                cancellation.Token.ThrowIfCancellationRequested();
                voicePreview?.Cancel();
                Process.Start(RuneUpdates.Worker(stage, RuneUpdates.Root));
                dialog.Close(); Close();
            }
            catch (OperationCanceledException) { status.Text = "Update cancelled. Installed files were not changed."; }
            catch (Exception error) { status.Text = "Update could not start: " + error.Message; }
            finally { downloading = false; install.IsEnabled = true; }
        };
        dialog.ShowDialog();
    }
}
