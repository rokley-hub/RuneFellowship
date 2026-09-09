using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text.Json;
using Rune.Shared;

namespace Rune.Voice;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if (args.Length >= 3 && args[0] is "--reply-voice-check" or "--live-reply-voice-check") { RuneWindow.TestReplyVoice(args[1], args[2], args[0] == "--live-reply-voice-check"); return; }
        if (args.Length >= 3 && args[0] == "--voice-maintenance-check") { RuneWindow.TestVoiceMaintenance(args[1], args[2]); return; }
        if (args.Length >= 3 && args[0] == "--local-service-check") { try { LocalBrainChecks.Run(args[1],args[2]).GetAwaiter().GetResult(); } catch(Exception e) { File.WriteAllText(args[2],e.ToString()); Environment.ExitCode=1; } return; }
        if (args.Length >= 2 && args[0] == "--session-fix-checks") { SessionFixChecks.Run(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--performance-checks") { PerformanceChecks.Run(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--reliability-checks") { ReliabilityChecks.Run(args[1]); return; }
        if (args.Length >= 3 && args[0] == "--live-memory-check") { ReliabilityChecks.LiveMemory(args[1], args[2]).GetAwaiter().GetResult(); return; }
        if (args.Length >= 3 && args[0] == "--shell-voice-preview-test") { RuneWindow.TestShellVoicePreview(args[1], args[2]); return; }
        if (args.Length >= 2 && args[0] == "--owned-mod-checks") { OwnedModChecks.Run(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--mod-source-checks") { ModSourceChecks.Run(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--nexus-mod-checks") { NexusModChecks.Run(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--xml-profile-checks") { ModProfileXmlChecks.Run(args[1], args.Length >= 3 ? args[2] : null); return; }
        if (args.Length >= 4 && args[0] == "--config-match-check") { try { File.WriteAllLines(args[3], OwnedMods.ConfigFilesFor(args[1], args[2])); } catch (Exception e) { File.WriteAllText(args[3], "FAILED: " + e); Environment.ExitCode = 1; } return; }
        if (args.Length >= 4 && args[0] == "--attach-rune-profile") { try { OwnedMods.AddRune(args[1], args[2], args[3]); } catch (Exception e) { File.WriteAllText(Path.Combine(args[1], "attach-error.txt"), e.ToString()); Environment.ExitCode = 1; } return; }
        if (args.Length >= 3 && args[0] == "--mod-install-check") {
            try {
                string modRoot = args[1];
                var catalog = ModCatalog.Refresh(modRoot, new Progress<string>(), CancellationToken.None).GetAwaiter().GetResult();
                string profile = OwnedMods.Create(modRoot, "Native install test");
                var requirements = ModCatalog.RuneRequirements(catalog, OwnedMods.Read(profile));
                OwnedMods.Install(profile, requirements, catalog, new Progress<string>(), CancellationToken.None, enableRequested: true).GetAwaiter().GetResult();
                var installed = OwnedMods.Read(profile);
                if (!installed.All(m => m.Enabled) || !File.Exists(Path.Combine(profile, "BepInEx/core/BepInEx.Preloader.dll")) || !Directory.GetFiles(profile, "Jotunn.dll", SearchOption.AllDirectories).Any() || !Directory.GetFiles(profile, "PlanBuild.dll", SearchOption.AllDirectories).Any()) throw new IOException("Required files are missing after installation.");
                File.WriteAllText(args[2], "PASS: Downloaded and installed Rune requirements with public dependencies into an independent empty profile; required libraries exist and are enabled. No game launched.\n" + string.Join("\n", installed.Select(m => m.Id + " " + m.Version)));
            }
            catch (Exception e) { File.WriteAllText(args[2], "FAILED: " + e); Environment.ExitCode = 1; } return;
        }
        if (args.Length >= 5 && args[0] == "--import-mod-profile") {
            try { string profile = OwnedMods.Import(args[1], args[2], args[3]); if (args.Length >= 7) OwnedMods.AddRune(profile, args[5], args[6]); File.WriteAllText(args[4], profile + "\n" + OwnedMods.Read(profile).Count + " packages"); }
            catch (Exception e) { File.WriteAllText(args[4], "FAILED: " + e); Environment.ExitCode = 1; } return;
        }
        if (args.Length >= 3 && args[0] == "--catalog-check") { try { var catalog = ModCatalog.Refresh(args[1], new Progress<string>(), CancellationToken.None).GetAwaiter().GetResult(); File.WriteAllText(args[2], catalog.Count + " packages\n" + string.Join("\n", catalog.Where(m => m.Id.Contains("PlanBuild") || m.Id.Contains("BepInExPack_Valheim")).Select(m => m.Id + " " + m.Version))); } catch (Exception e) { File.WriteAllText(args[2], "FAILED: " + e); Environment.ExitCode = 1; } return; }
        if (args.Length >= 2 && args[0] == "--chatgpt-checks") { try { ChatGptChecks.Run(args[1]).GetAwaiter().GetResult(); } catch (Exception e) { File.WriteAllText(args[1], "FAILED: " + e); Environment.ExitCode = 1; } return; }
        if (args.Length >= 3 && args[0] == "--chatgpt-transport-check") { try { ChatGptChecks.Transport(args[1], args[2]).GetAwaiter().GetResult(); } catch (Exception e) { File.WriteAllText(args[2], "FAILED: " + e); Environment.ExitCode = 1; } return; }
        if (args.Length >= 2 && args[0] == "--personality-dialogue-check") { try { ChatGptChecks.Personality(args[1]).GetAwaiter().GetResult(); } catch (Exception e) { File.WriteAllText(args[1], "FAILED: " + e); Environment.ExitCode = 1; } return; }
        if (args.Length >= 2 && args[0] == "--management-checks") { RuneWindow.TestManagement(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--cloud-mode-checks") { CloudModeChecks.Run(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--self-test") { SelfTest(args[1]); return; }
        if (args.Length >= 3 && args[0] == "--settings-check") { RuneWindow.TestSettingsOverview(args[1], args[2]); return; }
        if (args.Length >= 2 && args[0] == "--duplex-test") { RuneWindow.TestDuplex(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--voice-request-test") { VoiceRequestChecks.Run(args[1]).GetAwaiter().GetResult(); return; }
        if (args.Length >= 2 && args[0] == "--voice-engine-checks") { VoiceEngineChecks.Run(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--ui-benchmark") { UiPerformance.Run(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--wpf-preview") {
            double width = args.Length >= 4 && double.TryParse(args[3], out var requestedWidth) ? requestedWidth : 1440;
            double height = args.Length >= 5 && double.TryParse(args[4], out var requestedHeight) ? requestedHeight : 840;
            LauncherPreview.Save(args[1], args.Length >= 3 ? args[2] : "Play", width, height, args.Length >= 6 ? args[5] : null); return;
        }
        if (args.Length >= 2 && args[0] == "--memory-test") { MemoryPersistenceTest.Run(args[1]).GetAwaiter().GetResult(); return; }
        if (args.Length >= 2 && args[0] == "--recipe-knowledge-test") { RecipeKnowledgeTest.Run(args[1]); return; }
        if (args.Length >= 3 && args[0] == "--recipe-answer-test") { RecipeKnowledgeTest.LiveAnswer(args[1], args[2]).GetAwaiter().GetResult(); return; }
        if (args.Length >= 2 && args[0] == "--personality-preview-test") { PersonalityPreviewTest(args[1]).GetAwaiter().GetResult(); return; }
        if (args.Length >= 2 && args[0] == "--audio-connection-test") { RuneWindow.TestAudioConnection(args[1]).GetAwaiter().GetResult(); return; }
        if (args.Length >= 2 && (args[0] == "--brain-test" || args[0] == "--local-brain-test")) { BrainTest(args[1]).GetAwaiter().GetResult(); return; }
        if (args.Length >= 2 && args[0] == "--chatgpt-ui-preview") {
            ApplicationConfiguration.Initialize(); using var form = new RuneWindow(Path.Combine(Path.GetDirectoryName(args[1])!, "preview-bridge")); form.Opacity = 0; form.ShowInTaskbar = false; form.Show();
            typeof(RuneWindow).GetMethod("ShowChatGptSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(form, new object[] { args[1] }); form.Close(); return;
        }
        if (args.Length >= 2 && args[0] == "--language-ui-preview") {
            ApplicationConfiguration.Initialize(); using var form = new RuneWindow(Path.Combine(Path.GetDirectoryName(args[1])!, "language-preview-bridge")); form.Opacity = 0; form.ShowInTaskbar = false; form.Show();
            typeof(RuneWindow).GetMethod("ShowLanguageSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(form, new object[] { args[1] }); form.Close(); return;
        }
        if (args.Length >= 2 && args[0] == "--session-ui-preview") {
            ApplicationConfiguration.Initialize(); using var form = new RuneWindow(Path.Combine(Path.GetDirectoryName(args[1])!, "session-preview-bridge")); form.Opacity = 0; form.ShowInTaskbar = false; form.Show();
            typeof(RuneWindow).GetMethod("ShowFeedback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(form, new object[] { args[1] }); form.Close(); return;
        }
        if (args.Length >= 2 && args[0] == "--ui-preview") {
            ApplicationConfiguration.Initialize(); using var form = new RuneWindow(Path.Combine(Path.GetDirectoryName(args[1])!, "preview-bridge"));
            if (args.Contains("small")) form.ClientSize = new Size(1180, 800);
            if (args.Contains("testing")) form.PreviewVoiceTestLayout();
            if (args.Contains("settings")) typeof(RuneWindow).GetMethod("ShowPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(form, new object[] { "settings" });
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents(); form.PerformLayout();
            using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(args[1]); form.Close(); return;
        }
        if (args.Length >= 2 && args[0] == "--plan-test") {
            var brain = new Brain();
            var thought = brain.Chat("Make a task list to gather twelve wood, sort storage, and cook food.", new GameState { world = "synthetic-plan-test", ready = true, companion = true }, CancellationToken.None).GetAwaiter().GetResult();
            File.WriteAllText(args[1], JsonSerializer.Serialize(thought, Brain.Json)); return;
        }
        ApplicationConfiguration.Initialize();
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        string bridge = Path.Combine(root, "bridge");
        if (args.Length >= 2 && args[0] == "--bridge") bridge = args[1];
        using var mutex = new Mutex(true, "RuneVoice_Local_01", out bool first);
        if (!first) { MessageBox.Show("Rune Voice is already running."); return; }
        using var runtime = new RuneWindow(bridge) { Opacity = 0, ShowInTaskbar = false };
        runtime.Show();
        var desktop = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose };
        desktop.Run(new LauncherWindow(runtime));
        if (!runtime.IsDisposed) runtime.Close();
    }
    private static void SelfTest(string output)
    {
        var report = new Dictionary<string, object>();
        try {
            report["recognizers"] = SpeechRecognitionEngine.InstalledRecognizers().Select(r => new { r.Name, culture = r.Culture.Name }).ToArray();
            using var speaker = new SpeechSynthesizer();
            report["voices"] = speaker.GetInstalledVoices().Select(v => v.VoiceInfo.Name).ToArray();
            string wav = Path.ChangeExtension(output, ".wav");
            speaker.SetOutputToWaveFile(wav); speaker.Speak("Rune reporting for duty. No pulse, excellent work ethic.");
            speaker.SetOutputToNull();
            report["speechFile"] = wav;
            report["speechBytes"] = new FileInfo(wav).Length;
            using var recognizer = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("en-US"));
            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.SetInputToWaveFile(wav);
            report["recognizedSynthesizedSpeech"] = recognizer.Recognize(TimeSpan.FromSeconds(10))?.Text ?? "";
            recognizer.UnloadAllGrammars();
            var commandGrammar = new GrammarBuilder(new Choices("gather twenty wood", "collect ten stone", "stop")) { Culture = new System.Globalization.CultureInfo("en-US") };
            recognizer.LoadGrammar(new Grammar(commandGrammar));
            var commands = new List<object>();
            bool commandsPassed = true;
            foreach (string phrase in new[] { "gather twenty wood", "collect ten stone", "stop" }) {
                string sample = Path.Combine(Path.GetDirectoryName(output)!, phrase.Replace(' ', '-') + ".wav");
                speaker.SetOutputToWaveFile(sample); speaker.Speak(phrase); speaker.SetOutputToNull();
                recognizer.SetInputToWaveFile(sample); string heard = recognizer.Recognize(TimeSpan.FromSeconds(10))?.Text ?? "";
                commands.Add(new { phrase, heard }); commandsPassed &= heard.Equals(phrase, StringComparison.OrdinalIgnoreCase);
            }
            report["commandRecognition"] = commands; report["commandRecognitionPassed"] = commandsPassed;
            report["ok"] = true;
        } catch (Exception e) { report["ok"] = false; report["error"] = e.ToString(); }
        File.WriteAllText(output, JsonSerializer.Serialize(report, Brain.Json));
    }
    private static async Task PersonalityPreviewTest(string output)
    {
        string memoryFolder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "audition-memory-" + Guid.NewGuid().ToString("N"));
        var brain = new Brain { DisplayName = "Saved name", Personality = "Saved personality", MemoryFolder = memoryFolder };
        var results = new List<object>();
        foreach (var profile in new[] {
            new { name = "Rune", personality = "Thinks he's still alive, is afraid of bones, makes terrible Viking jokes.", appearance = "Skeleton" },
            new { name = "Eira", personality = "Calm, serious, compassionate and practical. Never sarcastic, never makes jokes.", appearance = "Female dwarf" },
            new { name = "Rune", personality = CompanionProfile.RuneDefaultPersonality, appearance = "Female dwarf" }
        }) {
            string sample = await brain.PreviewPersonality(profile.name, profile.personality, profile.appearance, CancellationToken.None);
            results.Add(new { profile, sample });
        }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); bool cancelled = false;
        try { await brain.PreviewPersonality("Cancelled", "", "Wolf", cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
        bool isolated = !Directory.Exists(memoryFolder) && brain.DisplayName == "Saved name" && brain.Personality == "Saved personality";
        File.WriteAllText(output, JsonSerializer.Serialize(new { results, cancellationPassed = cancelled, isolatedFromSavedProfileAndMemory = isolated }, Brain.Json));
        if (!cancelled || !isolated) throw new InvalidOperationException("Personality audition isolation or cancellation failed.");
    }

    private static async Task BrainTest(string output)
    {
        var brain = new Brain(); var results = new List<object>();
        foreach (string prompt in new[] { "Tell me a short joke about chopping wood.", "Don't collect wood. Let's just talk about why Vikings love boats.", "The chests are a mess; put their contents in order for me." }) {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = await brain.Chat(prompt, new GameState { world = "synthetic-test", ready = true, companion = true, health = 150, playerHealth = 80 }, CancellationToken.None);
            results.Add(new { prompt, seconds = clock.Elapsed.TotalSeconds, result });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(results, Brain.Json));
    }
}

public sealed partial class RuneWindow : Form
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private readonly GameBridge bridge;
    private readonly Brain brain = new();
    private readonly SpeechSynthesizer speaker = new();
    private readonly NeuralVoice neural = new();
    private readonly RichTextBox transcript = new();
    private readonly TextBox input = new();
    private readonly Label connection = new();
    private readonly Label listeningLabel = new();
    private readonly RuneCheckBox voice = new() { Text = "Speak replies", Checked = true, AutoSize = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 70 };
    private readonly List<string> speechParts = new();
    private bool keyDown;
    private bool recognizing;
    private bool collecting;
    private bool buttonDown;
    private int statusTicks;
    private CancellationTokenSource turn = new();
    private int generation;
    private bool closing;
    private int listenGeneration;
    private string lastGameNote = "";
    private string lastWorld = "";
    private string lastAnswer = "";
    private readonly Queue<string> gameReplies = new();

    public RuneWindow(string folder)
    {
        bridge = new GameBridge(folder);
        lastWorld = bridge.State().world;
        brain.MemoryFolder = Path.Combine(folder, "memories");
        BuildInterface(folder);
        chatGpt = new ChatGptConnection(folder) { ExecutableOverride = preferences.CodexExecutable };
        taskTrace = new TaskTrace(folder);
        bridge.Trace = taskTrace; neural.Trace = taskTrace;
        neural.RecoveryVoice = () => {
            if (recoveryVoice || closing) return;
            recoveryVoice = true;
            AddLine("Voice", "Chatterbox is slow or unavailable. Using the fast local voice for this reply; your selected companion voice is unchanged.");
        };
        taskTrace.Record("session-start", new { aiMode = preferences.AiMode, chatGptEnabled = preferences.ChatGptEnabled, commandModel = preferences.ChatGptModel });
        FormClosing += (_, _) => { if (PerformanceEnabled) _ = ReleaseVoiceLease(); voiceLifetime.Cancel(); chatGpt.Dispose(); taskTrace.Dispose(); };
        speaker.Rate = 0;
        AddLine(DisplayName, "Good to see you. Choose Conversation when you want to talk, or shape my personality here first.");
        timer.Tick += (_, _) => Tick(); timer.Start();
        // Load weights before the first spoken request. This never generates a
        // line or blocks the UI; slow loading still has explicit voice recovery.
        neural.PerformanceMode = preferences.PerformanceMode; TickPerformance();
        FormClosing += (_, _) => { closing = true; timer.Stop(); turn.Cancel(); speaker.SpeakAsyncCancelAll(); StopCapture(true); speaker.Dispose(); neural.Dispose(); foreach (var image in portraits.Values) image.Dispose(); };
    }
    private static void AddButton(Control parent, string text, Func<Task> action, bool primary = false)
    {
        var b = RuneTheme.Button(text, primary);
        b.Click += async (_, _) => { try { await action(); } catch (OperationCanceledException) { } catch (Exception e) { if (!parent.IsDisposed) MessageBox.Show(parent.FindForm(), e.Message, "Rune • action unavailable"); } }; parent.Controls.Add(b);
    }
    private void Tick()
    {
        if (!voice.Checked) gameReplies.Clear();
        TickPerformance();
        TickMicrophone();
        DrainHeardSpeech();
        if (gameReplies.Count > 0 && heardSpeech.Count == 0 && !microphoneHasSpeech && busyGeneration < 0 && !ReplyPlaying) _ = Speak(gameReplies.Dequeue(), turn.Token);
        if (++statusTicks < 14) return; statusTicks = 0;
        bool stateRead = bridge.TryState(out var s);
        taskTrace.Observe(s, stateRead);
        PollVoiceControls();
        // A failed/stale read is not evidence of a world change. Never interrupt
        // dialogue or speech merely because the game's status is unavailable.
        if (stateRead && Rules.Now - s.timestamp <= 5 && lastWorld != s.world) { lastWorld = s.world; lastGameNote = s.note; gameReplies.Clear(); CancelTurn("world changed"); }
        UpdateRoster(s);
        WriteVoiceState();
        var selected = s.roster.FirstOrDefault(c => c.id == bridge.CompanionId);
        string routing = preferences.AiMode == "chatgpt" ? "Full ChatGPT mode" : preferences.ChatGptEnabled ? "ChatGPT commands ON" : "ChatGPT commands OFF · Local parser";
        if (localStatus != null) localStatus.Text = preferences.AiMode == "chatgpt" ? "● CHATGPT\nDialogue & commands · " + VoiceCatalog.EngineName(CurrentProfile.VoiceEngine) + " voice" : preferences.ChatGptEnabled ? "● SPLIT\nChatGPT commands · Qwen conversation" : "● QWEN\nConversation & commands";
        connection.Text = routing + " · " + (!s.ready ? "Game offline" : selected == null ? "Summon " + DisplayName : $"{DisplayName} · {selected.task} · {selected.cargo} cargo items");
        if (s.ready && s.note != lastGameNote)
        {
            lastGameNote = s.note;
            if (s.note.Length > 0 && s.note != lastAnswer)
            { AddLine(preferences.Profiles.FirstOrDefault(p => p.Id == s.noteCompanion)?.Name ?? "Game", s.note); if (voice.Checked && s.noteCompanion == bridge.CompanionId) { if (gameReplies.Count >= 8) gameReplies.Dequeue(); gameReplies.Enqueue(s.note); } }
        }
    }
    private void CancelTurn([System.Runtime.CompilerServices.CallerMemberName] string reason = "") { if (ReplyPlaying || busyGeneration >= 0) taskTrace.Record("turn-interrupted", new { reason, speech = ReplyPlaying, processing = busyGeneration >= 0 }); ResetVoicePreview(); generation++; turn.Cancel(); turn.Dispose(); turn = new(); speaker.SpeakAsyncCancelAll(); neural.Stop(); }
    private sealed record ClarificationContext(string Companion, string Language, string Request, string Question, DateTime At);
    private ClarificationContext? pendingClarification;
    private ClarificationContext? TakeClarification()
    {
        var context = pendingClarification; pendingClarification = null;
        return context != null && context.Companion == bridge.CompanionId && context.Language == preferences.Language && DateTime.UtcNow - context.At <= TimeSpan.FromMinutes(2) ? context : null;
    }
    private async Task Submit(string text, bool recorded = false, string? requestId = null)
    {
        text = text.Trim(); if (text.Length == 0 || closing) return;
        requestId ??= taskTrace.BeginRequest(text, recorded ? "speech" : "typed");
        using var requestScope = taskTrace.Scope(requestId);
        var requestWatch = System.Diagnostics.Stopwatch.StartNew();

        var named = preferences.Profiles.FirstOrDefault(p => text.StartsWith("summon " + p.Name, StringComparison.OrdinalIgnoreCase) || text.StartsWith("summon " + p.RecognitionName, StringComparison.OrdinalIgnoreCase));
        if (named != null && !recorded) { SelectCompanion(named.Id); text = "summon"; }
        var address = CompanionAddress.Parse(text, preferences.Profiles);
        if (address.Companion != null) {
            if (recorded && address.Companion != bridge.CompanionId) { AddLine("Voice", "You are talking to " + DisplayName + ". Use " + preferences.SwitchCompanionShortcut + " to switch companions first."); return; }
            if (!recorded) SelectCompanion(address.Companion);
            text = address.Text;
            if (address.NameOnly) { await AcknowledgeAddress(requestId); return; }
        }
        taskTrace.Record("request-routed", new { text, companion = bridge.CompanionId, provider = preferences.ChatGptEnabled ? "ChatGPT" : "local", model = preferences.ChatGptEnabled ? (string.IsNullOrWhiteSpace(preferences.ChatGptModel) ? "account default" : preferences.ChatGptModel) : brain.LocalModel });
        if (TryBookmark(text, requestId)) return;
        if (!AlwaysOn) StopCapture(true); CancelTurn(); int serial = generation; busyGeneration = serial; var token = turn.Token;
        if (!recorded) AddLine("You", text); listeningLabel.Text = DisplayName + " is thinking…";
        var conversationState = bridge.State();
        try
        {
            string answer; string delivery = "";
            var parsed = Rules.Parse(text);
            string clarification = Rules.OrderClarification(text);
            var previousQuestion = TakeClarification();
            var command = allowGameOrders.Checked ? parsed : null;
            if (preferences.ChatGptEnabled && !FastOrder.CanBypass(text, parsed, previousQuestion != null) && parsed?.action is not ("follow" or "stay" or "defend" or "stop_pickup" or "planbuild_player" or "planbuild_self" or "finish_plan" or "resume_task") && !System.Text.RegularExpressions.Regex.IsMatch(Rules.NormalizeOrder(text), @"^plan ?build(?: |$)"))
            {
                listeningLabel.Text = "ChatGPT is interpreting your request…";
                string plannerRequest = previousQuestion == null ? text : JsonSerializer.Serialize(new { previousRequest = previousQuestion.Request, clarificationQuestion = previousQuestion.Question, currentReply = text });
                var gameState = bridge.State(); gameState.recipes = RecipeKnowledge.ReadMatches(bridge.Folder, gameState, previousQuestion == null ? text : previousQuestion.Request + " " + text);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var decision = await ChatGptPlanner.Interpret(chatGpt, preferences.ChatGptModel, plannerRequest, gameState, CurrentProfile.Copy(), allowGameOrders.Checked, token, preferences.Language, preferences.AiMode == "chatgpt", brain.ConversationHistory(gameState), brain.Notes);
                taskTrace.Timing("command-model", watch.Elapsed.TotalMilliseconds);
                taskTrace.Record("decision", new { decision.kind, decision.action });
                if (serial != generation || token.IsCancellationRequested || closing) return;
                answer = decision.message; if (decision.kind == "chat") delivery = decision.delivery;
                if (decision.kind != "chat") { lastTaskRequest = text; taskTrace.Record("interpreted", new { request = text, companion = bridge.CompanionId, decision, elapsedMs = watch.ElapsedMilliseconds }); }
                if (decision.kind == "chat" && preferences.AiMode != "chatgpt") {
                    brain.ConversationOnly = true;
                    watch.Restart(); var thought = await brain.Chat(previousQuestion == null ? text : JsonSerializer.Serialize(new { previousRequest = previousQuestion.Request, clarificationQuestion = previousQuestion.Question, currentReply = text }), gameState, token, remember: false); answer = thought.reply; delivery = thought.delivery;
                    taskTrace.Timing("local-dialogue", watch.Elapsed.TotalMilliseconds);
                } else if (decision.kind == "plan") {
                    brain.RememberExchange(conversationState, text, "Proposed plan awaiting your approval: " + answer); AddLine("ChatGPT plan", answer); ShowPlan(decision.steps, decision.objective); return;
                } else if (decision.kind == "command") {
                    gameReplies.Clear();
                    var result = await bridge.Send(new Command { action = decision.action, amount = decision.amount, item = decision.item, objective = decision.objective }, token);
                    answer = result.message;
                    taskTrace.Record("game-acknowledgment", new { companion = bridge.CompanionId, decision.action, result.accepted, result.message });
                } else if (decision.kind == "clarify") { pendingClarification = new(bridge.CompanionId, preferences.Language, previousQuestion?.Request ?? text, answer, DateTime.UtcNow); }
            }
            else if (!allowGameOrders.Checked && (parsed != null || clarification.Length > 0))
                answer = "Game orders are switched off in Settings. Enable Allow game orders before asking me to work. No order was sent.";
            else if (clarification.Length > 0) { answer = clarification; pendingClarification = new(bridge.CompanionId, preferences.Language, text, answer, DateTime.UtcNow); taskTrace.Record("clarification", new { request = text, companion = bridge.CompanionId, message = answer }); }
            else if (command != null)
            {
                gameReplies.Clear();
                lastTaskRequest = text; taskTrace.Record("direct-command", new { request = text, companion = bridge.CompanionId, command.action, command.amount, command.item });
                var dispatchWatch = System.Diagnostics.Stopwatch.StartNew(); bool accepted = false, dispatched = false;
                if (command.action == "invalid_amount") answer = "Ask for one to one hundred materials per trip.";
                else if (!ProfileAllows(command.action, out string reason)) answer = reason;
                else { dispatched = true; var result = await bridge.Send(command, token); accepted = result.accepted; answer = result.message; }
                taskTrace.Record("game-acknowledgment", new { companion = bridge.CompanionId, command.action, dispatched, accepted, message = answer, elapsedMs = dispatchWatch.ElapsedMilliseconds });
            }
            else
            {
                brain.CompanionId = bridge.CompanionId; brain.ConversationOnly = !allowGameOrders.Checked;
                var gameState = bridge.State(); gameState.recipes = RecipeKnowledge.ReadMatches(bridge.Folder, gameState, text);
                var localWatch = System.Diagnostics.Stopwatch.StartNew();
                var thought = await brain.Chat(previousQuestion == null ? text : JsonSerializer.Serialize(new { previousRequest = previousQuestion.Request, clarificationQuestion = previousQuestion.Question, currentReply = text }), gameState, token, remember: false);
                taskTrace.Timing("local-model", localWatch.Elapsed.TotalMilliseconds);
                taskTrace.Record("decision", new { thought.action, thought.item, thought.steps });
                if (serial != generation || token.IsCancellationRequested || closing) return;
                if (thought.action != "none" || thought.steps.Length > 0 || Rules.IsDirectOrder(text)) { lastTaskRequest = text; taskTrace.Record("local-interpreted", new { request = text, companion = bridge.CompanionId, thought.action, thought.item, thought.amount, thought.steps, elapsedMs = localWatch.ElapsedMilliseconds }); }
                if (brain.MemoryWarning.Length > 0) AddLine("Memory", brain.MemoryWarning);
                answer = thought.reply; if (thought.action == "none") delivery = thought.delivery;
                if (thought.kind == "clarify") pendingClarification = new(bridge.CompanionId, preferences.Language, previousQuestion?.Request ?? text, answer, DateTime.UtcNow);
                if (allowGameOrders.Checked && thought.action == "none" && thought.steps.Length == 0 && Rules.IsDirectOrder(text) && string.IsNullOrWhiteSpace(answer))
                    answer = "I haven't started an order from that request. Try a specific instruction such as gather twenty wood, craft a stone axe, or follow me. I will tell you if tools, materials or access prevent it.";
                if (allowGameOrders.Checked && thought.steps.Length > 0) { brain.RememberExchange(conversationState, text, "Proposed plan awaiting your approval: " + answer); AddLine(DisplayName, answer); ShowPlan(thought.steps, thought.objective); return; }
                if (allowGameOrders.Checked && thought.action != "none")
                {
                    if (!ProfileAllows(thought.action, out string reason)) { answer = reason; goto FinishReply; }
                    var result = await bridge.Send(new Command { action = thought.action, amount = thought.amount, item = thought.item, objective = thought.objective }, token);
                    // Speak the game's acknowledgement so rejected or incomplete actions are never presented as successes.
                    answer = result.message;
                    taskTrace.Record("game-acknowledgment", new { companion = bridge.CompanionId, thought.action, result.accepted, result.message });
                }
            }
            FinishReply:
            if (serial != generation || token.IsCancellationRequested || closing) return;
            brain.RememberExchange(conversationState, text, answer);
            AddLine(DisplayName, answer);
            taskTrace.Record("reply-text", new { companion = bridge.CompanionId, answer });
            taskTrace.Timing("request-to-written-reply", requestWatch.Elapsed.TotalMilliseconds);
            // Task processing ends at the authoritative written acknowledgment.
            // Speech can be cancelled independently by the next request; no extra model reaction.
            if (voice.Checked && heardSpeech.Count == 0) _ = Speak(answer, token, delivery);
            else taskTrace.Record("voice-skipped", new { reason = !voice.Checked ? "Speak replies is off" : "A newer spoken request is waiting" });
            listeningLabel.Text = AlwaysOn ? "Always on · listening through your selected input." : "Ready · Hold your shortcut or choose Always on.";
        }
        catch (OperationCanceledException) { taskTrace.Record("request-cancelled", new { reason = token.IsCancellationRequested ? "Interrupted or app closed" : "Service timeout" }); if (serial == generation && !closing) listeningLabel.Text = "Conversation timed out. Try a shorter message."; }
        catch (Exception e) { if (serial == generation && !closing) { taskTrace.Record("request-failed", new { companion = bridge.CompanionId, error = e.Message }); taskTrace.IncidentFor("request-error", bridge.CompanionId, "interpretation", e.Message); AddLine(preferences.ChatGptEnabled ? "ChatGPT / dialogue" : "Setup", "Request unavailable: " + e.Message + (!preferences.ChatGptEnabled && e is HttpRequestException ? " Open Rune.exe starts the local services." : "")); listeningLabel.Text = "Request failed · Follow and stop remain available."; } }
        finally { taskTrace.Timing("request-processing-total", requestWatch.Elapsed.TotalMilliseconds); if (busyGeneration == serial) busyGeneration = -1; listenAfter = DateTime.UtcNow.AddMilliseconds(900); }
    }
    private int speechSequence;
    private readonly CancellationTokenSource voiceLifetime = new();
    private CancellationTokenSource speechOutput = new();
    private bool recoveryVoice;
    private DateTime voiceStarted;
    private async Task Speak(string text, CancellationToken token, string delivery = "") {
        if (!voice.Checked) return;
        using var speechCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, speechOutput.Token);
        token = speechCancellation.Token;
        int speechOwner = ++speechSequence;
        recoveryVoice = false; voiceStarted = DateTime.UtcNow;
        if (!AlwaysOn) StopCapture(true); speakingReply = true;
        taskTrace.Record("voice-reply-start", new { engine = CurrentProfile.VoiceEngine, voice = CurrentProfile.Voice, output = neural.OutputDeviceName });
        try {
            await SpeakWithSelectedVoice(text, CurrentProfile.Copy(), token, delivery: delivery);
            taskTrace.Record("voice-reply-completed", new { speechOwner });
        }
        catch (OperationCanceledException) { taskTrace.Record("voice-reply-cancelled", new { speechOwner, reason = token.IsCancellationRequested ? "Turn interrupted" : "Voice service timed out" }); if (!token.IsCancellationRequested && !closing) { listeningLabel.Text = "Voice timed out. Your written reply is still available."; AddLine("Voice", listeningLabel.Text); } }
        catch (Exception e) { if (!token.IsCancellationRequested && !closing) { taskTrace.IncidentFor("voice-error", bridge.CompanionId, "speech-output", e.Message); listeningLabel.Text = "VOICE ERROR · " + e.Message; AddLine("Voice", "Voice unavailable: " + e.Message + " Your written reply is still shown above."); } }
        finally { if (speechOwner == speechSequence) { speakingReply = false; listenAfter = DateTime.UtcNow.AddMilliseconds(900); } }
    }

    private readonly SemaphoreSlim speechGate = new(1, 1);
    private async Task SpeakWithSelectedVoice(string text, CompanionProfile profile, CancellationToken token, Action<string>? progress = null, bool preview = false, string delivery = "")
    {
        await speechGate.WaitAsync(token);
        string previousEngine = neural.Engine, previousVoice = neural.VoiceId, previousLanguage = neural.Language, previousPersonality = neural.Personality, previousDelivery = neural.Delivery;
        try {
            neural.PerformanceMode = preferences.PerformanceMode;
            neural.Engine = VoiceCatalog.NormalizeEngine(profile.VoiceEngine);
            neural.VoiceId = profile.Voice;
            neural.Language = preferences.Language;
            neural.Personality = profile.Personality; neural.Delivery = preview ? "" : delivery;
            if (preview) await neural.SpeakPreview(text, token, progress);
            else await neural.Speak(text, token, progress);
        }
        finally { neural.Engine = previousEngine; neural.VoiceId = previousVoice; neural.Language = previousLanguage; neural.Personality = previousPersonality; neural.Delivery = previousDelivery; speechGate.Release(); }
    }
    private void Ui(Action action) { if (!closing && IsHandleCreated) BeginInvoke(action); }
    private void AddLine(string name, string text)
    {
        if (preferences.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) lastAnswer = text;
        transcript.SelectionStart = transcript.TextLength; transcript.SelectionColor = name == "You" ? Color.FromArgb(60, 113, 91) : Color.FromArgb(136, 96, 48);
        transcript.AppendText(name + "\n"); transcript.SelectionColor = ForeColor; transcript.AppendText(text + "\n\n"); transcript.ScrollToCaret();
    }

    private bool ProfileAllows(string action, out string reason)
    {
        var p = CurrentProfile; reason = "";
        if (!p.CraftAndBuild && action is "craft_item" or "gather_recipe" or "build_boat" or "planbuild_player" or "planbuild_self" or "finish_plan") reason = p.Name + " is not allowed to craft or build. Change that permission in Companions.";
        else if (!p.CookAndSort && action is "cook_food" or "sort_storage" or "manage_base" or "store_cargo") reason = p.Name + " is not allowed to cook or manage storage. Change that permission in Companions.";
        else if (p.Appearance == "wolf" && action is "craft_item" or "gather_recipe" or "build_boat" or "planbuild_player" or "planbuild_self" or "finish_plan" or "cook_food" or "sort_storage" or "manage_base" or "store_cargo" or "lend_tools" or "equip_gear" or "pickup_equip") reason = p.Name + " has a wolf body and cannot use tools, armour, crafting stations, or storage.";
        return reason.Length == 0;
    }
}








