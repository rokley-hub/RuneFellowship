using System.Windows;
using System.Windows.Controls;

namespace Rune.Voice;
public partial class LauncherWindow
{
    internal Dictionary<string, bool> CheckPreviewLayout()
    {
        Rect Bounds(FrameworkElement e) => e.TransformToAncestor(this).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
        var checks = new Dictionary<string, bool>();
        checks["windowHasRuneIcon"] = Icon != null;
        if (ModsPage.IsVisible) {
            checks["sourceSpecificBrowseSurface"] = NexusBrowsePanel.IsVisible == (UseNexus && browsingMods) && ModsList.IsVisible == !(UseNexus && browsingMods);
            checks["sourceSpecificInstallAction"] = InstallModButton.Content.ToString() == (UseNexus ? "Import ZIP" : "Install selected");
            checks["modListFitsPage"] = Bounds(ModsList).Right <= Bounds(ModsPage).Right + 1;
            checks["footerFitsPage"] = Bounds(ModNoticeText).Bottom <= Bounds(ModsPage).Bottom + 1;
        }
        if (SettingsPage.IsVisible) {
            checks["modPreferenceReflectsSavedValue"] = ModSourceCombo.SelectedIndex == (UseNexus ? 1 : 0);
            IEnumerable<FrameworkElement> Descendants(DependencyObject parent) {
                for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++) {
                    var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);
                    if(child is FrameworkElement element) yield return element;
                    foreach(var nested in Descendants(child)) yield return nested;
                }
            }
            var controls=Descendants(SettingsPage).Where(e=>e.IsVisible && (e is System.Windows.Controls.Button || e is System.Windows.Controls.ComboBox || e is System.Windows.Controls.CheckBox));
            checks["allSettingsControlsInsidePage"] = controls.All(e=>Bounds(e).Bottom <= Bounds(SettingsPage).Bottom+1 && Bounds(e).Right <= Bounds(SettingsPage).Right+1);
            checks["settingsHasNoScrolling"] = !Descendants(SettingsPage).OfType<ScrollViewer>().Any(e=>e.IsVisible && (e.ScrollableHeight>0 || e.ScrollableWidth>0));
        }
        if (PlayPage.IsVisible) {
            foreach (var status in new[] { ValheimReady, LocalReady, CloudReady, VoiceReady }) {
                var grid = (Grid)status.Parent;
                var label = grid.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 0 && Grid.GetRow(t) == 0);
                checks[status.Name + "DoesNotOverlapLabel"] = !Bounds(status).IntersectsWith(Bounds(label));
            }
            checks["voiceOutputHasSeparateRow"] = Bounds(VoiceOutput).Top >= Bounds(VoiceReady).Bottom;
        }
        if (CompanionsPage.IsVisible) {
            checks["perCompanionMapToggleFits"] = Bounds(TrackCompanionCheck).Bottom <= Bounds(CompanionsPage).Bottom;
            checks["unsummonButtonFits"] = UnsummonCompanionButton.IsVisible && Bounds(UnsummonCompanionButton).Right <= Bounds(CompanionsPage).Right && Bounds(UnsummonCompanionButton).Bottom <= Bounds(CompanionsPage).Bottom;
            var original = DraftProfile(); string notice = CompanionNotice.Text;
            var previousTracking = runtime.ShellProfiles().ToDictionary(p => p.Id, p => p.ShowOnMap);
            TrackCompanionCheck.IsChecked = !original.ShowOnMap;
            var savedTracking = ProfileStore.Load(System.IO.Path.Combine(runtime.ShellBridgeFolder, "preferences.json"));
            checks["trackingSavedImmediately"] = savedTracking.Profiles.Single(p => p.Id == original.Id).ShowOnMap == !original.ShowOnMap;
            checks["trackingLeavesOtherCompanionsUnchanged"] = savedTracking.Profiles.Where(p => p.Id != original.Id).All(p => p.ShowOnMap == previousTracking[p.Id]);
            checks["trackingSurvivesDraftCopy"] = DraftProfile().Copy().ShowOnMap == !original.ShowOnMap;
            TrackCompanionCheck.IsChecked = original.ShowOnMap;
            VoiceEngineCombo.SelectedIndex = 1;
            checks["turboOffers28References"] = VoiceCombo.Items.Count == 28 && VoiceChoiceLabel.Text.Contains("Reference");
            VoiceCombo.SelectedIndex = Array.IndexOf(voiceIds, "am_onyx");
            checks["onyxDraftIsMale"] = DraftProfile().Voice == "am_onyx" && IsMaleVoice(DraftProfile());
            VoiceEngineCombo.SelectedIndex = 0;
            checks["kokoroOffersSixChoices"] = VoiceCombo.Items.Count == 6 && VoiceCatalog.ForEngine("kokoro").Contains(DraftProfile().Voice);
            VoiceEngineCombo.SelectedIndex = 1;
            checks["engineSwitchRemembersDraftVoice"] = DraftProfile().Voice == "am_onyx";
            VoiceEngineCombo.SelectedIndex = 2;
            checks["v3Offers28References"] = VoiceCombo.Items.Count == 28 && DraftProfile().Voice == "am_onyx";
            LoadProfile(original); CompanionNotice.Text = notice; UpdateLayout();
            checks["personalityCountBelowField"] = Bounds(PersonalityCount).Top >= Bounds(PersonalityBox).Bottom;
            checks["personalityCountAboveRole"] = Bounds(PersonalityCount).Bottom <= Bounds((FrameworkElement)RoleCombo.Parent).Top;
            var footer = (Grid)CompanionNotice.Parent;
            var buttons = footer.Children.OfType<StackPanel>().Single();
            checks["previewNoticeExcludesButtons"] = Bounds(CompanionNotice).Right <= Bounds(buttons).Left;
            checks["previewNoticeWithinPage"] = Bounds(CompanionNotice).Bottom <= Bounds(CompanionsPage).Bottom;
            checks["personalityFieldUsableHeight"] = PersonalityBox.ActualHeight >= 65;
        }
        if (CommandsPage.IsVisible) {
            var seen = new HashSet<string>();
            foreach (string group in CommandGroupsCombo.Items) {
                CommandGroupsCombo.SelectedItem = group;
                foreach (CommandChoice choice in CommandActionList.Items) {
                    CommandActionList.SelectedItem = choice; UpdateLayout(); seen.Add(choice.Ability.action);
                    checks[choice.Ability.action + "Fits"] = Bounds(CommandHint).Bottom <= Bounds((FrameworkElement)((Grid)CommandHint.Parent).Children.Cast<FrameworkElement>().Single(e => Grid.GetRow(e) == 5)).Top;
                }
            }
            checks["allPublicAbilitiesIncluded"] = seen.SetEquals(Rune.Shared.Rules.Capabilities.Where(c => !c.hidden).Select(c => c.action));
            CommandGroupsCombo.SelectedIndex = 0; UpdateLayout();
        }
        checks["passed"] = checks.Values.All(v => v);
        return checks;
    }
}
