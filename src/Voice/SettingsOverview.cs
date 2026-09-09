using System.Text.Json;

namespace Rune.Voice;

public sealed partial class RuneWindow
{
    internal static void TestSettingsOverview(string stub, string output)
    {
        ApplicationConfiguration.Initialize();
        using var window = new RuneWindow(Path.Combine(Path.GetDirectoryName(output)!, "settings-check-bridge"));
        window.Opacity = 0; window.ShowInTaskbar = false;
        window.Shown += async (_, _) => {
            try {
                window.timer.Stop(); window.chatGpt.Dispose(); window.chatGpt = new ChatGptConnection(window.bridge.Folder) { ExecutableOverride = stub };
                window.preferences.ChatGptEnabled = false; window.preferences.ChatGptModel = "";
                await window.RefreshAccountOverview();
                if (!window.accountOverview.Text.Contains("Signed in") || !window.commandOverview.Text.Contains("ChatGPT off") || !window.cloudModelOverview.Text.Contains("synthetic-default")) throw new Exception("Account/default model/disabled command status not distinguished.");
                window.preferences.ChatGptEnabled = true; window.preferences.ChatGptModel = "chosen-test-model"; window.RefreshSettingsSummary();
                if (!window.commandOverview.Text.Contains("ChatGPT enabled") || !window.cloudModelOverview.Text.Contains("chosen-test-model") || !window.localModelOverview.Text.Contains(window.brain.LocalModel)) throw new Exception("Selected models not shown.");
                File.WriteAllText(output, "PASS: Verified sign-in is separate from command enablement; account default, chosen cloud model and local model are visible. Synthetic helper only; no real account or inference used.");
            } catch (Exception e) { File.WriteAllText(output, "FAILED: " + e); }
            window.Close();
        };
        Application.Run(window);
    }
    private Label accountOverview = null!, commandOverview = null!, cloudModelOverview = null!, localModelOverview = null!, languageOverview = null!;
    private bool checkingAccount;
    private string accountDefaultModel = "";

    private Control BuildSettingsPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20), BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 254));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        Label Line(string text, int height, bool heading = false) => new Label { Text = text, Dock = DockStyle.Top, Height = height, BackColor = Color.Transparent, ForeColor = heading ? RuneTheme.Bone : RuneTheme.Muted, Font = new Font("Segoe UI", heading ? 13 : 10, heading ? FontStyle.Bold : FontStyle.Regular) };
        Control Card(string title, string caption, Label[] lines, FlowLayoutPanel buttons) {
            var card = new RunePanel { Dock = DockStyle.Fill, Padding = new Padding(14), Margin = new Padding(0, 0, 14, 16) };
            buttons.Dock = DockStyle.Bottom; buttons.Height = 45; buttons.WrapContents = false;
            card.Controls.Add(buttons);
            foreach (var line in lines.Reverse()) card.Controls.Add(line);
            card.Controls.Add(Line(caption, 30)); card.Controls.Add(Line(title, 32, true)); return card;
        }
        accountOverview = Line("Sign-in not checked · press Refresh", 28);
        commandOverview = Line("", 28); cloudModelOverview = Line("", 42);
        var cloudButtons = new FlowLayoutPanel { BackColor = Color.Transparent };
        AddButton(cloudButtons, "Account and model", async () => { ShowChatGptSettings(); RefreshSettingsSummary(); await RefreshAccountOverview(); }, true);
        AddButton(cloudButtons, "Refresh", RefreshAccountOverview);
        cards.Controls.Add(Card("ChatGPT · game commands", "Understands requests and chooses tasks.", new[] { accountOverview, commandOverview, cloudModelOverview }, cloudButtons), 0, 0);
        localModelOverview = Line("", 42); languageOverview = Line("", 28);
        var localButtons = new FlowLayoutPanel { BackColor = Color.Transparent };
        AddButton(localButtons, "Change model", async () => { await ShowModelSettings(); RefreshSettingsSummary(); });
        AddButton(localButtons, "Language", () => { ShowLanguageSettings(); RefreshSettingsSummary(); return Task.CompletedTask; });
        cards.Controls.Add(Card("Qwen · conversation", "Your companion's dialogue and personality.", new[] { localModelOverview, languageOverview, Line("Runs locally on this PC", 28) }, localButtons), 1, 0);
        layout.Controls.Add(cards, 0, 0);
        var audio = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(14, 0, 14, 0) };
        var mic = BuildMicrophonePanel(); mic.Dock = DockStyle.Fill; audio.Controls.Add(mic); audio.Controls.Add(Line("Microphone and voice output", 36, true)); layout.Controls.Add(audio, 0, 1);
        var extras = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(14, 8, 0, 0), WrapContents = false };
        AddButton(extras, "Memory and notes", () => { ShowMemory(); return Task.CompletedTask; });
        AddButton(extras, "Session report / Issues", () => { ShowFeedback(); return Task.CompletedTask; });
        layout.Controls.Add(extras, 0, 2); RefreshSettingsSummary();
        return PageFrame("Settings", "See what is connected, which models are selected, and how Rune listens.", layout);
    }

    private void RefreshSettingsSummary()
    {
        if (commandOverview == null || commandOverview.IsDisposed) return;
        commandOverview.Text = "Command handling: " + (preferences.ChatGptEnabled ? "ChatGPT enabled" : "Local Qwen · ChatGPT off");
        cloudModelOverview.Text = "Selected model: " + (preferences.ChatGptModel.Length > 0 ? preferences.ChatGptModel : "Account default" + (accountDefaultModel.Length > 0 ? " · " + accountDefaultModel : " (checked on Refresh)"));
        localModelOverview.Text = "Selected local model: " + brain.LocalModel;
        languageOverview.Text = "Conversation language: " + LanguageSettings.Name(preferences.Language);
    }

    private async Task RefreshAccountOverview()
    {
        if (checkingAccount || closing) return; checkingAccount = true; accountOverview.Text = "Checking ChatGPT sign-in…";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try {
            var response = await chatGpt.Request("account/read", new { refreshToken = false }, timeout.Token);
            if (closing || accountOverview.IsDisposed) return;
            bool signedIn = response.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object && account.TryGetProperty("type", out var type) && type.GetString() == "chatgpt";
            accountOverview.Text = signedIn ? "● Signed in with ChatGPT" : "○ Not signed in · open Account and model";
            accountOverview.ForeColor = signedIn ? RuneTheme.Good : RuneTheme.AmberSoft;
            accountDefaultModel = "";
            if (signedIn && preferences.ChatGptModel.Length == 0) {
                try {
                    var available = await chatGpt.Request("model/list", new { }, timeout.Token);
                    foreach (var model in available.GetProperty("data").EnumerateArray())
                        if (model.TryGetProperty("isDefault", out var isDefault) && isDefault.ValueKind == JsonValueKind.True) accountDefaultModel = model.GetProperty("model").GetString() ?? "";
                } catch { /* Account status remains valid if model discovery is unavailable. */ }
            }
            if (!closing) RefreshSettingsSummary();
        } catch (Exception) { if (!closing && !accountOverview.IsDisposed) { accountOverview.Text = "Sign-in check unavailable · try Refresh"; accountOverview.ForeColor = RuneTheme.AmberSoft; } }
        finally { checkingAccount = false; }
    }
}
