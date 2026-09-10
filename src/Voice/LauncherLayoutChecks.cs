using System.Windows;
using System.Windows.Controls;

namespace Rune.Voice;
public partial class LauncherWindow
{
    internal Dictionary<string, bool> CheckPreviewLayout()
    {
        Rect Bounds(FrameworkElement e) => e.TransformToAncestor(this).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
        var checks = new Dictionary<string, bool>();
        CheckNotifications(checks);
        checks["windowHasRuneIcon"] = Icon != null;
        checks["emberAnimationRespectsMotionPreference"] = RuneEmberHalo.HasAnimatedProperties == SystemParameters.ClientAreaAnimation;
        if (ModsPage.IsVisible) {
            checks["sourceSpecificBrowseSurface"] = NexusBrowsePanel.IsVisible == (UseNexus && browsingMods) && ModsList.IsVisible == !(UseNexus && browsingMods);
            checks["sourceSpecificInstallAction"] = InstallModButton.Content.ToString() == (UseNexus ? "Import ZIP" : "Install selected");
            checks["modListFitsPage"] = Bounds(ModsList).Right <= Bounds(ModsPage).Right + 1;
            checks["footerFitsPage"] = Bounds(ModNoticeText).Bottom <= Bounds(ModsPage).Bottom + 1;
        }
        if (SettingsPage.IsVisible) {
            var activeSection = SettingsSections.Children.Cast<FrameworkElement>().First(e => e.IsVisible).Tag?.ToString() ?? "AI";
            checks["modPreferenceReflectsSavedValue"] = ModSourceCombo.SelectedIndex == (UseNexus ? 1 : 0);
            IEnumerable<FrameworkElement> Descendants(DependencyObject parent) {
                for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++) {
                    var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);
                    if(child is FrameworkElement element) yield return element;
                    foreach(var nested in Descendants(child)) yield return nested;
                }
            }
            foreach (var section in new[] { "AI", "Voice", "Controls", "Behaviour", "Mods", "Support" }) {
                SelectSection(SettingsSections, SettingsTabs, section); UpdateLayout();
                var controls=Descendants(SettingsPage).Where(e=>e.IsVisible && (e is System.Windows.Controls.Button || e is System.Windows.Controls.ComboBox || e is System.Windows.Controls.CheckBox));
                checks[section + "ControlsInsidePage"] = controls.All(e=>Bounds(e).Bottom <= Bounds(SettingsPage).Bottom+1 && Bounds(e).Right <= Bounds(SettingsPage).Right+1);
                checks[section + "HasNoScrolling"] = !Descendants(SettingsPage).OfType<ScrollViewer>().Any(e=>e.IsVisible && (e.ScrollableHeight>0 || e.ScrollableWidth>0));
                if (section == "Controls") {
                    var bindings = (Grid)MicKeyButton.Parent;
                    foreach (var label in bindings.Children.OfType<TextBlock>()) {
                        var key = bindings.Children.OfType<System.Windows.Controls.Button>().Single(b => Grid.GetRow(b) == Grid.GetRow(label));
                        checks[$"keybind{Grid.GetRow(label)}LabelHasClearGap"] = Bounds(label).Right + 12 <= Bounds(key).Left;
                        checks[$"keybind{Grid.GetRow(label)}RowIsAligned"] = Math.Abs((Bounds(label).Top + Bounds(label).Bottom) / 2 - (Bounds(key).Top + Bounds(key).Bottom) / 2) < 2;
                    }
                }
            }
            SelectSection(SettingsSections, SettingsTabs, activeSection); UpdateLayout();
        }
        if (PlayPage.IsVisible) {
            // Exercise the real card click, including refreshes that used to reselect
            // the editor's companion and silently undo the requested voice target.
            var profiles = runtime.ShellProfiles();
            var previousTarget = runtime.ShellSelectedCompanionId;
            var previousDraft = DraftProfile();
            PersonalityBox.Text = previousDraft.Personality + " Keep this unsaved edit.";
            var draftText = PersonalityBox.Text;
            checks["playHasMultipleVoiceTargets"] = profiles.Length >= 2;
            foreach (int index in Enumerable.Range(0, Math.Min(3, profiles.Length))) {
                var card = (Border)PlayCompanionCards.Children[index];
                card.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) {
                    RoutedEvent = System.Windows.UIElement.MouseLeftButtonUpEvent
                });
                RefreshRuntimeState();
                checks[$"playCard{index}SelectsVoiceTarget"] = runtime.ShellSelectedCompanionId == profiles[index].Id;
                checks[$"playCard{index}UpdatesFooter"] = TalkTargetText.Text == profiles[index].Name && ConversationTitle.Text == "Conversation with " + profiles[index].Name;
                checks[$"playCard{index}PreservesEditorDraft"] = selectedProfile?.Id == previousDraft.Id && PersonalityBox.Text == draftText;
                checks[$"playCard{index}HasSingleHighlight"] = PlayCompanionCards.Children.Cast<Border>().Count(c => c.BorderThickness.Left == 2) == 1
                    && ((Border)PlayCompanionCards.Children[index]).BorderThickness.Left == 2;
            }
            LoadProfile(previousDraft); runtime.ShellSelectCompanion(previousTarget); RefreshRuntimeState(); UpdateLayout();
            foreach (var status in new[] { ValheimReady, LocalReady, CloudReady, VoiceReady }) {
                var grid = (Grid)status.Parent;
                var label = grid.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 0 && Grid.GetRow(t) == 0);
                checks[status.Name + "DoesNotOverlapLabel"] = !Bounds(status).IntersectsWith(Bounds(label));
            }
            checks["voiceOutputHasSeparateRow"] = Bounds(VoiceOutput).Top >= Bounds(VoiceReady).Bottom;
        }
        if (CompanionsPage.IsVisible) {
            var activeTab = CompanionSections.Children.Cast<FrameworkElement>().First(e => e.IsVisible).Tag?.ToString() ?? "Personality";
            checks["perCompanionMapToggleFits"] = Bounds(TrackCompanionCheck).Bottom <= Bounds(CompanionsPage).Bottom;
            checks["presenceButtonFits"] = CompanionPresenceButton.IsVisible && Bounds(CompanionPresenceButton).Right <= Bounds(CompanionsPage).Right && Bounds(CompanionPresenceButton).Bottom <= Bounds(CompanionsPage).Bottom;
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
            SelectSection(CompanionSections, CompanionTabs, "Personality"); UpdateLayout();
            checks["personalityCountBelowField"] = Bounds(PersonalityCount).Top >= Bounds(PersonalityBox).Bottom;
            checks["personalityCountInsidePage"] = Bounds(PersonalityCount).Bottom <= Bounds(CompanionsPage).Bottom;
            var footer = (Grid)CompanionNotice.Parent;
            var buttons = footer.Children.OfType<StackPanel>().Single();
            checks["previewNoticeExcludesButtons"] = Bounds(CompanionNotice).Right <= Bounds(buttons).Left;
            checks["previewNoticeWithinPage"] = Bounds(CompanionNotice).Bottom <= Bounds(CompanionsPage).Bottom;
            checks["personalityFieldUsableHeight"] = PersonalityBox.ActualHeight >= 65;
            checks["removeCompanionFits"] = Bounds(RemoveCompanionButton).Bottom <= Bounds(CompanionsPage).Bottom;
            foreach (var tab in new[] { "Voice", "Behaviour", "Personality" }) {
                SelectSection(CompanionSections, CompanionTabs, tab); UpdateLayout();
                checks[tab + "KeepsDraft"] = DraftProfile().Personality == original.Personality && DraftProfile().Voice == original.Voice;
            }
            SelectSection(CompanionSections, CompanionTabs, "Voice"); UpdateLayout();
            checks["testVoiceButtonFits"] = Bounds(TestVoiceButton).Right <= Bounds(CompanionsPage).Right;
            var present = new Rune.Shared.GameState { ready = true, roster = new[] { new Rune.Shared.CompanionState { id = original.Id } } };
            RefreshPresence(present);
            checks["presentCompanionOffersUnsummon"] = CompanionPresenceButton.Content.ToString() == "Unsummon" && CompanionPresenceButton.IsEnabled;
            RefreshPresence(new Rune.Shared.GameState { ready = true, roster = new[] { new Rune.Shared.CompanionState { id = "another-companion" } } });
            checks["otherCompanionDoesNotChangeSelectedPresence"] = CompanionPresenceButton.Content.ToString() == "Summon" && CompanionPresenceButton.IsEnabled;
            RefreshPresence(new Rune.Shared.GameState { ready = false });
            checks["presenceDisabledWithoutGame"] = !CompanionPresenceButton.IsEnabled;
            changingCompanionPresence = true; RefreshPresence(present);
            checks["duplicatePresenceRequestDisabled"] = !CompanionPresenceButton.IsEnabled;
            changingCompanionPresence = false; RefreshPresence(runtime.ShellState());
            SelectSection(CompanionSections, CompanionTabs, activeTab); UpdateLayout();
        }
        if (CommandsPage.IsVisible) {
            var seen = new HashSet<string>();
            foreach (string group in CommandGroupsCombo.Items) {
                CommandGroupsCombo.SelectedItem = group;
                foreach (CommandChoice choice in CommandActionList.Items) {
                    CommandActionList.SelectedItem = choice; UpdateLayout(); seen.Add(choice.Ability.action);
                    var detail = (Grid)((FrameworkElement)CommandHint.Parent).Parent;
                    checks[choice.Ability.action + "Fits"] = Bounds(CommandHint).Bottom <= Bounds(detail.Children.Cast<FrameworkElement>().Single(e => Grid.GetRow(e) == 5)).Top;
                }
            }
            checks["allPublicAbilitiesIncluded"] = seen.SetEquals(Rune.Shared.Rules.Capabilities.Where(c => !c.hidden).Select(c => c.action));
            CommandGroupsCombo.SelectedIndex = 0; UpdateLayout();
        }
        checks["passed"] = checks.Values.All(v => v);
        return checks;
    }
}

