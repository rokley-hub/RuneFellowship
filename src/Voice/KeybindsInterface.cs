namespace Rune.Voice;

public sealed partial class RuneWindow
{
    private int recipientVersion;
    private bool switchKeyDown;
    private bool shellCapturingShortcut;
    private readonly Dictionary<string, ComboBox> keyChoices = new();
    private static readonly string[] ShortcutChoices = { "Off", "Ctrl + Alt + R", "Ctrl + Alt + V", "Ctrl + Alt + C", "Ctrl + Alt + B", "Ctrl + Alt + N", "Ctrl + Shift + R", "Ctrl + Shift + C", "F6", "F7", "F8", "F10", "F11" };
    private bool ShortcutHeld(string shortcut)
    {
        if (shellCapturingShortcut || shortcut == "Off") return false;
        var parts = shortcut.Split('+').Select(s => s.Trim()).ToArray();
        bool down(Keys k) => (GetAsyncKeyState((int)k) & 0x8000) != 0;
        bool ctrl = down(Keys.ControlKey), alt = down(Keys.Menu), shift = down(Keys.ShiftKey);
        bool modifiersMatch = ctrl == parts.Contains("Ctrl") && alt == parts.Contains("Alt") && shift == parts.Contains("Shift");
        string final = parts.Last();
        if (final == "Ctrl") return modifiersMatch && ctrl;
        if (final == "Alt") return modifiersMatch && alt;
        if (final == "Shift") return modifiersMatch && shift;
        return modifiersMatch && Enum.TryParse<Keys>(final, out var key) && down(key);
    }
    private void TickCompanionShortcut()
    {
        bool pressed = ShortcutHeld(preferences.SwitchCompanionShortcut);
        if (pressed && !switchKeyDown && !keyChoices.Values.Any(c => c.Focused || c.DroppedDown)) {
            int index = Array.FindIndex(preferences.Profiles, p => p.Id == bridge.CompanionId);
            SelectCompanion(preferences.Profiles[(index + 1) % preferences.Profiles.Length].Id);
            listeningLabel.Text = "Talking to " + DisplayName + " · " + preferences.SwitchCompanionShortcut + " switches companion";
            controlNotice = "Talking to " + DisplayName; controlNoticeUntil = DateTime.UtcNow.AddSeconds(5); WriteVoiceState();
        }
        switchKeyDown = pressed;
    }
    private Control BuildKeybindsPage()
    {
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 6, Padding = new Padding(16) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        string[] ids = { "mic", "switch", "overlay", "controls" };
        string[] labels = { "Microphone\nHold to talk; toggles mute in Always on", "Switch companion\nOnly the selected companion receives your speech", "Show / hide Fellowship\nIn-game status and crafting checklist", "Open / close controls\nAlso unlocks dragging the in-game window" };
        string[] values = { preferences.MicShortcut, preferences.SwitchCompanionShortcut, preferences.OverlayShortcut, preferences.ControlsShortcut };
        for (int i = 0; i < ids.Length; i++) {
            string id = ids[i]; body.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            body.Controls.Add(new Label { Text = labels[i], Dock = DockStyle.Fill, ForeColor = RuneTheme.Bone, BackColor = Color.Transparent, Font = new Font("Segoe UI", 11), Padding = new Padding(0, 12, 0, 0) }, 0, i);
            var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new Padding(0, 16, 0, 0) };
            choice.Items.AddRange(ShortcutChoices); choice.SelectedItem = values[i]; RuneTheme.Field(choice); keyChoices[id] = choice; body.Controls.Add(choice, 1, i);
            choice.SelectionChangeCommitted += (_, _) => {
                string selected = choice.Text;
                if (selected != "Off" && keyChoices.Any(p => p.Key != id && p.Value.Text == selected)) { MessageBox.Show(this, "That shortcut is already assigned. Choose another key combination.", "Keybind conflict"); choice.SelectedItem = id == "mic" ? preferences.MicShortcut : id == "switch" ? preferences.SwitchCompanionShortcut : id == "overlay" ? preferences.OverlayShortcut : preferences.ControlsShortcut; return; }
                if (id == "mic") { preferences.MicShortcut = selected; StopCapture(true); keyDown = false; }
                else if (id == "switch") preferences.SwitchCompanionShortcut = selected;
                else if (id == "overlay") preferences.OverlayShortcut = selected;
                else preferences.ControlsShortcut = selected;
                SavePreferences(); WriteVoiceState();
            };
        }
        var hint = new Label { Dock = DockStyle.Fill, ForeColor = RuneTheme.Muted, BackColor = Color.Transparent, Text = "Shortcuts save immediately. Choose keys that do not conflict with your Valheim or other mod controls.\nSwitching companions stops the old reply and clears pending speech; ongoing game tasks continue.", Font = new Font("Segoe UI", 10) };
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.Controls.Add(hint, 0, 4); body.SetColumnSpan(hint, 2);
        return PageFrame("Keybinds", "Your microphone, conversation target and in-game controls.", body);
    }
    private void ResetRecipientAudio()
    {
        recipientVersion++; StopCapture(true); heardSpeech.Clear(); gameReplies.Clear(); microphoneHasSpeech = false;
        listenAfter = DateTime.UtcNow.AddMilliseconds(250);
    }
}
