using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfButton = System.Windows.Controls.Button;

namespace Rune.Voice;

public partial class LauncherWindow
{
    private string footerPortraitKey = "";

    private void UpdateEmberAnimation()
    {
        RuneEmberHalo.BeginAnimation(OpacityProperty, null);
        RuneEmberCore.BeginAnimation(OpacityProperty, null);
        RuneEmberHalo.Opacity = 0.15;
        RuneEmberCore.Opacity = 0.65;
        // Fade the cached core as well as its halo so the rune rests between flares.
        // Respect Windows' animation preference and stop work while minimized.
        if (WindowState == WindowState.Minimized || !SystemParameters.ClientAreaAnimation) return;
        var halo = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4.8), RepeatBehavior = RepeatBehavior.Forever };
        var core = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4.8), RepeatBehavior = RepeatBehavior.Forever };
        foreach (var (seconds, glow, light) in new[] { (0.0, 0.02, 0.12), (0.8, 0.02, 0.12), (2.0, 1.0, 1.0), (2.5, 0.7, 0.85), (3.1, 0.85, 0.95), (4.4, 0.02, 0.12), (4.8, 0.02, 0.12) }) {
            halo.KeyFrames.Add(new EasingDoubleKeyFrame(glow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            core.KeyFrames.Add(new EasingDoubleKeyFrame(light, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        }
        RuneEmberHalo.BeginAnimation(OpacityProperty, halo);
        RuneEmberCore.BeginAnimation(OpacityProperty, core);
    }
    private bool changingCompanionPresence;

    private void RefreshPresence(Rune.Shared.GameState state)
    {
        bool summoned = selectedProfile != null && state.roster.Any(c => c.id == selectedProfile.Id);
        CompanionPresenceButton.Content = changingCompanionPresence ? "Please wait…" : summoned ? "Unsummon" : "Summon";
        CompanionPresenceButton.IsEnabled = !changingCompanionPresence && state.ready && selectedProfile != null;
        CompanionPresenceButton.ToolTip = !state.ready ? "Enter a Valheim world to summon or dismiss this companion."
            : summoned ? "Dismiss this companion and keep their profile. Cargo and borrowed tools must be returned first; personal equipment stays on the ground."
            : "Save this companion's edits and summon them into the game.";
    }

    private async void CompanionPresence_Click(object sender, RoutedEventArgs e)
    {
        if (changingCompanionPresence || selectedProfile == null) return;
        var state = runtime.ShellState();
        if (!state.ready) { RefreshPresence(state); return; }
        string id = selectedProfile.Id;
        bool summoned = state.roster.Any(c => c.id == id);
        var draft = DraftProfile();
        changingCompanionPresence = true; RefreshPresence(state);
        try {
            string result;
            if (summoned) result = await runtime.ShellUnsummon(id);
            else { await runtime.ShellSaveProfile(draft); await runtime.ShellSummon(id); result = "Summon request sent. Check the game response in Conversation."; }
            if (selectedProfile?.Id == id) CompanionNotice.Text = result;
        }
        catch (Exception error) { if (selectedProfile?.Id == id) CompanionNotice.Text = error.Message; }
        finally { changingCompanionPresence = false; RefreshPresence(runtime.ShellState()); }
    }

    private void AiModeCard_Click(object sender, RoutedEventArgs e)
    {
        AiModeCombo.SelectedIndex = ((FrameworkElement)sender).Tag?.ToString() switch { "chatgpt" => 2, "hybrid" => 1, _ => 0 };
    }

    private void RefreshAiCards()
    {
        foreach (WpfButton button in AiModeCards.Children)
            button.Style = (Style)FindResource(button.Tag?.ToString() == runtime.ShellAiMode ? "SelectedModeButton" : "RuneButton");
    }

    private void SelectSection(System.Windows.Controls.Panel sections, System.Windows.Controls.Panel tabs, string key)
    {
        foreach (FrameworkElement panel in sections.Children)
            panel.Visibility = panel.Tag?.ToString() == key ? Visibility.Visible : Visibility.Collapsed;
        foreach (WpfButton button in tabs.Children)
            button.Style = (Style)FindResource(button.Tag?.ToString() == key ? "SelectedModeButton" : "RuneButton");
    }

    private void SettingsSection_Click(object sender, RoutedEventArgs e) =>
        SelectSection(SettingsSections, SettingsTabs, ((FrameworkElement)sender).Tag?.ToString() ?? "AI");

    private void CompanionSection_Click(object sender, RoutedEventArgs e) =>
        SelectSection(CompanionSections, CompanionTabs, ((FrameworkElement)sender).Tag?.ToString() ?? "Personality");

    private void ProfileOptions_Click(object sender, RoutedEventArgs e)
    {
        ProfileOptionsPopup.PlacementTarget = (UIElement)sender;
        ProfileOptionsPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ProfileOptionsPopup.IsOpen = !ProfileOptionsPopup.IsOpen;
    }

    private void OpenModsPage_Click(object sender, RoutedEventArgs e) => ShowPage("Mods");
    private void ReportProblem_Click(object sender, RoutedEventArgs e) =>
        OpenHelpPage("https://github.com/rokley-hub/RuneFellowship/issues");
    private void PlayerGuide_Click(object sender, RoutedEventArgs e) =>
        OpenHelpPage("https://thunderstore.io/c/valheim/p/RuneFellowship/RuneFellowship/wiki/5843-getting-started-and-player-guide/");

    private void OpenHelpPage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) { ShowRuneMessage(error.Message, "Could not open the browser"); }
    }
}

public sealed partial class RuneWindow
{
    // Read the actual lifecycle: an enabled microphone is not proof of capture,
    // and preparing a voice is not yet audible playback.
    internal string ShellActivity => neural.IsSpeaking || speaker.State == System.Speech.Synthesis.SynthesizerState.Speaking
        ? "Speaking" : ReplyPlaying ? "Preparing voice" : busyGeneration == generation ? "Thinking"
        : recognizing && !AlwaysOn ? "Transcribing" : collecting || (AlwaysOn && recognizing && !microphoneMuted) ? "Listening"
        : microphoneMuted || microphoneMode.SelectedIndex == 0 ? "Muted"
        : AlwaysOn ? "Mic offline" : "Ready";
}
