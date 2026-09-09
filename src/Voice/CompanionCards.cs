using Rune.Shared;

namespace Rune.Voice;

public sealed partial class RuneWindow
{
    private readonly List<Button> companionCards = new();
    private FlowLayoutPanel? companionCardsHost;

    private void BuildCompanionCards(FlowLayoutPanel parent)
    {
        companionCardsHost = parent; parent.SizeChanged += (_, _) => UpdateCompanionLayout(); RefreshCompanionCards();
    }

    private void RefreshCompanionCards()
    {
        if (companionCardsHost == null) return;
        companionCardsHost.SuspendLayout();
        foreach (Control oldCard in companionCardsHost.Controls.Cast<Control>().ToArray()) oldCard.Dispose();
        companionCardsHost.Controls.Clear(); companionCards.Clear();
        foreach (var profile in preferences.Profiles)
        {
            var card = new Button { Tag = profile.Id, Size = new Size(190, 62), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(11, 0, 0, 0), Margin = new Padding(0, 0, 8, 6), FlatStyle = FlatStyle.Flat, ForeColor = RuneTheme.Bone, Font = new Font("Segoe UI Semibold", 8.5f), Cursor = Cursors.Hand, UseMnemonic = false };
            RuneTheme.SetButtonTone(card, RuneButtonTone.Neutral);
            card.Click += (_, _) => SelectCompanion((string)card.Tag!); companionCards.Add(card); companionCardsHost.Controls.Add(card);
        }
        if (preferences.Profiles.Length < 8) {
            var add = RuneTheme.Button("＋  New companion", true); add.AutoSize = false; add.Size = new Size(170, 62); add.Margin = new Padding(0, 0, 8, 6); add.Click += (_, _) => AddCompanion(); companionCardsHost.Controls.Add(add);
        }
        companionCardsHost.ResumeLayout(); UpdateCompanionLayout(); UpdateCompanionCards();
    }

    private void AddCompanion()
    {
        if (preferences.Profiles.Length >= 8) { MessageBox.Show(this, "Rune Fellowship supports up to eight companion profiles at once.", "Fellowship full"); return; }
        string id = "bot-" + Guid.NewGuid().ToString("N").Substring(0, 10);
        int number = preferences.Profiles.Length + 1;
        var profile = new CompanionProfile { Id = id, Name = "Companion " + number, RecognitionName = "Companion " + number, Personality = "Loyal, observant, practical, and still discovering their own sense of humor.", Traits = new[] { "Loyal", "Curious" }, Role = "Balanced companion", CombatStyle = "Balanced" };
        preferences.Profiles = preferences.Profiles.Append(profile).ToArray(); SavePreferences(); RefreshCompanionCards(); SelectCompanion(id);
        profileNotice.ForeColor = RuneTheme.AmberSoft; profileNotice.Text = "New profile created. Choose a name, voice, appearance, and personality.";
    }

    private async Task RemoveCurrentCompanion()
    {
        if (preferences.Profiles.Length <= 1) { MessageBox.Show(this, "Keep at least one companion profile.", "Cannot remove companion"); return; }
        var profile = CurrentProfile;
        if (MessageBox.Show(this, "Remove " + profile.Name + " from the fellowship?\n\nTheir cargo and borrowed gear must be returned first. Their local conversation memory and profile settings will be deleted.", "Remove companion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var state = bridge.State();
        if (!state.ready) { MessageBox.Show(this, "Open the solo Valheim world where this companion belongs, then remove them. This lets Rune safely remove their in-game body as well as the profile.", "Valheim world required"); return; }
        var reply = await bridge.Send(new Command { action = "dismiss" }, turn.Token);
        if (!reply.accepted) { MessageBox.Show(this, reply.message, "Could not remove companion"); return; }
        CancelTurn(); brain.DeleteMemoryFor(profile.Id); preferences.Profiles = preferences.Profiles.Where(p => p.Id != profile.Id).ToArray(); bridge.CompanionId = preferences.Profiles[0].Id; SavePreferences(); RefreshCompanionCards(); SelectCompanion(bridge.CompanionId);
    }

    private void UpdateCompanionCards(GameState? state = null)
    {
        state ??= bridge.State();
        foreach (var card in companionCards)
        {
            string id = (string)card.Tag!; bool selected = id == bridge.CompanionId; var profile = preferences.Profiles.First(p => p.Id == id); var member = state.roster?.FirstOrDefault(c => c.id == id); string status = member == null ? "Not summoned" : member.task;
            card.Text = profile.Name + "\n" + FriendlyAppearance(profile.Appearance) + "  ·  " + status;
            card.BackColor = Color.Transparent; card.ForeColor = selected ? RuneTheme.AmberSoft : RuneTheme.Bone; RuneTheme.SetButtonTone(card, selected ? RuneButtonTone.Selected : RuneButtonTone.Neutral);
        }
    }

    private static string FriendlyAppearance(string skin) => skin switch { "elite" => "Elite draugr", "draugr" => "Draugr", "dwarf" => "Dwarf", "wolf" => "Wolf", _ => "Skeleton" };
}
