using System.Text.Json;
using Rune.Shared;

namespace Rune.Voice;

// Keeps the proven voice/game runtime behind the new WPF shell.  The old form
// remains hidden and owns its message-driven microphone and bridge timers while
// this narrow surface exposes only operations the launcher needs.
public sealed partial class RuneWindow
{
    internal string ShellBridgeFolder => bridge.Folder;
    internal string ShellSettingsPath => settingsPath;
    internal string ShellModsRoot => ModsRoot;
    internal string ShellSelectedCompanionId => bridge.CompanionId;
    internal string ShellTranscript => transcript.Text;
    internal string ShellListeningStatus => listeningLabel.Text;
    internal string ShellConnectionStatus => connection.Text;
    internal string ShellProfilePath => preferences.ModProfilePath;
    internal string ShellModSource => ModSources.Normalize(preferences.ModSource);
    internal string? ShellModPage(string id) => ModSources.Page(ShellModSource,id,preferences.ModSourcePages);
    internal void ShellSetModSource(string source) {preferences.ModSource=ModSources.Normalize(source);SavePreferences();}
    internal void ShellSetModPage(string id,string url) {
        if(!ModSources.ValidPage(ShellModSource,url))throw new IOException("Enter a Valheim mod page on "+ModSources.Label(ShellModSource)+".");
        preferences.ModSourcePages[ShellModSource+":"+id]=url;SavePreferences();
    }
    internal string ShellGamePath => preferences.GamePath;
    internal Task<LocalBrainService.Status> ShellEnsureLocalBrain(CancellationToken token) => LocalBrainService.Ensure(Path.GetDirectoryName(bridge.Folder)!, brain.LocalModel, token);
    internal void ShellSetCompanionTracking(string id,bool value) { var profile=preferences.Profiles.FirstOrDefault(p=>p.Id==id); if(profile==null)return; profile.ShowOnMap=value; SavePreferences(); WriteVoiceState(); }
    internal string ShellLocalModel => brain.LocalModel;
    internal string ShellLanguage => preferences.Language;
    internal string ShellChatGptModel => preferences.ChatGptModel;
    internal string ShellAiMode => preferences.AiMode;
    internal string ShellVoiceRoute => VoiceCatalog.EngineName(CurrentProfile.VoiceEngine) + " offline voice";
    internal string ShellVoiceEngine => VoiceCatalog.NormalizeEngine(CurrentProfile.VoiceEngine);
    internal bool ShellChatGptEnabled => preferences.ChatGptEnabled;
    internal bool ShellVoiceReplies => voice.Checked;
    internal int ShellVoiceVolume => neural.Volume;
    internal void ShellSetVoiceVolume(int volume) { preferences.VoiceVolume = neural.Volume = Math.Clamp(volume, 0, 100); SavePreferences(); }
    internal bool ShellAllowGameOrders => allowGameOrders.Checked;
    internal bool ShellAlwaysOn => AlwaysOn;
    internal bool ShellMicrophoneMuted => microphoneMuted;
    internal int ShellMicrophoneMode => microphoneMode.SelectedIndex;
    internal string ShellOutputDevice => neural.OutputDeviceName;
    internal string ShellMicShortcut => preferences.MicShortcut;
    internal string ShellSwitchShortcut => preferences.SwitchCompanionShortcut;
    internal string ShellOverlayShortcut => preferences.OverlayShortcut;
    internal string ShellControlsShortcut => preferences.ControlsShortcut;

    internal CompanionProfile[] ShellProfiles() => preferences.Profiles.Select(p => p.Copy()).ToArray();
    internal GameState ShellState() => bridge.State();

    internal void ShellSelectCompanion(string id) => SelectCompanion(id);

    internal async Task ShellSubmit(string text) => await Submit(text);

    internal async Task ShellSaveProfile(CompanionProfile draft)
    {
        int index = Array.FindIndex(preferences.Profiles, p => p.Id == draft.Id);
        if (index < 0) throw new InvalidOperationException("That companion profile no longer exists.");
        if (string.IsNullOrWhiteSpace(draft.Name) || string.IsNullOrWhiteSpace(draft.RecognitionName)) throw new InvalidOperationException("Name and recognition nickname are required.");
        if (WordCount(draft.Personality) > 200) throw new InvalidOperationException("Shorten the personality to 200 words before saving.");
        draft.Name = draft.Name.Trim();
        draft.RecognitionName = draft.RecognitionName.Trim();
        draft.Personality = draft.Personality.Trim();
        draft.Traits = Array.Empty<string>();
        if (Rune.Shared.Rules.IsWolf(draft.Appearance)) { draft.UseStoredMaterials = false; draft.CraftAndBuild = false; draft.CookAndSort = false; }
        preferences.Profiles[index] = draft.Copy();
        SelectCompanion(draft.Id);
        SavePreferences();
        await SyncProfileToGame();
    }

    internal CompanionProfile ShellAddCompanion()
    {
        if (preferences.Profiles.Length >= 8) throw new InvalidOperationException("Rune Fellowship supports up to eight companion profiles.");
        string id = "bot-" + Guid.NewGuid().ToString("N")[..10];
        int number = preferences.Profiles.Length + 1;
        var profile = new CompanionProfile {
            Id = id, Name = "Companion " + number, RecognitionName = "Companion " + number,
            Personality = "Loyal, observant, practical, and still discovering their own sense of humor.",
            Traits = Array.Empty<string>(), Role = "Balanced companion", CombatStyle = "Balanced"
        };
        preferences.Profiles = preferences.Profiles.Append(profile).ToArray();
        SavePreferences(); RefreshCompanionCards(); SelectCompanion(id);
        return profile.Copy();
    }

    internal async Task ShellRemoveCompanion(string id)
    {
        if (preferences.Profiles.Length <= 1) throw new InvalidOperationException("Keep at least one companion profile.");
        var profile = preferences.Profiles.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("That companion profile no longer exists.");
        var state = bridge.State();
        if (state.ready && state.roster.Any(c => c.id == id)) {
            SelectCompanion(id);
            var reply = await bridge.Send(new Command { action = "dismiss" }, turn.Token);
            if (!reply.accepted) throw new InvalidOperationException(reply.message);
        }
        CancelTurn(); brain.DeleteMemoryFor(profile.Id);
        preferences.Profiles = preferences.Profiles.Where(p => p.Id != profile.Id).ToArray();
        bridge.CompanionId = preferences.Profiles[0].Id;
        SavePreferences(); RefreshCompanionCards(); SelectCompanion(bridge.CompanionId);
    }

    internal async Task ShellSummon(string id)
    {
        SelectCompanion(id);
        if (bridge.State().roster.Any(c => c.id == id)) await SyncProfileToGame();
        else await Submit("summon");
    }

    internal async Task<string> ShellUnsummon(string id)
    {
        if (!preferences.Profiles.Any(p => p.Id == id)) throw new InvalidOperationException("That companion profile no longer exists.");
        SelectCompanion(id);
        var reply = await bridge.Send(new Command { action = "dismiss" }, turn.Token);
        AddLine("Companion", reply.message);
        return reply.message;
    }

    internal async Task<string> ShellPreviewVoice(CompanionProfile draft, CancellationToken cancellation, Action<string>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (VoiceCatalog.NormalizeEngine(draft.VoiceEngine) == "chatterbox-turbo" && preferences.Language != "en")
            throw new InvalidOperationException("Chatterbox Turbo speaks English. Choose Chatterbox V3 for " + preferences.Language.ToUpperInvariant() + ".");
        if (!string.IsNullOrWhiteSpace(neural.OutputDeviceName) && !OutputAudio.Devices().Contains(neural.OutputDeviceName))
            throw new InvalidOperationException("The selected headset output is disconnected. Choose an available output in Settings → Audio setup.");
        if (!AlwaysOn) StopCapture(true);
        CancelTurn();
        int serial = generation;
        voicePreviewGeneration = serial;
        voiceStarted = DateTime.UtcNow;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, turn.Token);
        var token = linked.Token;
        using var scope = taskTrace.Scope(Guid.NewGuid().ToString("N"));
        void Report(string stage) { token.ThrowIfCancellationRequested(); taskTrace.Record("voice-preview-stage", new { companion = draft.Id, engine = draft.VoiceEngine, stage }); progress?.Invoke(stage); }
        try {
        Report("Preparing a quick voice test—no AI writing step.");
        string sample = VoiceAudition.Line(preferences.Language);
        token.ThrowIfCancellationRequested();
        AddLine("Voice test · " + draft.Name, sample);
        var audition = draft.Copy(); audition.Personality = VoiceAudition.Delivery;
        await SpeakWithSelectedVoice(sample, audition, token, Report, preview: true);
        Report("Voice test complete. Your sample is in Conversation.");
        return sample;
        } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
            taskTrace.IncidentFor("voice-preview-timeout", draft.Id, "voice-preview", "The selected local voice service timed out.");
            throw new TimeoutException("The local voice service took too long. Restart Rune or try Kokoro, then retry.");
        } catch (OperationCanceledException) { taskTrace.Record("voice-preview-cancelled", new { companion = draft.Id }); throw; }
        catch (Exception ex) { taskTrace.IncidentFor("voice-preview-error", draft.Id, "voice-preview", ex.Message); throw; }
        finally { if (generation == serial) ResetVoicePreview(); }
    }

    internal void ShellLaunchValheim(bool modded) => LaunchValheim(modded);
    internal void ShellToggleMicrophoneMute() => ToggleMicrophoneMute();
    internal event Action? ShellCommandsRequested;
    internal void ShellOpenCommands() => ShowCommands();
    internal void ShellOpenPlan() => OpenPlan();
    internal void ShellOpenChatGpt() => ShowChatGptSettings();
    internal void ShellOpenLocalModel() => _ = ShowModelSettings();
    internal void ShellOpenAudioSetup() => _ = ShowMicrophoneSetup();
    internal void ShellOpenLanguage() => ShowLanguageSettings();
    internal void ShellOpenMemory() => ShowMemory();
    internal void ShellOpenSessionReport() => ShowFeedback();
    internal void ShellOpenModConfigs(string profilePath, string modId) => EditModConfigs(profilePath, modId);

    internal void ShellSetVoiceReplies(bool enabled) {
        voice.Checked = enabled; SavePreferences();
        nextPerformance = DateTime.MinValue;
        if (!enabled) {
            speechOutput.Cancel(); speechOutput.Dispose(); speechOutput = new();
            ResetVoicePreview(); neural.CancelWarm(); neural.Stop(); speaker.SpeakAsyncCancelAll(); gameReplies.Clear();
            ShellVoiceReadiness = "Spoken replies off · microphone remains available";
            speechDisableTask = DisableSpeechModels();
        } else TickPerformance();
    }
    private Task speechDisableTask = Task.CompletedTask;
    private async Task DisableSpeechModels() { await ReleaseVoiceLease(); await neural.ReleaseSpeechModel(); }
    internal void ShellSetAllowOrders(bool enabled) { allowGameOrders.Checked = enabled; SavePreferences(); }
    internal void ShellSetAiMode(string mode)
    {
        if (mode is not ("local" or "hybrid" or "chatgpt")) throw new ArgumentOutOfRangeException(nameof(mode));
        CancelTurn(); preferences.AiMode = mode; preferences.ChatGptEnabled = mode != "local"; SavePreferences(); statusTicks = 14; Tick();
    }
    internal void ShellSetMicrophoneMode(int mode)
    {
        microphoneMode.SelectedIndex = Math.Clamp(mode, 0, microphoneMode.Items.Count - 1);
        if (!AlwaysOn) microphoneMuted = false;
        SavePreferences(); WriteVoiceState();
    }

    internal void ShellSetShortcut(string kind, string value)
    {
        if (kind == "mic") { preferences.MicShortcut = value; StopCapture(true); keyDown = false; }
        else if (kind == "switch") preferences.SwitchCompanionShortcut = value;
        else if (kind == "overlay") preferences.OverlayShortcut = value;
        else if (kind == "controls") preferences.ControlsShortcut = value;
        else throw new ArgumentOutOfRangeException(nameof(kind));
        SavePreferences(); WriteVoiceState();
    }

    internal void ShellSetShortcutCapture(bool active)
    {
        shellCapturingShortcut = active;
        if (active) { keyDown = false; switchKeyDown = false; }
    }

    internal void ShellSetModProfile(string path)
    {
        preferences.ModProfilePath = path;
        var item = modProfiles.Items.Cast<ProfileChoice>().FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        if (item != null) modProfiles.SelectedItem = item;
        SavePreferences(); RefreshMods();
    }

    internal void ShellSetGamePath(string path) { preferences.GamePath = path; SavePreferences(); }

    internal string ShellCreateModProfile(string name)
    {
        string path = OwnedMods.Create(ModsRoot, name);
        AttachRune(path); preferences.ModProfilePath = path; DiscoverModProfiles(); SavePreferences();
        return path;
    }

    internal string ShellRemoveModProfile(string path)
    {
        OwnedMods.RemoveProfile(ModsRoot, path);
        DiscoverModProfiles();
        string replacement = OwnedMods.Profiles(ModsRoot).FirstOrDefault() ?? "";
        preferences.ModProfilePath = replacement;
        SavePreferences();
        return replacement;
    }

    internal string ShellImportModProfile(string source, string name)
    {
        string path = OwnedMods.Import(ModsRoot, name, source);
        AttachRune(path); preferences.ModProfilePath = path; DiscoverModProfiles(); SavePreferences();
        return path;
    }

    internal async Task<string> ShellImportModProfileXml(string source, string name)
    {
        string path = await Task.Run(() => ModProfileXml.Import(ModsRoot, name, source));
        AttachRune(path); preferences.ModProfilePath = path; DiscoverModProfiles(); SavePreferences();
        return path;
    }

    internal async Task<string> ShellChatGptStatus(CancellationToken token)
    {
        if (!preferences.ChatGptEnabled) return "Off";
        try {
            var response = await chatGpt.Request("account/read", new { refreshToken = false }, token);
            bool signedIn = response.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object && account.TryGetProperty("type", out var type) && type.GetString() == "chatgpt";
            return signedIn ? "Connected" : "Sign in required";
        } catch { return "Unavailable"; }
    }
}
