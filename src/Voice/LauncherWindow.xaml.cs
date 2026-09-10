using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Rune.Shared;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfCursors = System.Windows.Input.Cursors;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfImage = System.Windows.Controls.Image;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Rune.Voice;

public partial class LauncherWindow : Window
{
    private readonly RuneWindow runtime;
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly HttpClient health = new() { Timeout = TimeSpan.FromSeconds(1.5) };
    private readonly string[] appearanceIds = { "skeleton", "draugr", "elite", "dwarf", "wolf" };
    private readonly string[] appearanceLabels = { "Skeleton", "Draugr", "Elite draugr", "Dwarf · player gear", "Wolf" };
    private string[] voiceIds = VoiceCatalog.VoiceIds;
    private string displayedVoiceEngine = "kokoro";
    private readonly Dictionary<string, string> draftVoiceChoices = new();
    private readonly string[] voiceEngineIds = VoiceCatalog.EngineIds;
    private readonly string[] voiceEngineLabels = VoiceCatalog.EngineLabels;
    private CompanionProfile? selectedProfile;
    private bool loadingProfile;
    private bool loadingSettings;
    private bool loadingMods;
    private int healthTick;
    private CancellationTokenSource? voicePreview;
    private List<InstalledMod> installedMods = new();
    private List<CatalogMod> catalogMods = new();
    private ModRow? selectedMod;
    private bool catalogRefreshRunning;
    private bool browsingMods;
    private WpfButton? shortcutCaptureButton;
    private string shortcutCaptureOriginal = "";
    private ModifierKeys shortcutCaptureModifiers;

    private sealed record ProfileChoice(string Path, string Label) { public override string ToString() => Label; }
    private sealed class RosterChoice
    {
        public required CompanionProfile Profile { get; init; }
        public required string Status { get; set; }
        public BitmapImage? Portrait { get; init; }
        public string Name => Profile.Name;
        public override string ToString() => Profile.Name + "\n" + FriendlyAppearance(Profile.Appearance) + " · " + Status;
    }
    private sealed class ModRow
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string InstalledVersion { get; init; }
        public required string LatestVersion { get; init; }
        public required string State { get; init; }
        public InstalledMod? Installed { get; init; }
        public CatalogMod? Catalog { get; init; }
    }

    public LauncherWindow(RuneWindow runtime)
    {
        this.runtime = runtime;
        InitializeComponent();
        Loaded += (_, _) => UpdateEmberAnimation();
        StateChanged += (_, _) => UpdateEmberAnimation();
        Closed += (_, _) => { RuneEmberHalo.BeginAnimation(OpacityProperty, null); RuneEmberCore.BeginAnimation(OpacityProperty, null); };
        ContentRendered += async (_, _) => { if (!Environment.GetCommandLineArgs().Contains("--wpf-preview")) await CheckRuneUpdate(false); };
        Closed += (_, _) => updateLifetime.Cancel();
        InitializeChoices();
        InitializeCommandLibrary();
        runtime.ShellCommandsRequested += OpenCommandsFromRuntime;
        ReloadEverything();
        SetModsMode(false);
        if (!UseNexus) _ = EnsureCatalogFresh();
        refreshTimer.Tick += RefreshTimer_Tick;
        refreshTimer.Start();
        Closed += (_, _) => { runtime.ShellCommandsRequested -= OpenCommandsFromRuntime; refreshTimer.Stop(); voicePreview?.Cancel(); runtime.ShellSetShortcutCapture(false); health.Dispose(); };
        if (!Environment.GetCommandLineArgs().Contains("--wpf-preview") && File.Exists(Path.Combine(AppContext.BaseDirectory, "..", "release.json")))
            ContentRendered += (_, _) => ShowFirstRunOnce();
    }

    private void InitializeChoices()
    {
        AppearanceCombo.ItemsSource = appearanceLabels;
        VoiceEngineCombo.ItemsSource = voiceEngineLabels;
        VoiceCombo.ItemsSource = voiceIds.Select(VoiceCatalog.Label).ToArray();
        RoleCombo.ItemsSource = new[] { "Builder & Gatherer", "Scout & Gatherer", "Guard & Explorer", "Quartermaster & Cook", "Balanced companion" };
        CombatCombo.ItemsSource = new[] { "Cautious", "Balanced", "Aggressive" };
        FrequencyCombo.ItemsSource = new[] { "Quiet", "Natural", "Talkative" };
        ModsFilterCombo.ItemsSource = new[] { "All installed", "Enabled only", "Disabled only", "Updates available" };
        catalogMods = ModCatalog.Cached(runtime.ShellModsRoot);
        ModsFilterCombo.SelectedIndex = 0;
    }

    private void OpenCommandsFromRuntime() { ShowPage("Commands"); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    private void CommandSearch_Changed(object sender, TextChangedEventArgs e) { if (CommandActionList != null) CommandGroup_Changed(sender,new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>())); }
    private void UseCommandExample_Click(object sender, RoutedEventArgs e) { if (CommandActionList.SelectedItem is CommandChoice choice) { ShowPage("Conversation"); ConversationInput.Text = choice.Ability.example; ConversationInput.Focus(); } }
    private void InitializeCommandLibrary()
    {
        CommandGroupsCombo.ItemsSource = Rules.Capabilities.Where(c => !c.hidden).Select(c => c.group).Distinct().ToArray();
        CommandGroupsCombo.SelectedIndex = 0;
    }

    private sealed record CommandChoice(Capability Ability)
    {
        public string Title => Ability.action switch {
            "equip_weapon" => "Switch weapon", "block" => "Hold a guard", "parry" => "Attempt parries", "shoot" => "Shoot arrows", "focus_enemy" => "Focus an enemy", "combat_auto" => "Automatic combat", "repair_equipment" => "Repair equipment", "resume_task" => "Resume saved work",
            "summon" => "Join the fellowship", "dismiss" => "Dismiss companion", "follow" => "Follow me", "stay" => "Wait here",
            "return" => "Bring cargo back", "status" => "Check current work", "defend" => "Protect me",
            "lend_tools" => "Borrow work tools", "pickup_equip" => "Pick up and use equipment", "equip_gear" => "Borrow armour",
            "gather_wood" => "Gather wood", "gather_stone" => "Gather stone", "gather_item" => "Collect a named item",
            "pickup_all" => "Collect all loose items", "stop_pickup" => "Stop collecting items", "exclude_item" => "Exclude an item", "include_item" => "Include an item again",
            "craft_item" => "Craft an item", "gather_recipe" => "Gather recipe ingredients", "clear_crafting" => "Clear crafting",
            "build_boat" => "Build a boat", "planbuild_player" => "Place a design near me", "planbuild_self" => "Place a design near Rune", "finish_plan" => "Complete placed blueprints",
            "set_base" => "Remember our base", "store_cargo" => "Store carried materials", "sort_storage" => "Organise storage", "cook_food" => "Cook food", "manage_base" => "Look after the base",
            _ => Ability.action.Replace('_', ' ')
        };
    }

    private void CommandGroup_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CommandActionList == null) return;
        CommandActionList.ItemsSource = Rules.Capabilities.Where(c => !c.hidden && (string.IsNullOrWhiteSpace(CommandSearchBox.Text) ? c.group == CommandGroupsCombo.SelectedItem?.ToString() : (c.action + c.example + c.description + c.group).Contains(CommandSearchBox.Text, StringComparison.OrdinalIgnoreCase))).Select(c => new CommandChoice(c)).ToArray();
        CommandActionList.SelectedIndex = 0;
    }

    private void CommandAction_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CommandActionList.SelectedItem is not CommandChoice choice) { CommandTitle.Text = "No matching abilities"; CommandExample.Text = ""; CommandDescription.Text = "Try another item or action, such as weapon, blueprint or gather."; CommandHint.Text = ""; return; }
        CommandTitle.Text = choice.Title;
        CommandExample.Text = "“" + choice.Ability.example + "”";
        CommandDescription.Text = choice.Ability.description;
        CommandHint.Text = choice.Ability.group switch {
            "Building & boats" => "Use the exact saved design name and say ‘on me’ or ‘on yourself’. A compatible blueprint integration is required for designs; building compatibility is still a beta limitation. Rune cannot invent an arbitrary house layout or sail a boat.",
            "Gathering & loot" => "Get, collect, find and gather express the same goal when the item is clear. Name a quantity for a bounded trip (1–100). Named loose items can be collected; Rune cannot harvest every resource type.",
            "Crafting & task plans" => "‘Make a bronze axe’ requests work. ‘What does a bronze axe need?’ asks for information. Crafting already checks missing ingredients; you do not need to spell out each gathering step.",
            "Combat & equipment" => "‘Defend me’, ‘protect me’ and ‘watch my back’ request protection. The companion's body, available equipment and combat style determine how they can fight.",
            "Base management" => "Remember a base first. Storage and cooking need accessible chests, usable stations and the matching permissions. Rune reports the actual blocker when a prerequisite is missing.",
            _ => "Select one companion to talk to. Use your Switch companion keybind to change the recipient. ‘Come with me’ and ‘follow me’ request the same outcome. Ask what Rune is doing to check her current state."
        };
    }

    private void ReloadEverything()
    {
        RefreshProfiles();
        RefreshModProfiles();
        RefreshSettings();
        RefreshRuntimeState();
        _ = RefreshHealth();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        RefreshRuntimeState();
        // Account/model discovery can touch the local ChatGPT transport. Keep it
        // off the fast UI pulse so the launcher stays responsive while listening.
        if (++healthTick % 46 == 0) _ = RefreshHealth();
    }

    private void RefreshRuntimeState()
    {
        GameState state;
        try { state = runtime.ShellState(); } catch { return; }
        var profiles = runtime.ShellProfiles();
        var current = profiles.FirstOrDefault(p => p.Id == runtime.ShellSelectedCompanionId) ?? profiles.FirstOrDefault();
        string name = current?.Name ?? "Rune";
        TalkTargetText.Text = name;
        var portraitKey = current == null ? "" : current.Id + ":" + current.Appearance + ":" + current.Voice;
        if (portraitKey != footerPortraitKey) { FooterPortrait.Source = current == null ? null : PortraitSource(current); footerPortraitKey = portraitKey; }
        ActivityText.Text = runtime.ShellActivity;
        ActivityText.Foreground = (WpfBrush)FindResource(runtime.ShellActivity is "Muted" or "Mic offline" ? "Gold" : "Green");
        ActivityText.ToolTip = runtime.ShellListeningStatus;
        ConversationTitle.Text = "Conversation with " + name;
        MicStateText.Text = runtime.ShellMicrophoneMode == 0 ? "Microphone off · typed chat available"
            : runtime.ShellAlwaysOn ? runtime.ShellMicShortcut + " toggles mute" : "Hold to talk · " + runtime.ShellMicShortcut;
        MicStateText.Foreground = (WpfBrush)FindResource("Muted");
        MuteButton.Content = runtime.ShellMicrophoneMuted ? "Unmute mic" : "Mute mic";
        MuteButton.IsEnabled = runtime.ShellAlwaysOn;
        TitleRuntimeStatus.Text = state.ready ? "Valheim connected" : "Valheim closed";
        ConversationTranscript.Text = runtime.ShellTranscript;
        ConversationProvider.Text = runtime.ShellAiMode == "chatgpt" ? "ChatGPT · " + runtime.ShellVoiceRoute : runtime.ShellAiMode == "hybrid" ? "ChatGPT tasks · local conversation" : "Local conversation";
        if (ConversationPage.Visibility == Visibility.Visible) ConversationTranscript.ScrollToEnd();

        if (selectedProfile != null) {
            var member = state.roster.FirstOrDefault(c => c.id == selectedProfile.Id);
            CompanionRuntimeInfo.Text = member == null ? "Not summoned\n\n150 base health\n32 inventory slots" :
                $"{member.task}\n\nHealth {member.health:0} / {member.maxHealth:0} · Cargo {member.cargo}\nBase {(member.hasBase ? "remembered" : "not set")}";
        }
        RefreshPresence(state);
        RefreshPlayCompanions(profiles, state);
    }

    private bool healthCheckRunning;
    private string localHealthMessage = "Checking Qwen…";
    private async Task RefreshHealth()
    {
        if (healthCheckRunning) return;
        healthCheckRunning = true;
        try {
        bool needsLocal = runtime.ShellAiMode != "chatgpt";
        bool preview = Environment.GetCommandLineArgs().Contains("--wpf-preview");
        var brainStatus = needsLocal && !preview ? await runtime.ShellEnsureLocalBrain(CancellationToken.None) : await LocalBrainService.Inspect(runtime.ShellLocalModel,CancellationToken.None);
        localHealthMessage = brainStatus.Message;
        bool local = brainStatus.Ready;
        bool voiceService = await Ping((LocalServices.Audio + "/"));
        bool expressiveVoice = runtime.ShellVoiceEngine == "kokoro" || await Ping((LocalServices.Expressive + "/"));
        string cloud;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3))) cloud = await runtime.ShellChatGptStatus(timeout.Token);
        if (!Dispatcher.CheckAccess()) { await Dispatcher.InvokeAsync(() => ApplyHealth(local, voiceService, expressiveVoice, cloud)); return; }
        ApplyHealth(local, voiceService, expressiveVoice, cloud);
        } finally { healthCheckRunning = false; }
    }

    private async Task<bool> Ping(string address)
    {
        try { using var response = await health.GetAsync(address); return response.IsSuccessStatusCode; } catch { return false; }
    }

    private void ApplyHealth(bool local, bool voiceService, bool expressiveVoice, string cloud)
    {
        ValheimReady.Text = File.Exists(runtime.ShellGamePath) ? "●  Ready" : "●  Locate game";
        ValheimReady.Foreground = File.Exists(runtime.ShellGamePath) ? (WpfBrush)FindResource("Green") : (WpfBrush)FindResource("Gold");
        bool fullCloud = runtime.ShellAiMode == "chatgpt";
        LocalReady.Text = fullCloud ? "—  Not used" : "●  " + localHealthMessage;
        LocalReady.Foreground = fullCloud ? (WpfBrush)FindResource("Muted") : local ? (WpfBrush)FindResource("Green") : new SolidColorBrush(WpfColor.FromRgb(198, 102, 82));
        CloudReady.Text = "●  " + cloud;
        CloudReady.Foreground = cloud == "Connected" ? (WpfBrush)FindResource("Green") : (WpfBrush)FindResource("Gold");
        bool selectedVoiceReady = voiceService && expressiveVoice;
        VoiceReady.Text = "●  " + VoiceCatalog.EngineName(runtime.ShellVoiceEngine) + (selectedVoiceReady ? " · " + runtime.ShellVoiceReadiness : " · Unavailable");
        VoiceOutput.Text = selectedVoiceReady ? "Output · " + (string.IsNullOrWhiteSpace(runtime.ShellOutputDevice) ? "System default" : runtime.ShellOutputDevice) : "Open Rune.exe to start voice, or check Audio setup.";
        VoiceOutput.ToolTip = VoiceOutput.Text;
        VoiceReady.ToolTip = VoiceReady.Text;
        VoiceReady.Foreground = selectedVoiceReady ? (WpfBrush)FindResource("Green") : new SolidColorBrush(WpfColor.FromRgb(198, 102, 82));
        SidebarLocal.Text = fullCloud ? "Qwen brain · Not used" : local ? runtime.ShellLocalModel + " Qwen brain" : "Qwen brain unavailable";
        SidebarCloud.Text = runtime.ShellAiMode == "local" ? "ChatGPT · Off" : "ChatGPT · " + cloud;
        SettingsChatStatus.Text = (fullCloud ? "ChatGPT brain · conversation & commands" : runtime.ShellAiMode == "hybrid" ? "ChatGPT brain · commands only" : "ChatGPT brain · off") + " · " + cloud + (string.IsNullOrWhiteSpace(runtime.ShellChatGptModel) ? " · account default model" : " · " + runtime.ShellChatGptModel);
        SettingsLocalStatus.Text = fullCloud ? "Qwen brain · not used · voice stays local" : (runtime.ShellAiMode == "hybrid" ? "Qwen brain · conversation" : "Qwen brain · conversation & commands") + " · " + localHealthMessage;
        SettingsAudioStatus.Text = "Selected voice · " + runtime.ShellVoiceRoute + " · Output · " + (string.IsNullOrWhiteSpace(runtime.ShellOutputDevice) ? "system default" : runtime.ShellOutputDevice) + " · Recognition " + runtime.ShellLanguage.ToUpperInvariant();
    }

    private void RefreshProfiles()
    {
        var profiles = runtime.ShellProfiles();
        var state = runtime.ShellState();
        string wanted = selectedProfile?.Id ?? runtime.ShellSelectedCompanionId;
        CompanionRoster.Items.Clear();
        foreach (var profile in profiles) {
            string status = state.roster.FirstOrDefault(c => c.id == profile.Id)?.task ?? "Not summoned";
            CompanionRoster.Items.Add(new RosterChoice { Profile = profile, Status = status, Portrait = PortraitSource(profile) });
        }
        var chosen = CompanionRoster.Items.Cast<RosterChoice>().FirstOrDefault(c => c.Profile.Id == wanted) ?? CompanionRoster.Items.Cast<RosterChoice>().FirstOrDefault();
        CompanionRoster.SelectedItem = chosen;
    }

    private void RefreshPlayCompanions(CompanionProfile[] profiles, GameState state)
    {
        if (PlayCompanionCards.Children.Count == profiles.Length && PlayCompanionCards.Tag?.ToString() == runtime.ShellSelectedCompanionId + ":" + string.Join('|', state.roster.Select(r => r.id + r.task))) return;
        PlayCompanionCards.Children.Clear();
        PlayCompanionCards.Tag = runtime.ShellSelectedCompanionId + ":" + string.Join('|', state.roster.Select(r => r.id + r.task));
        foreach (var profile in profiles.Take(4)) {
            var member = state.roster.FirstOrDefault(c => c.id == profile.Id);
            var card = new Border { Width = 330, Height = 124, Margin = new Thickness(0, 0, 12, 12), Background = new SolidColorBrush(WpfColor.FromArgb(225, 24, 23, 20)), BorderBrush = profile.Id == runtime.ShellSelectedCompanionId ? (WpfBrush)FindResource("Gold") : new SolidColorBrush(WpfColor.FromRgb(68, 60, 43)), BorderThickness = new Thickness(profile.Id == runtime.ShellSelectedCompanionId ? 2 : 1), Cursor = WpfCursors.Hand };
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new WpfImage { Source = PortraitSource(profile), Stretch = Stretch.UniformToFill });
            var text = new StackPanel { Margin = new Thickness(17, 12, 10, 8) }; Grid.SetColumn(text, 1);
            text.Children.Add(new TextBlock { Text = profile.Name, FontFamily = new WpfFontFamily("Georgia"), FontSize = 22, Foreground = (WpfBrush)FindResource("Bone"), FontWeight = FontWeights.Normal });
            text.Children.Add(new TextBlock { Text = FriendlyAppearance(profile.Appearance) + " · " + profile.Role, Foreground = (WpfBrush)FindResource("Muted"), Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = profile.Id == runtime.ShellSelectedCompanionId ? "●  Voice target" : member == null ? "○  Not summoned" : "●  " + member.task, Foreground = profile.Id == runtime.ShellSelectedCompanionId ? (WpfBrush)FindResource("Green") : (WpfBrush)FindResource("Muted"), Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            grid.Children.Add(text); card.Child = grid;
            card.ToolTip = "Talk to " + profile.Name + " · click to select your voice target";
            card.MouseLeftButtonUp += (_, e) => {
                // The editor may contain another companion's unsaved draft. Rebuilding
                // its roster here would select that companion again and undo this click.
                if (runtime.ShellSelectedCompanionId != profile.Id) runtime.ShellSelectCompanion(profile.Id);
                RefreshRuntimeState(); e.Handled = true;
            };
            PlayCompanionCards.Children.Add(card);
        }
    }

    private void LoadProfile(CompanionProfile profile)
    {
        loadingProfile = true; selectedProfile = profile.Copy();
        CompanionName.Text = profile.Name; RecognitionName.Text = profile.RecognitionName; PersonalityBox.Text = profile.Personality;
        AppearanceCombo.SelectedIndex = Math.Max(0, Array.IndexOf(appearanceIds, profile.Appearance));
        VoiceEngineCombo.SelectedIndex = Math.Max(0, Array.IndexOf(voiceEngineIds, VoiceCatalog.NormalizeEngine(profile.VoiceEngine)));
        draftVoiceChoices.Clear();
        RefreshVoiceChoices(profile.Voice);
        RoleCombo.SelectedItem = profile.Role; CombatCombo.SelectedItem = profile.CombatStyle; FrequencyCombo.SelectedItem = profile.ConversationFrequency;
        TrackCompanionCheck.IsChecked = profile.ShowOnMap;
        StoredMaterialsCheck.IsChecked = profile.UseStoredMaterials; CraftBuildCheck.IsChecked = profile.CraftAndBuild; CookSortCheck.IsChecked = profile.CookAndSort; BossFightCheck.IsChecked = profile.JoinBossFights;
        loadingProfile = false; CompanionNotice.Text = ""; UpdatePersonalityCount(); UpdateProfileCapabilities(); UpdatePortrait();
    }

    private CompanionProfile DraftProfile()
    {
        if (selectedProfile == null) throw new InvalidOperationException("Select a companion first.");
        return new CompanionProfile {
            Id = selectedProfile.Id, Name = CompanionName.Text, RecognitionName = RecognitionName.Text,
            Appearance = appearanceIds[Math.Max(0, AppearanceCombo.SelectedIndex)],
            VoiceEngine = voiceEngineIds[Math.Max(0, VoiceEngineCombo.SelectedIndex)],
            Voice = voiceIds[Math.Max(0, VoiceCombo.SelectedIndex)],
            Personality = PersonalityBox.Text, Traits = Array.Empty<string>(),
            Role = RoleCombo.SelectedItem?.ToString() ?? "Balanced companion", CombatStyle = CombatCombo.SelectedItem?.ToString() ?? "Balanced", ConversationFrequency = FrequencyCombo.SelectedItem?.ToString() ?? "Natural",
            ShowOnMap = TrackCompanionCheck.IsChecked == true, UseStoredMaterials = StoredMaterialsCheck.IsChecked == true, CraftAndBuild = CraftBuildCheck.IsChecked == true, CookAndSort = CookSortCheck.IsChecked == true, JoinBossFights = BossFightCheck.IsChecked == true
        };
    }

    private void UpdatePortrait()
    {
        if (selectedProfile == null) return;
        var draft = DraftProfile(); CompanionPortrait.Source = PortraitSource(draft); PortraitTitle.Text = draft.Name;
        PortraitDescription.Text = draft.Appearance switch {
            "wolf" => "Fast animal companion. Can fight and collect loose items, but cannot wear armour or use tools.",
            "dwarf" => (IsMaleVoice(draft) ? "Male" : "Female") + " player-style dwarf. Can wear armour and use weapons and tools.",
            "skeleton" => "Undead humanoid with native movement and weapons. Can use tools, craft, cook, and manage storage.",
            "draugr" => "Durable undead worker with native movement and weapons. Can use tools and manage your base.",
            _ => "Heavy undead warrior with native movement and weapons. Can use tools and perform base work."
        };
    }

    private BitmapImage? PortraitSource(CompanionProfile profile)
    {
        string file = profile.Appearance == "dwarf" ? "dwarf-" + (IsMaleVoice(profile) ? "male" : "female") + ".png" : profile.Appearance + ".png";
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", file);
        if (!File.Exists(path)) return null;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); return image;
    }

    private bool IsMaleVoice(CompanionProfile profile)
    {
        return VoiceCatalog.IsMale(profile.Voice);
    }
    private void RefreshVoiceChoices(string? preferred = null)
    {
        string engine = voiceEngineIds[Math.Max(0, VoiceEngineCombo.SelectedIndex)];
        bool wasLoading = loadingProfile; loadingProfile = true;
        voiceIds = VoiceCatalog.ForEngine(engine);
        string choice = VoiceCatalog.SelectVoice(engine, preferred);
        VoiceCombo.ItemsSource = voiceIds.Select(VoiceCatalog.Label).ToArray();
        VoiceCombo.SelectedIndex = Array.IndexOf(voiceIds, choice);
        displayedVoiceEngine = engine;
        VoiceChoiceLabel.Text = engine == "kokoro" ? "Voice · 6 choices" : "Reference voice · 28";
        VoiceCombo.ToolTip = engine == "kokoro" ? "Six Kokoro voices." : "Chatterbox uses a local recording of this source voice. These 28 reference speakers come from the installed Kokoro pack.";
        draftVoiceChoices[engine] = choice;
        loadingProfile = wasLoading;
    }
    private static string FriendlyAppearance(string id) => id switch { "elite" => "Elite draugr", "draugr" => "Draugr", "dwarf" => "Dwarf", "wolf" => "Wolf", _ => "Skeleton" };

    private void UpdateProfileCapabilities()
    {
        bool wolf = AppearanceCombo.SelectedIndex == Array.IndexOf(appearanceIds, "wolf");
        PermissionsPanel.Visibility = wolf ? Visibility.Collapsed : Visibility.Visible;
        if (wolf) { StoredMaterialsCheck.IsChecked = false; CraftBuildCheck.IsChecked = false; CookSortCheck.IsChecked = false; }
        string[] roles = wolf ? new[] { "Scout & Gatherer", "Guard & Explorer", "Balanced companion" } : new[] { "Builder & Gatherer", "Scout & Gatherer", "Guard & Explorer", "Quartermaster & Cook", "Balanced companion" };
        string? current = RoleCombo.SelectedItem?.ToString(); RoleCombo.ItemsSource = roles; RoleCombo.SelectedItem = roles.Contains(current) ? current : wolf ? "Guard & Explorer" : "Balanced companion";
    }

    private void UpdatePersonalityCount()
    {
        int count = string.IsNullOrWhiteSpace(PersonalityBox.Text) ? 0 : PersonalityBox.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        PersonalityCount.Text = count + " / 200 words";
        PersonalityCount.Foreground = count > 200 ? new SolidColorBrush(WpfColor.FromRgb(211, 102, 84)) : (WpfBrush)FindResource("Muted");
    }

    private void RefreshModProfiles()
    {
        loadingMods = true;
        string selected = runtime.ShellProfilePath;
        var choices = OwnedMods.Profiles(runtime.ShellModsRoot).Select(p => new ProfileChoice(p, Path.GetFileName(p))).ToArray();
        PlayProfileCombo.ItemsSource = choices; ModsProfileCombo.ItemsSource = choices;
        var choice = choices.FirstOrDefault(p => string.Equals(p.Path, selected, StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
        PlayProfileCombo.SelectedItem = choice; ModsProfileCombo.SelectedItem = choice;
        SidebarProfile.Text = choice == null ? "No mod profile" : choice.Label + " profile";
        loadingMods = false;
        RefreshModsList();
    }

    private void RefreshModsList()
    {
        if (ModsProfileCombo.SelectedItem is not ProfileChoice profile) { installedMods.Clear(); notificationDependencyIssue = notificationProfileError = ""; ModsList.ItemsSource = null; ProfileHealthText.Text = "Create or import a mod profile to begin."; UpdateModButtons(); RefreshNotifications(); return; }
        try { installedMods = ModProfiles.Read(profile.Path); notificationProfileError = ""; } catch (Exception e) { installedMods.Clear(); notificationDependencyIssue = ""; notificationProfileError = "Could not read profile"; ModsList.ItemsSource = null; ModNoticeText.Text = e.Message; RefreshNotifications(); return; }
        selectedMod = null;
        string search = ModsSearchBox.Text ?? ""; int filter = ModsFilterCombo.SelectedIndex;
        IEnumerable<ModRow> rows;
        if (browsingMods) rows = (UseNexus ? Enumerable.Empty<CatalogMod>() : catalogMods).Where(c => (c.Name + c.Id + c.Description).Contains(search, StringComparison.OrdinalIgnoreCase)).OrderBy(c => c.Deprecated).ThenBy(c => c.Name).Take(500).Select(c => {
            var installed = installedMods.FirstOrDefault(m => m.Id == c.Id); bool update = installed != null && ModCatalog.IsNewer(installed.Version, c.Version);
            return new ModRow { Id = c.Id, Name = c.Name, InstalledVersion = installed?.Version ?? "—", LatestVersion = c.Version, State = update ? "Update available" : installed != null ? (installed.Enabled ? "Installed · enabled" : "Installed · disabled") : c.Deprecated ? "Deprecated" : "Available", Catalog = c, Installed = installed };
        });
        else rows = installedMods.Where(m => (m.Name + m.Id).Contains(search, StringComparison.OrdinalIgnoreCase) && (filter == 0 || filter == 1 && m.Enabled || filter == 2 && !m.Enabled || filter == 3 && catalogMods.Any(c => c.Id == m.Id && ModCatalog.IsNewer(m.Version, c.Version)))).OrderBy(m => m.Name).Select(m => {
            var available = UseNexus ? null : catalogMods.FirstOrDefault(c => c.Id == m.Id); bool update = available != null && ModCatalog.IsNewer(m.Version, available.Version);
            return new ModRow { Id = m.Id, Name = m.Name, InstalledVersion = m.Version, LatestVersion = UseNexus ? "On website" : NexusMods.IsNexus(m.Id) ? "Not checked" : available?.Version ?? "—", State = (update ? "Update available" : m.Enabled ? "Enabled" : "Disabled"), Installed = m, Catalog = available };
        });
        ModsList.ItemsSource = rows.ToArray();
        int enabled = installedMods.Count(m => m.Enabled); bool plan = installedMods.Any(m => m.Enabled && m.Id.Contains("PlanBuild", StringComparison.OrdinalIgnoreCase));
        int updates = UseNexus ? 0 : AvailableUpdates().Count;
        string issue = installedMods.Where(m => m.Enabled).Select(m => ModProfiles.DependencyError(installedMods, m, true)).FirstOrDefault(x => x.Length > 0) ?? "";
        notificationDependencyIssue = issue;
        ProfileHealthText.Text = enabled + " mods enabled · PlanBuild " + (plan ? "ready" : "not enabled") + " · " + (issue.Length == 0 ? "No known dependency conflicts" : issue);
        ModNoticeText.Text = UseNexus
            ? (browsingMods ? "Nexus downloads import into " + profile.Label : installedMods.Count + " mods in " + profile.Label) + " · Check dependencies and updates on the website."
            : browsingMods ? (catalogMods.Count == 0 ? "Refresh the catalogue to browse downloads." : catalogMods.Count + " downloads available · installs into " + profile.Label)
            : installedMods.Count + " mods in " + profile.Label + " · " + updates + " known updates" + (installedMods.Any(m=>NexusMods.IsNexus(m.Id)) ? " · Imported ZIP versions are not checked automatically" : "");
        ModsNav.Content = updates > 0 ? "◆   Mods  ·  " + updates : "◆   Mods";
        UpdateModButtons();
        RefreshNotifications();
    }

    private List<CatalogMod> AvailableUpdates() => installedMods.Select(m => catalogMods.FirstOrDefault(c => c.Id == m.Id && ModCatalog.IsNewer(m.Version, c.Version))).Where(c => c != null).Cast<CatalogMod>().ToList();

    private void UpdateModButtons()
    {
        bool hasProfile = ModsProfileCombo.SelectedItem is ProfileChoice;
        RemoveProfileButton.IsEnabled = hasProfile;
        ExportProfileButton.IsEnabled = hasProfile;

        InstallRequirementsButton.IsEnabled = hasProfile && !installingRequirements;
        InstallModButton.IsEnabled = browsingMods && hasProfile && (UseNexus || selectedMod?.Catalog != null);
        ToggleModButton.IsEnabled = !browsingMods && hasProfile && selectedMod?.Installed != null;
        RemoveModButton.IsEnabled = !browsingMods && hasProfile && selectedMod?.Installed != null && selectedMod.Installed.Id != "RuneCompanion";
        EditConfigButton.IsEnabled = !browsingMods && hasProfile && selectedMod?.Installed != null;
        ModWebsiteButton.IsEnabled = selectedMod != null; ProfileModWebsiteButton.IsEnabled = selectedMod != null && selectedMod.Id != "RuneCompanion";
        UpdateAllButton.IsEnabled = !browsingMods && hasProfile && (UseNexus || AvailableUpdates().Count > 0);
    }

    private void RefreshSettings()
    {
        loadingSettings = true;
        ModSourceCombo.SelectedIndex = UseNexus ? 1 : 0;
        ModSourceHelp.Text = UseNexus ? "Links open on Nexus; ZIP downloads and updates use its website. Required-mod setup uses Thunderstore." : "Links open on Thunderstore. Browse, install and update in Rune. Switching source keeps your installed mods.";
        bool fullChatGpt = runtime.ShellAiMode == "chatgpt";
        AiModeCombo.SelectedIndex = fullChatGpt ? 2 : runtime.ShellAiMode == "hybrid" ? 1 : 0;
        RefreshAiCards();
        if (runtime.ShellAiMode == "local") SettingsChatStatus.Text = "ChatGPT · Off in Local mode";
        AiModeHelp.Text = fullChatGpt
            ? "ChatGPT handles conversation and commands. Qwen is off. The companion's selected voice engine stays local."
            : runtime.ShellAiMode == "hybrid"
                ? "ChatGPT interprets commands. Qwen handles ordinary conversation. The selected voice engine stays local."
                : "Qwen handles conversation and commands. ChatGPT is off. The selected voice engine stays local.";
        LocalModelButton.Visibility = fullChatGpt ? Visibility.Collapsed : Visibility.Visible;
        if (fullChatGpt) {
            LocalReady.Text = "—  Not used";
            LocalReady.Foreground = (WpfBrush)FindResource("Muted");
            SidebarLocal.Text = "Qwen brain · Not used";
            SettingsLocalStatus.Text = "Qwen brain · not used · voice stays local";
        }
        MicrophoneModeCombo.SelectedIndex = Math.Clamp(runtime.ShellMicrophoneMode, 0, 2);
        if (shortcutCaptureButton == null) {
            MicKeyButton.Content = runtime.ShellMicShortcut; SwitchKeyButton.Content = runtime.ShellSwitchShortcut;
            OverlayKeyButton.Content = runtime.ShellOverlayShortcut; ControlsKeyButton.Content = runtime.ShellControlsShortcut;
        }
        VoiceRepliesCheck.IsChecked = runtime.ShellVoiceReplies; GameOrdersCheck.IsChecked = runtime.ShellAllowGameOrders;
        loadingSettings = false;
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        string page = ((FrameworkElement)sender).Tag?.ToString() ?? "Play";
        ShowPage(page);
    }

    internal void ShowPreviewPage(string page)
    {
        ShowPage(page is "ModsBrowse" or "Dropdown" ? "Mods" : page == "PlayStatus" ? "Play" : page == "CompanionsTest" ? "Companions" : page);
        if (page.StartsWith("Settings")) { ShowPage("Settings"); SelectSection(SettingsSections, SettingsTabs, page.Length > 8 ? page[8..] : "AI"); }
        if (page.StartsWith("Companions")) { ShowPage("Companions"); SelectSection(CompanionSections, CompanionTabs, page is "CompanionsVoice" or "CompanionsTest" ? "Voice" : page == "CompanionsBehaviour" ? "Behaviour" : "Personality"); }
        if (page == "ModsBrowse") SetModsMode(true);
        if (page == "Dropdown") ModsProfileCombo.IsDropDownOpen = true;
        if (page == "PlayStatus") ApplyHealth(true, true, true, "Connected");
        if (page == "Notifications") { ShowPage("Settings"); PreviewNotifications(); }
        if (page == "CompanionsTest") { TestVoiceButton.Content = "Stop"; CompanionNotice.Text = "Preparing Chatterbox Turbo… First use can take about 30 seconds. The personality sample is available in Conversation."; }
    }

    private void ShowPage(string page)
    {
        PlayPage.Visibility = page == "Play" ? Visibility.Visible : Visibility.Collapsed;
        CompanionsPage.Visibility = page == "Companions" ? Visibility.Visible : Visibility.Collapsed;
        ConversationPage.Visibility = page == "Conversation" ? Visibility.Visible : Visibility.Collapsed;
        CommandsPage.Visibility = page == "Commands" ? Visibility.Visible : Visibility.Collapsed;
        ModsPage.Visibility = page == "Mods" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { PlayNav, CompanionsNav, ConversationNav, CommandsNav, ModsNav, SettingsNav }) button.Style = (Style)FindResource(button.Tag?.ToString() == page ? "SelectedNavButton" : "NavButton");
        if (page == "Conversation") { ConversationTranscript.ScrollToEnd(); ConversationInput.Focus(); }
        if (page == "Companions") RefreshProfiles();
        if (page == "Mods") { RefreshModProfiles(); if (!UseNexus) _ = EnsureCatalogFresh(); }
        if (page == "Settings") { RefreshSettings(); _ = RefreshHealth(); }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void StartModded_Click(object sender, RoutedEventArgs e) => Launch(true);
    private void StartVanilla_Click(object sender, RoutedEventArgs e) => Launch(false);
    private void Launch(bool modded) { runtime.ShellLaunchValheim(modded); LaunchNotice.Text = "Starting " + (modded ? (PlayProfileCombo.SelectedItem?.ToString() ?? "modded Valheim") : "vanilla Valheim") + "…"; }

    private void PlayProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!loadingMods && PlayProfileCombo.SelectedItem is ProfileChoice p) { runtime.ShellSetModProfile(p.Path); loadingMods = true; ModsProfileCombo.SelectedItem = ModsProfileCombo.Items.Cast<ProfileChoice>().FirstOrDefault(x => x.Path == p.Path); loadingMods = false; RefreshModsList(); } }
    private void ModsProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!loadingMods && ModsProfileCombo.SelectedItem is ProfileChoice p) { runtime.ShellSetModProfile(p.Path); loadingMods = true; PlayProfileCombo.SelectedItem = PlayProfileCombo.Items.Cast<ProfileChoice>().FirstOrDefault(x => x.Path == p.Path); loadingMods = false; RefreshModsList(); } }

    private void CompanionRoster_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (CompanionRoster.SelectedItem is RosterChoice c) { runtime.ShellSelectCompanion(c.Profile.Id); LoadProfile(c.Profile); RefreshRuntimeState(); } }
    private void PersonalityBox_TextChanged(object sender, TextChangedEventArgs e) { if (PersonalityCount != null) UpdatePersonalityCount(); }
    private void VoiceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!loadingProfile && selectedProfile != null) UpdatePortrait(); }
    private void VoiceEngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingProfile || selectedProfile == null) return;
        string previousVoice = voiceIds[Math.Max(0, VoiceCombo.SelectedIndex)];
        draftVoiceChoices[displayedVoiceEngine] = previousVoice;
        string engine = voiceEngineIds[Math.Max(0, VoiceEngineCombo.SelectedIndex)];
        RefreshVoiceChoices(draftVoiceChoices.GetValueOrDefault(engine, previousVoice));
        UpdatePortrait();
        CompanionNotice.Text = engine == "kokoro" ? "Six Kokoro voices. Save companion to keep this choice." : "28 reference voices for " + VoiceCatalog.EngineName(engine) + ". A new reference is prepared locally on its first test.";
    }
    private void AppearanceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingProfile || selectedProfile == null) return;
        string next = appearanceIds[Math.Max(0, AppearanceCombo.SelectedIndex)];
        if (next != selectedProfile.Appearance) {
            var result = ShowRuneMessage("Changing body type can make equipped gear incompatible. Rune will keep the profile, but the companion may need to re-equip suitable items in game.\n\nChange appearance?", "Change companion body", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) { loadingProfile = true; AppearanceCombo.SelectedIndex = Math.Max(0, Array.IndexOf(appearanceIds, selectedProfile.Appearance)); loadingProfile = false; return; }
        }
        UpdateProfileCapabilities(); UpdatePortrait();
    }

    private async void SaveCompanion_Click(object sender, RoutedEventArgs e)
    {
        try { var draft = DraftProfile(); await runtime.ShellSaveProfile(draft); selectedProfile = draft.Copy(); CompanionNotice.Text = "Saved and synchronized with the game."; RefreshProfiles(); }
        catch (Exception ex) { CompanionNotice.Text = ex.Message; }
    }
    private void AddCompanion_Click(object sender, RoutedEventArgs e) { try { var p = runtime.ShellAddCompanion(); RefreshProfiles(); LoadProfile(p); } catch (Exception ex) { ShowRuneMessage(ex.Message, "Fellowship"); } }
    private async void RemoveCompanion_Click(object sender, RoutedEventArgs e)
    {
        if (selectedProfile == null) return;
        if (ShowRuneMessage("Remove " + selectedProfile.Name + " from the fellowship? Their local conversation memory and profile settings will be deleted.", "Remove companion", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await runtime.ShellRemoveCompanion(selectedProfile.Id); selectedProfile = null; RefreshProfiles(); }
        catch (Exception ex) { ShowRuneMessage(ex.Message, "Could not remove companion"); }
    }
    private async void TestVoice_Click(object sender, RoutedEventArgs e)
    {
        if (voicePreview != null) { voicePreview.Cancel(); return; }
        using var preview = new CancellationTokenSource(); voicePreview = preview;
        TestVoiceButton.Content = "Stop";
        CompanionNotice.Foreground = (WpfBrush)FindResource("Green");
        try { await runtime.ShellPreviewVoice(DraftProfile(), preview.Token, stage => { CompanionNotice.Text = stage; CompanionNotice.ToolTip = stage; }); }
        catch (OperationCanceledException) { CompanionNotice.Text = "Voice test stopped or interrupted."; }
        catch (Exception ex) { CompanionNotice.Foreground = (WpfBrush)FindResource("Gold"); CompanionNotice.Text = "Voice test unavailable: " + ex.Message; }
        finally { voicePreview = null; TestVoiceButton.Content = "Test voice"; CompanionNotice.ToolTip = CompanionNotice.Text; }
    }

    private void OpenConversation_Click(object sender, RoutedEventArgs e) => ShowPage("Conversation");
    private async void ConversationSend_Click(object sender, RoutedEventArgs e) => await SendConversation();
    private async void ConversationInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; await SendConversation(); } }
    private async Task SendConversation()
    {
        string text = ConversationInput.Text.Trim(); if (text.Length == 0) return;
        ConversationInput.Clear(); ConversationSendButton.IsEnabled = false;
        try { await runtime.ShellSubmit(text); } catch (Exception ex) { ShowRuneMessage(ex.Message, "Conversation unavailable"); }
        finally { ConversationSendButton.IsEnabled = true; ConversationInput.Focus(); RefreshRuntimeState(); }
    }
    private void MuteButton_Click(object sender, RoutedEventArgs e) { runtime.ShellToggleMicrophoneMute(); RefreshRuntimeState(); }

    private void ModsSearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (ModsList != null) RefreshModsList(); }
    private void ModsFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!loadingMods && ModsList != null) RefreshModsList(); }
    private void ModsList_SelectionChanged(object sender, SelectionChangedEventArgs e) { selectedMod = ModsList.SelectedItem as ModRow; string[] dependencies = selectedMod?.Catalog?.Dependencies ?? selectedMod?.Installed?.Dependencies ?? Array.Empty<string>(); ModDetailsText.Text = selectedMod == null ? "" : selectedMod.Id + (dependencies.Length > 0 ? " · Requires " + string.Join(", ", dependencies) : selectedMod.Catalog?.Description is string d ? " · " + d : ""); UpdateModButtons(); }
    private async void RefreshCatalog_Click(object sender, RoutedEventArgs e) { if (UseNexus) OpenModCatalogue(); else await RefreshCatalog(); }
    private void MyProfileMode_Click(object sender, RoutedEventArgs e)
    {
        SetModsMode(false);
    }
    private async void BrowseMode_Click(object sender, RoutedEventArgs e)
    {
        SetModsMode(true);
        if (!UseNexus && catalogMods.Count == 0) await RefreshCatalog();
        if (!UseNexus) ModsSearchBox.Focus();
    }
    private void SetModsMode(bool browse)
    {
        browsingMods = browse; selectedMod = null; ModsList.SelectedItem = null;
        MyProfileModeButton.Style = (Style)FindResource(browse ? "RuneButton" : "SelectedModeButton");
        BrowseModeButton.Style = (Style)FindResource(browse ? "SelectedModeButton" : "RuneButton");
        ProfileFilterPanel.Visibility = browse ? Visibility.Collapsed : Visibility.Visible;
        ProfileModActions.Visibility = browse ? Visibility.Collapsed : Visibility.Visible;
        BrowseModActions.Visibility = browse ? Visibility.Visible : Visibility.Collapsed;
        ProfileModFooter.Visibility = browse ? Visibility.Collapsed : Visibility.Visible;
        BrowseModFooter.Visibility = browse ? Visibility.Visible : Visibility.Collapsed;
        ModsModeTitle.Text = browse ? ModSources.Label(runtime.ShellModSource) + " downloads" : "Installed in this profile";
        ModsModeHelp.Text = browse ? (UseNexus ? "Download on the website, then import into the profile selected above." : "Search online. Downloads install into the profile selected above.") : "Select a mod to configure, update, disable, or remove it. Pages open on " + ModSources.Label(runtime.ShellModSource) + ".";
        ModsSearchLabel.Text = browse ? "Search Thunderstore" : "Search this profile";
        ModsSearchBox.IsEnabled = !(browse && UseNexus);
        ModsSearchBox.Visibility = browse && UseNexus ? Visibility.Hidden : Visibility.Visible;
        ModWebsiteButton.Visibility = UseNexus ? Visibility.Collapsed : Visibility.Visible;
        ModsSearchLabel.Text = browse && UseNexus ? "Browse using Open catalogue" : ModsSearchLabel.Text;
        ModsList.Visibility = browse && UseNexus ? Visibility.Collapsed : Visibility.Visible;
        NexusBrowsePanel.Visibility = browse && UseNexus ? Visibility.Visible : Visibility.Collapsed;
        RefreshSourceButton.Content = UseNexus ? "Open catalogue" : "Refresh online";
        InstallModButton.Content = UseNexus ? "Import ZIP" : "Install selected";
        UpdateAllButton.Content = UseNexus ? "Check updates" : "Update all";
        int filter = ModsFilterCombo.SelectedIndex;
        loadingMods = true;
        ModsFilterCombo.ItemsSource = UseNexus ? new[] { "All mods", "Enabled", "Disabled" } : new[] { "All mods", "Enabled", "Disabled", "Updates available" };
        ModsFilterCombo.SelectedIndex = Math.Clamp(filter,0,UseNexus ? 2 : 3);
        loadingMods = false;
        ModsSearchBox.Clear(); RefreshModsList();
    }
    private async Task RefreshCatalog()
    {
        if (catalogRefreshRunning) return;
        catalogRefreshRunning = true;
        RefreshNotifications();
        ModNoticeText.Text = "Refreshing Thunderstore catalogue…";
        try { catalogMods = await ModCatalog.Refresh(runtime.ShellModsRoot, new Progress<string>(s => Dispatcher.Invoke(() => ModNoticeText.Text = s)), CancellationToken.None); notificationCatalogFailed = false; RefreshModsList(); if (!UseNexus) ModNoticeText.Text = catalogMods.Count + " packages available. Update check complete."; }
        catch (Exception ex) { notificationCatalogFailed = true; ModNoticeText.Text = "Catalogue unavailable: " + ex.Message; }
        finally { catalogRefreshRunning = false; RefreshNotifications(); }
    }
    private async Task EnsureCatalogFresh()
    {
        if (catalogMods.Count == 0 || !ModCatalog.CacheIsFresh(runtime.ShellModsRoot, TimeSpan.FromHours(6))) await RefreshCatalog();
    }
    private async void InstallMod_Click(object sender, RoutedEventArgs e)
    {
        if (UseNexus) { ImportNexus_Click(sender,e); return; }
        if (selectedMod == null || ModsProfileCombo.SelectedItem is not ProfileChoice p) return;
        if (catalogMods.Count == 0) await RefreshCatalog();
        var mod = selectedMod.Catalog ?? catalogMods.FirstOrDefault(c => c.Id == selectedMod.Id); if (mod == null) { ModNoticeText.Text = "No online package found for this mod."; return; }
        ModNoticeText.Text = "Installing " + mod.Name + " with its dependencies…";
        try { var requested = ModCatalog.WithProfileFoundation(new[] { mod }, catalogMods, installedMods); await OwnedMods.Install(p.Path, requested, catalogMods, new Progress<string>(s => Dispatcher.Invoke(() => ModNoticeText.Text = s)), CancellationToken.None); RefreshModsList(); ModNoticeText.Text = mod.Name + " " + mod.Version + " installed and ready in " + p.Label + "."; }
        catch (Exception ex) { ModNoticeText.Text = ex.Message; }
    }
    private async void UpdateAllMods_Click(object sender, RoutedEventArgs e)
    {
        if (UseNexus) { if (selectedMod != null && selectedMod.Id != "RuneCompanion") OpenPreferredModPage(selectedMod.Id,selectedMod.Name); else OpenModCatalogue(); return; }
        if (ModsProfileCombo.SelectedItem is not ProfileChoice p) return;
        if (catalogMods.Count == 0) await RefreshCatalog();
        var updates = AvailableUpdates(); if (updates.Count == 0) { ModNoticeText.Text = "No known Thunderstore updates. Check Nexus mods using Open selected page and import a newer ZIP to update."; return; }
        ModNoticeText.Text = "Updating " + updates.Count + " mods with their dependencies…";
        try { await OwnedMods.Install(p.Path, ModCatalog.WithProfileFoundation(updates, catalogMods, installedMods), catalogMods, new Progress<string>(s => Dispatcher.Invoke(() => ModNoticeText.Text = s)), CancellationToken.None); RefreshModsList(); ModNoticeText.Text = updates.Count + " mods updated."; }
        catch (Exception ex) { ModNoticeText.Text = ex.Message; }
    }
    private async void ToggleMod_Click(object sender, RoutedEventArgs e)
    {
        if (selectedMod?.Installed is not InstalledMod mod || ModsProfileCombo.SelectedItem is not ProfileChoice p) return;
        string issue = ModProfiles.DependencyError(installedMods, mod, !mod.Enabled); if (issue.Length > 0) { ModNoticeText.Text = issue; return; }
        try { await Task.Run(() => ModProfiles.Toggle(p.Path, mod.Id)); RefreshModsList(); } catch (Exception ex) { ModNoticeText.Text = ex.Message; }
    }
    private async void RemoveMod_Click(object sender, RoutedEventArgs e)
    {
        if (selectedMod?.Installed is not InstalledMod mod || ModsProfileCombo.SelectedItem is not ProfileChoice p) return;
        if (ShowRuneMessage("Remove " + mod.Name + "? Rune keeps its configuration and a profile backup.", "Remove mod", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await Task.Run(() => OwnedMods.Remove(p.Path, mod.Id)); RefreshModsList(); } catch (Exception ex) { ModNoticeText.Text = ex.Message; }
    }
    private async void RestoreBackup_Click(object sender, RoutedEventArgs e) { if (ModsProfileCombo.SelectedItem is not ProfileChoice p) return; try { await Task.Run(() => OwnedMods.RestoreLast(p.Path)); RefreshModsList(); ModNoticeText.Text = "Previous profile restored."; } catch (Exception ex) { ModNoticeText.Text = ex.Message; } }
    private void EditConfigs_Click(object sender, RoutedEventArgs e)
    {
        if (ModsProfileCombo.SelectedItem is not ProfileChoice profile || selectedMod?.Installed is not InstalledMod mod) { ModNoticeText.Text = "Select an installed mod first."; return; }
        runtime.ShellOpenModConfigs(profile.Path, mod.Id);
    }
    private void TrackCompanionCheck_Changed(object sender, RoutedEventArgs e) { if (!loadingProfile && selectedProfile != null) { bool value=TrackCompanionCheck.IsChecked==true; runtime.ShellSetCompanionTracking(selectedProfile.Id,value); selectedProfile.ShowOnMap=value; CompanionNotice.Text=value ? "Map tracking enabled for this companion." : "Map tracking disabled for this companion."; } }
    private void VoiceVolume_Click(object sender, RoutedEventArgs e)
    {
        CreateVoiceVolumeWindow().ShowDialog(); RefreshSettings();
    }
    private void OpenThunderstore_Click(object sender, RoutedEventArgs e)
    {
        if (selectedMod == null || selectedMod.Id == "RuneCompanion") return;
        OpenPreferredModPage(selectedMod.Id, selectedMod.Name);
    }

    private void LocateValheim_Click(object sender, RoutedEventArgs e) { var picker = new WpfOpenFileDialog { Filter = "Valheim|valheim.exe", FileName = "valheim.exe" }; if (picker.ShowDialog(this) == true) { runtime.ShellSetGamePath(picker.FileName); _ = RefreshHealth(); } }
    private bool installingRequirements;
    private async Task InstallRuneRequirements(ProfileChoice profile)
    {
        if (installingRequirements) return;
        installingRequirements = true; ModsPage.IsEnabled = false;
        ModNoticeText.Text = "Setting up Rune in " + profile.Label + " · BepInEx, Jötunn and PlanBuild…";
        try {
            while (catalogRefreshRunning) await Task.Delay(100);
            await EnsureCatalogFresh();
            var requested = ModCatalog.RuneRequirements(catalogMods, OwnedMods.Read(profile.Path));
            var progress = new Progress<string>(message => ModNoticeText.Text = profile.Label + " · " + message);
            await Task.Run(() => OwnedMods.Install(profile.Path, requested, catalogMods, progress, CancellationToken.None, enableRequested: true));
            SetModsMode(false); RefreshModsList();
            ModNoticeText.Text = "Rune requirements installed and enabled in " + profile.Label + ". You can now Start modded.";
        } catch (Exception ex) {
            ModNoticeText.Text = "Setup did not finish for " + profile.Label + ": " + ex.Message + " Your profile is kept; use Set up required mods to retry.";
        } finally { installingRequirements = false; ModsPage.IsEnabled = true; UpdateModButtons(); }
    }
    private async void InstallRequirements_Click(object sender, RoutedEventArgs e)
    {
        if (ModsProfileCombo.SelectedItem is ProfileChoice profile && ConfirmRequiredModSource()) await InstallRuneRequirements(profile);
    }
    private async void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        string? name = Prompt("New Rune mod profile", "New fellowship");
        if (name == null) return;
        try {
            string path = runtime.ShellCreateModProfile(name); RefreshModProfiles();
            if (ConfirmRequiredModSource()) await InstallRuneRequirements(new ProfileChoice(path, name));
        } catch (Exception ex) { loadingMods = false; ModNoticeText.Text = ex.Message; }
    }
    private void ImportProfile_Click(object sender, RoutedEventArgs e) { var picker = new OpenFolderDialog { Title = "Choose a profile containing BepInEx" }; if (picker.ShowDialog(this) != true) return; string? name = Prompt("Name the independent copy", Path.GetFileName(picker.FolderName)); if (name == null) return; try { runtime.ShellImportModProfile(picker.FolderName, name); RefreshModProfiles(); } catch (Exception ex) { ModNoticeText.Text = ex.Message; } }
    private async void ExportProfileXml_Click(object sender, RoutedEventArgs e)
    {
        if (ModsProfileCombo.SelectedItem is not ProfileChoice profile) return;
        var picker = new Microsoft.Win32.SaveFileDialog { Title = "Export mod profile", Filter = "Rune mod profile XML|*.xml", DefaultExt = ".xml", FileName = profile.Label + ".xml", AddExtension = true };
        if (picker.ShowDialog(this) != true) return;
        ModsPage.IsEnabled = false; ModNoticeText.Text = "Exporting " + profile.Label + "… Large mod packs can take a while.";
        try { await Task.Run(() => ModProfileXml.Export(profile.Path, picker.FileName)); ModNoticeText.Text = "Exported " + profile.Label + " to " + picker.FileName; }
        catch (Exception ex) { ModNoticeText.Text = "Export failed: " + ex.Message; }
        finally { ModsPage.IsEnabled = true; }
    }

    private async void ImportProfileXml_Click(object sender, RoutedEventArgs e)
    {
        var picker = new WpfOpenFileDialog { Title = "Import Rune mod profile", Filter = "Rune mod profile XML|*.xml", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        string? name = Prompt("Name the imported profile", Path.GetFileNameWithoutExtension(picker.FileName));
        if (name == null) return;
        ModsPage.IsEnabled = false; ModNoticeText.Text = "Importing and checking mod files…";
        try { await runtime.ShellImportModProfileXml(picker.FileName, name); selectedMod = null; RefreshModProfiles(); ModNoticeText.Text = "Imported " + name + " as an independent profile. Mods, settings and local blueprints restored."; }
        catch (Exception ex) { ModNoticeText.Text = "Import failed: " + ex.Message; }
        finally { ModsPage.IsEnabled = true; }
    }

    private void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ModsProfileCombo.SelectedItem is not ProfileChoice p) return;
        if (ShowRuneMessage("Remove the " + p.Label + " profile? Rune will move it to a backup. Your other profiles are unaffected.", "Remove mod profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { runtime.ShellRemoveModProfile(p.Path); selectedMod = null; RefreshModProfiles(); ModNoticeText.Text = p.Label + " was removed and kept in Rune's backups."; }
        catch (Exception ex) { ModNoticeText.Text = ex.Message; }
    }
    private string? Prompt(string title, string initial)
    {
        var dialog = CreateProfileNameWindow(title, initial, out var input);
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private void Donate_Click(object sender, RoutedEventArgs e)
    {
        const string recipient = "rokley@gmail.com";
        string url = "https://www.paypal.com/cgi-bin/webscr?cmd=_donations&business=" + Uri.EscapeDataString(recipient)
            + "&item_name=" + Uri.EscapeDataString("Rune Fellowship") + "&currency_code=EUR";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { ShowRuneMessage("Could not open PayPal in your browser. " + ex.Message, "Donate with PayPal"); }
    }

    private void ChatGptSettings_Click(object sender, RoutedEventArgs e) { runtime.ShellOpenChatGpt(); _ = RefreshHealth(); }
    private async void AiModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingSettings || AiModeCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string mode) return;
        runtime.ShellSetAiMode(mode); RefreshSettings(); RefreshVoiceChoices();
        if (mode != "chatgpt") { localHealthMessage = "Starting Qwen…"; LocalReady.Text = "●  " + localHealthMessage; await runtime.ShellEnsureLocalBrain(CancellationToken.None); }
        if (selectedProfile != null) LoadProfile(runtime.ShellProfiles().FirstOrDefault(p => p.Id == selectedProfile.Id) ?? selectedProfile);
        _ = RefreshHealth();
    }
    private void LocalModelSettings_Click(object sender, RoutedEventArgs e) { runtime.ShellOpenLocalModel(); RefreshSettings(); }
    private void AudioSettings_Click(object sender, RoutedEventArgs e) { runtime.ShellOpenAudioSetup(); RefreshSettings(); }
    private void LanguageSettings_Click(object sender, RoutedEventArgs e) { runtime.ShellOpenLanguage(); RefreshSettings(); }
    private void MemorySettings_Click(object sender, RoutedEventArgs e) => runtime.ShellOpenMemory();
    private void SessionReport_Click(object sender, RoutedEventArgs e) => runtime.ShellOpenSessionReport();
    private void CommandLibrary_Click(object sender, RoutedEventArgs e) => ShowPage("Commands");
    private void TaskList_Click(object sender, RoutedEventArgs e) => runtime.ShellOpenPlan();
    private void MicrophoneModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!loadingSettings && MicrophoneModeCombo.SelectedIndex >= 0) runtime.ShellSetMicrophoneMode(MicrophoneModeCombo.SelectedIndex); }
    private void ShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button) return;
        if (shortcutCaptureButton != null && shortcutCaptureButton != button) CancelShortcutCapture();
        shortcutCaptureButton = button;
        shortcutCaptureOriginal = CurrentShortcut(button.Tag?.ToString());
        shortcutCaptureModifiers = ModifierKeys.None;
        runtime.ShellSetShortcutCapture(true);
        button.Content = "Press keys…";
        KeybindHint.Text = "Press a key combination now · Escape cancels · Backspace disables";
        Keyboard.Focus(button);
    }

    private void ShortcutButton_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not WpfButton button || shortcutCaptureButton != button) return;
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { CancelShortcutCapture(); return; }
        if (key is Key.Back or Key.Delete) { CompleteShortcutCapture(button, "Off"); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) {
            ModifierKeys modifier = ModifierForKey(key);
            if (modifier == ModifierKeys.Windows) { KeybindHint.Text = "The Windows key is reserved. Press another key, or Escape to cancel."; return; }
            shortcutCaptureModifiers |= modifier | (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift));
            button.Content = ModifierText(shortcutCaptureModifiers) + " …";
            return;
        }
        string? shortcut = FormatShortcut(key, Keyboard.Modifiers);
        if (shortcut == null) { KeybindHint.Text = "That key cannot be used. Press another key, or Escape to cancel."; return; }
        string conflict = ShortcutConflict(button.Tag?.ToString(), shortcut);
        if (conflict.Length > 0) {
            CancelShortcutCapture();
            ShowRuneMessage(shortcut + " is already assigned to " + conflict + ".", "Keybind conflict", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        CompleteShortcutCapture(button, shortcut);
    }

    private void ShortcutButton_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not WpfButton button || shortcutCaptureButton != button || shortcutCaptureModifiers == ModifierKeys.None) return;
        e.Handled = true;
        Dispatcher.BeginInvoke(() => {
            ModifierKeys held = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
            if (shortcutCaptureButton != button || held != ModifierKeys.None) return;
            string shortcut = ModifierText(shortcutCaptureModifiers);
            string conflict = ShortcutConflict(button.Tag?.ToString(), shortcut);
            if (conflict.Length > 0) {
                CancelShortcutCapture();
                ShowRuneMessage(shortcut + " is already assigned to " + conflict + ".", "Keybind conflict", MessageBoxButton.OK, MessageBoxImage.Information);
            } else CompleteShortcutCapture(button, shortcut);
        }, DispatcherPriority.Input);
    }

    private void ShortcutButton_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not WpfButton button || shortcutCaptureButton != button) return;
        Dispatcher.BeginInvoke(() => { if (shortcutCaptureButton == button && !button.IsKeyboardFocusWithin) CancelShortcutCapture(); }, DispatcherPriority.Input);
    }

    private void CompleteShortcutCapture(WpfButton button, string shortcut)
    {
        string kind = button.Tag?.ToString() ?? throw new InvalidOperationException("Missing shortcut type.");
        runtime.ShellSetShortcut(kind, shortcut);
        shortcutCaptureButton = null;
        shortcutCaptureOriginal = "";
        shortcutCaptureModifiers = ModifierKeys.None;
        runtime.ShellSetShortcutCapture(false);
        KeybindHint.Text = shortcut == "Off" ? "Shortcut disabled. Click it to assign another key." : shortcut + " saved. Click any control to change it.";
        RefreshSettings();
    }

    private void CancelShortcutCapture()
    {
        if (shortcutCaptureButton != null) shortcutCaptureButton.Content = shortcutCaptureOriginal;
        shortcutCaptureButton = null;
        shortcutCaptureOriginal = "";
        shortcutCaptureModifiers = ModifierKeys.None;
        runtime.ShellSetShortcutCapture(false);
        KeybindHint.Text = "Click a control, then press the key combination you want.";
    }

    private string CurrentShortcut(string? kind) => kind switch {
        "mic" => runtime.ShellMicShortcut,
        "switch" => runtime.ShellSwitchShortcut,
        "overlay" => runtime.ShellOverlayShortcut,
        "controls" => runtime.ShellControlsShortcut,
        _ => "Off"
    };

    private string ShortcutConflict(string? kind, string shortcut)
    {
        if (shortcut == "Off") return "";
        var bindings = new[] { (Kind: "mic", Name: "Microphone", Value: runtime.ShellMicShortcut), (Kind: "switch", Name: "Switch companion", Value: runtime.ShellSwitchShortcut), (Kind: "overlay", Name: "Show fellowship", Value: runtime.ShellOverlayShortcut), (Kind: "controls", Name: "Open controls", Value: runtime.ShellControlsShortcut) };
        return bindings.FirstOrDefault(x => x.Kind != kind && string.Equals(x.Value, shortcut, StringComparison.OrdinalIgnoreCase)).Name ?? "";
    }

    internal static string? FormatShortcut(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Windows) != 0) return null;
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0) return null;
        string keyName = ((System.Windows.Forms.Keys)virtualKey).ToString();
        if (keyName.Length == 0 || keyName.Contains(',')) return null;
        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        parts.Add(keyName);
        return string.Join(" + ", parts);
    }

    private static ModifierKeys ModifierForKey(Key key) => key switch {
        Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
        Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
        Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
        Key.LWin or Key.RWin => ModifierKeys.Windows,
        _ => ModifierKeys.None
    };

    private static string ModifierText(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        return string.Join(" + ", parts);
    }
    private void VoiceRepliesCheck_Changed(object sender, RoutedEventArgs e) { if (!loadingSettings) runtime.ShellSetVoiceReplies(VoiceRepliesCheck.IsChecked == true); }
    private void GameOrdersCheck_Changed(object sender, RoutedEventArgs e) { if (!loadingSettings) runtime.ShellSetAllowOrders(GameOrdersCheck.IsChecked == true); }
}


