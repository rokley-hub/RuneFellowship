namespace Rune.Voice;

public sealed partial class RuneWindow
{
    private readonly ComboBox companionVoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private string[] voiceIds = VoiceCatalog.VoiceIds;
    private bool restoringVoice;
    private Button? voicePreviewButton;
    private int voicePreviewGeneration = -1;
    private string CurrentVoiceId => companionVoice.SelectedIndex >= 0 ? voiceIds[companionVoice.SelectedIndex] : CurrentProfile.Voice;

    private Control BuildVoiceChoices()
    {
        RuneTheme.Field(companionVoice); companionVoice.Items.AddRange(voiceIds.Select(VoiceCatalog.Label).ToArray());
        companionVoice.SelectedIndexChanged += (_, _) => { if (restoringVoice || companionVoice.SelectedIndex < 0) return; neural.VoiceId = CurrentVoiceId; UpdatePortrait(); };
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        var preview = RuneTheme.Button("Test voice"); preview.AutoSize = false; preview.Dock = DockStyle.Top; preview.Height = 27; preview.Padding = Padding.Empty; preview.Margin = new Padding(5, 3, 0, 0); preview.Font = new Font("Segoe UI", 9);
        voicePreviewButton = preview;
        preview.Click += async (_, _) =>
        {
            if (voicePreviewGeneration >= 0) { CancelTurn(); profileNotice.Text = "Voice test stopped."; return; }
            var draft = CurrentProfile.Copy(); draft.Name = profileName.Text.Trim(); draft.Voice = CurrentVoiceId;
            preview.Text = "Stop test";
            profileNotice.ForeColor = RuneTheme.Muted;
            try
            {
                await ShellPreviewVoice(draft, CancellationToken.None, stage => profileNotice.Text = stage);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!closing) { profileNotice.ForeColor = Color.IndianRed; profileNotice.Text = "Voice test unavailable: " + e.Message; } }
            finally { if (!closing) preview.Text = "Test voice"; }
        };
        row.Controls.Add(companionVoice, 0, 0); row.Controls.Add(preview, 1, 0); return row;
    }

    private void ResetVoicePreview()
    {
        if (voicePreviewGeneration < 0) return;
        voicePreviewGeneration = -1; speakingReply = false; listenAfter = DateTime.UtcNow.AddMilliseconds(900);
        if (voicePreviewButton is { IsDisposed: false }) voicePreviewButton.Text = "Test voice";
    }

    internal void PreviewVoiceTestLayout()
    {
        if (voicePreviewButton != null) voicePreviewButton.Text = "Stop test";
        profileNotice.ForeColor = RuneTheme.Muted;
        profileNotice.Text = "Preparing a quick voice test—no AI writing step.";
    }

    private void SetVoice(string voiceId) {
        restoringVoice = true;
        voiceIds = VoiceCatalog.ForEngine(CurrentProfile.VoiceEngine);
        companionVoice.Items.Clear(); companionVoice.Items.AddRange(voiceIds.Select(VoiceCatalog.Label).ToArray());
        companionVoice.SelectedIndex = Array.IndexOf(voiceIds, VoiceCatalog.SelectVoice(CurrentProfile.VoiceEngine, voiceId));
        restoringVoice = false; neural.VoiceId = CurrentVoiceId;
    }
    private string VoiceGender(string voiceId) => VoiceCatalog.IsMale(voiceId) ? "male" : "female";
}
