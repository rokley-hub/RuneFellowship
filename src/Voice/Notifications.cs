using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;

namespace Rune.Voice;

public partial class LauncherWindow
{
    private sealed record LauncherNotice(string Title, string Detail, string Action, string Target);
    private string notificationDependencyIssue = "";
    private string notificationProfileError = "";
    private bool notificationUpdateFailed;
    private bool notificationCatalogFailed;
    private bool notificationBatchChecking;
    private List<LauncherNotice> currentNotifications = new();

    private static List<LauncherNotice> ComposeNotifications(string? appVersion, int modUpdates, string profile,
        string dependencyIssue, string profileError, bool appCheckFailed, bool catalogFailed, bool nexus)
    {
        var notices = new List<LauncherNotice>();
        if (appVersion != null) notices.Add(new("Rune update available", "Version " + appVersion + " is ready to review.", "Review app update", "app"));
        if (profile.Length == 0) notices.Add(new("Choose a mod profile", "Create or import a profile to prepare your game.", "Open profiles", "mods"));
        if (profileError.Length > 0) notices.Add(new("Profile could not be read", "Open Mods to check the selected profile and retry.", "Review profile", "mods"));
        if (dependencyIssue.Length > 0) notices.Add(new("Mod dependencies need attention", profile + " · " + dependencyIssue, "Review mods", "mods"));
        if (!nexus && modUpdates > 0) notices.Add(new(modUpdates == 1 ? "1 mod update available" : modUpdates + " mod updates available", "Updates found for " + profile + ". Review them before installing.", "Review mod updates", "mod-updates"));
        if (appCheckFailed) notices.Add(new("App update check unavailable", "Rune could not check for a newer version. Check your connection and retry.", "Open app updates", "app"));
        if (!nexus && catalogFailed) notices.Add(new("Mod update check unavailable", "The last catalogue refresh failed. Any listed updates may be based on saved results.", "Open Mods", "mods"));
        return notices;
    }

    private void RefreshNotifications()
    {
        if (NotificationsButton == null) return;
        var profile = ModsProfileCombo.SelectedItem as ProfileChoice;
        currentNotifications = ComposeNotifications(availableUpdate?.Version,
            profile == null || UseNexus ? 0 : AvailableUpdates().Count, profile?.Label ?? "",
            notificationDependencyIssue, notificationProfileError, notificationUpdateFailed, notificationCatalogFailed, UseNexus);
        NotificationsLabel.Text = currentNotifications.Count == 0 ? "Notifications" : "Notifications · " + currentNotifications.Count;
        NotificationsButton.Foreground = (Brush)FindResource(currentNotifications.Count == 0 ? "Muted" : "Gold");
        System.Windows.Automation.AutomationProperties.SetName(NotificationsButton, currentNotifications.Count + " notifications. Updates and important information.");
        NotificationsCheckButton.IsEnabled = !notificationBatchChecking && !checkingUpdate && !catalogRefreshRunning;
        NotificationsCheckButton.Content = NotificationsCheckButton.IsEnabled ? "Check for updates" : "Checking…";
        NotificationsScope.Text = UseNexus
            ? "Nexus mod versions must be checked on the website. Rune app updates are checked here."
            : "Mod notices apply to the selected profile and the last catalogue check. Imported ZIP versions may need a website check.";
        RenderNotifications();
    }

    private void RenderNotifications()
    {
        NotificationsList.Children.Clear();
        if (currentNotifications.Count == 0) {
            NotificationsList.Children.Add(new TextBlock { Text = "No new notifications.", Margin = new Thickness(0, 8, 0, 0), Foreground = (Brush)FindResource("Bone") });
            return;
        }
        foreach (var notice in currentNotifications) {
            var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 12) };
            panel.Children.Add(new TextBlock { Text = notice.Title, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Bone"), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = notice.Detail, FontSize = 13, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10) });
            var action = new WpfButton { Content = notice.Action, Tag = notice.Target, Style = (Style)FindResource("RuneButton"), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            action.Click += NotificationAction_Click;
            panel.Children.Add(action); NotificationsList.Children.Add(panel);
        }
    }

    private void Notifications_Click(object sender, RoutedEventArgs e)
    {
        RefreshNotifications(); NotificationsPopup.IsOpen = !NotificationsPopup.IsOpen;
    }

    private void NotificationAction_Click(object sender, RoutedEventArgs e)
    {
        NotificationsPopup.IsOpen = false;
        if (((FrameworkElement)sender).Tag?.ToString() == "app") {
            ShowPage("Settings"); SelectSection(SettingsSections, SettingsTabs, "Support");
        } else {
            ShowPage("Mods"); SetModsMode(false);
            if (((FrameworkElement)sender).Tag?.ToString() == "mod-updates" && !UseNexus) ModsFilterCombo.SelectedIndex = 3;
        }
    }

    private async void NotificationsCheck_Click(object sender, RoutedEventArgs e)
    {
        if (notificationBatchChecking) return;
        notificationBatchChecking = true; RefreshNotifications();
        try {
            await CheckRuneUpdate(true);
            if (!UseNexus) await RefreshCatalog();
        } finally { notificationBatchChecking = false; RefreshNotifications(); }
    }

    internal void PreviewNotifications()
    {
        currentNotifications = ComposeNotifications("0.4.99-beta", 3, "Example profile", "", "", false, false, false);
        NotificationsLabel.Text = "Notifications · " + currentNotifications.Count;
        NotificationsButton.Foreground = (Brush)FindResource("Gold");
        RenderNotifications(); NotificationsPopup.IsOpen = true;
    }

    private void CheckNotifications(Dictionary<string, bool> checks)
    {
        var combined = ComposeNotifications("0.4.99-beta", 3, "Example", "Missing required library", "", false, false, false);
        checks["notificationsIncludeAppModsAndDependencies"] = combined.Count == 3 && combined.Select(n => n.Target).ToHashSet().SetEquals(new[] { "app", "mods", "mod-updates" });
        checks["resolvedNotificationsDisappear"] = ComposeNotifications(null, 0, "Example", "", "", false, false, false).Count == 0;
        checks["nexusDoesNotClaimAutomaticModUpdates"] = !ComposeNotifications(null, 3, "Example", "", "", false, true, true).Any(n => n.Target == "mod-updates" || n.Title == "Mod update check unavailable");
        checks["failedChecksAreNotAnAllClear"] = ComposeNotifications(null, 0, "Example", "", "", true, true, false).Count == 2;
        checks["profileReadErrorIsActionable"] = ComposeNotifications(null, 0, "Example", "", "Read failure", false, false, false).Single().Target == "mods";
        checks["noProfileOffersSetup"] = ComposeNotifications(null, 0, "", "", "", false, false, false).Single().Target == "mods";
        var buttonBounds = NotificationsButton.TransformToAncestor(this).TransformBounds(new Rect(0, 0, NotificationsButton.ActualWidth, NotificationsButton.ActualHeight));
        var titleBounds = TitleRuntimeStatus.TransformToAncestor(this).TransformBounds(new Rect(0, 0, TitleRuntimeStatus.ActualWidth, TitleRuntimeStatus.ActualHeight));
        checks["notificationButtonFitsTitleBar"] = buttonBounds.Left > titleBounds.Right + 12 && buttonBounds.Bottom <= 42 && buttonBounds.Right < ActualWidth - 132;
    }
}
