using System.Net.Http;
using System.Text.Json;

namespace Rune.Voice;

internal static class LanguageSettings
{
    internal static readonly string[] Codes = { "en", "de", "nl" };
    internal static readonly string[] Labels = { "English", "Deutsch (German)", "Nederlands (Dutch)" };
    internal static string Normalize(string? code) => Codes.Contains(code) ? code! : "en";
    internal static string Name(string code) => code == "de" ? "German" : code == "nl" ? "Dutch" : "English";
    internal static string Rule(string code) => " The selected conversation language is " + Name(code) + ". Write all conversational replies and clarification messages only in " + Name(code) + ". Do not switch languages because a misheard word, a name or quoted text resembles another language. Keep JSON keys and game action identifiers unchanged. ";
}

public sealed partial class RuneWindow
{
    private void ShowLanguageSettings(string? previewPath = null)
    {
        using var dialog = ThemedDialog("Conversation language", new Size(620, 300));
        using var lifetime = new CancellationTokenSource();
        var choice = new ComboBox { Location = new Point(24, 54), Width = 560, DropDownStyle = ComboBoxStyle.DropDownList };
        choice.Items.AddRange(LanguageSettings.Labels); choice.SelectedIndex = Array.IndexOf(LanguageSettings.Codes, LanguageSettings.Normalize(preferences.Language)); RuneTheme.Field(choice);
        var info = new Label { Location = new Point(24, 98), Size = new Size(560, 100), ForeColor = RuneTheme.Muted,
            Text = "Locks recognition and AI replies to one language; no automatic switching.\nEnglish uses your installed model. German and Dutch need a multilingual recognition model downloaded on first setup.\nThe current natural voice pack and fixed game notices are English-oriented." };
        var actions = new FlowLayoutPanel { Location = new Point(24, 212), Size = new Size(565, 55) };
        AddButton(actions, "Use selected language", async () => {
            string code = LanguageSettings.Codes[choice.SelectedIndex];
            if (code != "en") {
                microphonePending++;
                actions.Enabled = choice.Enabled = false; StopCapture(true); info.Text = "Preparing multilingual recognition. First setup downloads a model and may take several minutes…";
                try {
                    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                    using var response = await http.PostAsync((LocalServices.Audio + "/language/prepare"), LocalJson(new { language = code }), lifetime.Token);
                    await CheckMicrophoneResponse(response);
                } catch (Exception e) { if (!dialog.IsDisposed) { info.Text = "Language unchanged: " + e.Message; actions.Enabled = choice.Enabled = true; } return; }
                finally { microphonePending--; }
            }
            if (dialog.IsDisposed || lifetime.IsCancellationRequested) return;
            StopCapture(true); CancelTurn();
            foreach (var heard in heardSpeech) taskTrace.Record("request-cancelled", new { reason = "Language changed" }, heard.Id);
            heardSpeech.Clear(); preferences.Language = brain.Language = code; SavePreferences();
            taskTrace.Record("language-changed", new { language = code }); dialog.Close();
        }, true);
        dialog.FormClosed += (_, _) => lifetime.Cancel();
        dialog.Controls.Add(new Label { Location = new Point(24, 20), Size = new Size(560, 25), Text = "Recognition and AI reply language", ForeColor = RuneTheme.Bone });
        dialog.Controls.AddRange(new Control[] { choice, info, actions });
        if (previewPath != null) { dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(this); dialog.PerformLayout(); Application.DoEvents(); using var bitmap = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(previewPath); dialog.Close(); return; }
        dialog.ShowDialog(this);
    }
}
