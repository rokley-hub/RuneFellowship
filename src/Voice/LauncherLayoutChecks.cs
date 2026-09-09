using System.Windows;
using System.Windows.Controls;

namespace Rune.Voice;
public partial class LauncherWindow
{
    internal Dictionary<string, bool> CheckPreviewLayout()
    {
        Rect Bounds(FrameworkElement e) => e.TransformToAncestor(this).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
        var checks = new Dictionary<string, bool>();
        if (PlayPage.IsVisible) {
            foreach (var status in new[] { ValheimReady, LocalReady, CloudReady, VoiceReady }) {
                var grid = (Grid)status.Parent;
                var label = grid.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 0 && Grid.GetRow(t) == 0);
                checks[status.Name + "DoesNotOverlapLabel"] = !Bounds(status).IntersectsWith(Bounds(label));
            }
            checks["voiceOutputHasSeparateRow"] = Bounds(VoiceOutput).Top >= Bounds(VoiceReady).Bottom;
        }
        if (CompanionsPage.IsVisible) {
            var original = DraftProfile(); string notice = CompanionNotice.Text;
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
