using System.Text.Json;
using System.Runtime.InteropServices;
using Rune.Shared;

namespace Rune.Voice;

public sealed partial class RuneWindow
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    private readonly string[] skins = { "skeleton", "draugr", "elite", "dwarf", "wolf", "direwolf" };
    private readonly string[] skinLabels = { "Skeleton", "Draugr", "Elite draugr", "Dwarf · player gear", "Wolf", "Direwolf · mount" };
    private readonly ComboBox appearanceChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox profileName = new() { Dock = DockStyle.Fill, MaxLength = 24 };
    private readonly TextBox recognitionName = new() { Dock = DockStyle.Fill, MaxLength = 24 };
    private readonly TextBox personality = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 1800 };
    private readonly Label personalityCount = new() { AutoSize = true, BackColor = Color.Transparent };
    private readonly ComboBox roleChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox combatChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox conversationChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly RuneCheckBox storedMaterials = new() { Text = "Stored materials", AutoSize = true };
    private readonly RuneCheckBox craftBuild = new() { Text = "Craft & build", AutoSize = true };
    private readonly RuneCheckBox cookSort = new() { Text = "Cook & sort", AutoSize = true };
    private readonly RuneCheckBox bossFights = new() { Text = "Boss fights", AutoSize = true };
    private readonly FlowLayoutPanel permissionsHost = new() { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, BackColor = Color.Transparent };
    private readonly Dictionary<CheckBox, Control> permissionTiles = new();
    private readonly PictureBox portrait = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Margin = Padding.Empty };
    private readonly Label portraitTitle = new() { Dock = DockStyle.Top, Height = 32, BackColor = Color.Transparent };
    private readonly Label appearanceHint = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
    private readonly Label companionInfo = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
    private readonly Label profileNotice = new() { AutoSize = true, BackColor = Color.Transparent };
    private readonly Panel contentHost = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
    private readonly Dictionary<string, Control> pages = new();
    private readonly Dictionary<string, Button> navigation = new();
    private readonly Dictionary<string, Image> portraits = new();
    private RowStyle? companionRosterRow;
    private TableLayoutPanel? profileTable;
    private TableLayoutPanel? portraitLayout;
    private RuneBackdrop? sidebar;
    private Control? sidebarBrand;
    private Label? localStatus;
    private bool arrangingCompanions;
    private string selectedPage = "companions";
    private RunePreferences preferences = new();
    private string settingsPath = "";
    private bool restoring;

    private CompanionProfile CurrentProfile => preferences.Profiles.First(p => p.Id == bridge.CompanionId);
    private string DisplayName => CurrentProfile.Name;

    private void BuildInterface(string folder)
    {
        Text = "Rune Fellowship 0.3.10 · clear in-game goals"; FormBorderStyle = FormBorderStyle.None; DoubleBuffered = true;
        ClientSize = new Size(1360, 850); MinimumSize = new Size(1180, 800); StartPosition = FormStartPosition.CenterScreen;
        BackColor = RuneTheme.Coal; ForeColor = RuneTheme.Bone; Font = new Font("Segoe UI", 10); Padding = new Padding(7);
        BackgroundImage = null;
        settingsPath = Path.Combine(folder, "preferences.json"); preferences = ProfileStore.Load(settingsPath);
        brain.Language = preferences.Language; brain.Notes = preferences.Notes; brain.LocalModel = preferences.LocalModel;
        voice.Checked = preferences.VoiceReplies; neural.Volume = preferences.VoiceVolume;

        contentHost.Dock = DockStyle.None; Controls.Add(contentHost); Controls.Add(BuildSidebar());
        var chrome = BuildWindowChrome(); chrome.Dock = DockStyle.Top; chrome.Height = 30; Controls.Add(chrome);
        pages["companions"] = BuildCompanionsPage(); pages["conversation"] = BuildConversationPage(); pages["tasks"] = BuildTasksPage(); pages["settings"] = BuildSettingsPage(); pages["keybinds"] = BuildKeybindsPage(); pages["mods"] = BuildModsPage();
        foreach (var page in pages.Values) { page.Visible = false; contentHost.Controls.Add(page); }
        SelectCompanion(preferences.Profiles[0].Id); ShowPage("companions"); UpdateRoster(bridge.State());
        SizeChanged += (_, _) => LayoutArtworkRegions(); LayoutArtworkRegions();
    }

    private Control BuildWindowChrome()
    {
        var chrome = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
        var controls = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 132, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
        var minimize = ChromeControl(RuneWindowGlyph.Minimize); minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var maximize = ChromeControl(RuneWindowGlyph.Maximize); maximize.Click += (_, _) => ToggleMaximize();
        var close = ChromeControl(RuneWindowGlyph.Close); close.Click += (_, _) => Close();
        controls.Controls.Add(minimize); controls.Controls.Add(maximize); controls.Controls.Add(close); chrome.Controls.Add(controls);
        MouseEventHandler drag = (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } };
        chrome.MouseDown += drag; chrome.DoubleClick += (_, _) => ToggleMaximize(); return chrome;
    }

    private static RuneWindowControl ChromeControl(RuneWindowGlyph glyph) => new(glyph);

    private void ToggleMaximize() { if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal; else { MaximizedBounds = Screen.FromControl(this).WorkingArea; WindowState = FormWindowState.Maximized; } }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message); if (message.Msg != 0x84 || WindowState == FormWindowState.Maximized || (int)message.Result != 1) return;
        var point = PointToClient(new Point((short)((long)message.LParam & 0xffff), (short)(((long)message.LParam >> 16) & 0xffff))); int grip = 8;
        bool left = point.X < grip, right = point.X >= ClientSize.Width - grip, top = point.Y < grip, bottom = point.Y >= ClientSize.Height - grip;
        message.Result = (IntPtr)(top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 1);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => RuneTheme.DrawFullSurface(e.Graphics, ClientRectangle);

    private Control BuildSidebar()
    {
        var side = new RuneBackdrop { Margin = Padding.Empty, Padding = Padding.Empty }; sidebar = side;
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0), Margin = Padding.Empty };
        stack.Controls.Add(new Label { Text = "ᚱ", AutoSize = false, Width = 205, Height = 44, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter, ForeColor = RuneTheme.Amber, Font = new Font("Georgia", 27, FontStyle.Bold) });
        stack.Controls.Add(new Label { Text = "R U N E", AutoSize = false, Width = 205, Height = 42, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter, ForeColor = RuneTheme.Bone, Font = new Font("Georgia", 22, FontStyle.Bold) });
        stack.Controls.Add(new Label { Text = "F E L L O W S H I P", AutoSize = false, Width = 205, Height = 38, BackColor = Color.Transparent, TextAlign = ContentAlignment.TopCenter, ForeColor = RuneTheme.AmberSoft, Font = new Font("Georgia", 10, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) });
        stack.Dock = DockStyle.None; sidebarBrand = stack; side.Controls.Add(stack);
        AddNav(side, "companions", "♟   Companions"); AddNav(side, "conversation", "●   Conversation"); AddNav(side, "tasks", "⚒   Tasks"); AddNav(side, "settings", "⚙   Settings"); AddNav(side, "keybinds", "⌨   Keybinds"); AddNav(side, "mods", "◇   Mods");
        localStatus = new Label { Text = preferences.ChatGptEnabled ? "● CHATGPT COMMANDS\nQwen dialogue & voice" : "● LOCAL AI\nChatGPT commands OFF", BackColor = Color.Transparent, ForeColor = RuneTheme.Good, Font = new Font("Segoe UI Semibold", 9), TextAlign = ContentAlignment.MiddleCenter, Margin = Padding.Empty };
        side.Controls.Add(localStatus); return side;
    }

    private void LayoutArtworkRegions()
    {
        if (sidebar == null || sidebarBrand == null || localStatus == null) return;
        var nav = RuneTheme.ArtworkBounds(ClientSize, new Rectangle(24, 184, 272, 236));
        int left = Math.Max(20, nav.Left), right = Math.Max(left + 190, nav.Right);
        sidebar.Bounds = new Rectangle(left, 40, right - left, ClientSize.Height - 60);
        sidebarBrand.Bounds = new Rectangle(0, 12, sidebar.Width, 132);
        foreach (Control label in sidebarBrand.Controls) { label.Width = sidebar.Width; label.Margin = Padding.Empty; }
        int rowHeight = Math.Clamp(nav.Height / navigation.Count - 4, 30, 42), index = 0;
        foreach (var pair in navigation)
        {
            pair.Value.Bounds = new Rectangle(0, nav.Top - sidebar.Top + index++ * (rowHeight + 5), sidebar.Width, rowHeight);
            RuneTheme.SetButtonTone(pair.Value, pair.Key == selectedPage ? RuneButtonTone.NavigationSelected : RuneButtonTone.Navigation);
        }
        var status = RuneTheme.ArtworkBounds(ClientSize, new Rectangle(82, 768, 144, 52));
        int statusLeft = Math.Max(0, status.Left - sidebar.Left);
        localStatus.Bounds = new Rectangle(statusLeft, Math.Min(status.Top - sidebar.Top, sidebar.Height - 52), Math.Min(status.Width, sidebar.Width - statusLeft), 52);
        var content = RuneTheme.ArtworkBounds(ClientSize, new Rectangle(342, 40, 972, 784));
        int contentRight = Math.Min(ClientSize.Width - 28, content.Right);
        contentHost.Bounds = new Rectangle(content.Left, 40, contentRight - content.Left, Math.Min(ClientSize.Height - 22, content.Bottom) - 40);
        UpdateCompanionLayout(); Invalidate(true);
    }

    private void AddNav(Control parent, string id, string text)
    {
        var button = new Button { Tag = id, Text = text, Size = new Size(205, 54), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = RuneTheme.Bone, Font = new Font("Segoe UI", 12), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 5) };
        RuneTheme.SetButtonTone(button, RuneButtonTone.Navigation);
        button.FlatAppearance.BorderColor = RuneTheme.Iron; button.Click += (_, _) => ShowPage(id); navigation[id] = button; parent.Controls.Add(button);
    }

    private void ShowPage(string id)
    {
        selectedPage = id;
        foreach (var pair in pages) pair.Value.Visible = pair.Key == id;
        foreach (var pair in navigation) { bool selected = pair.Key == id; pair.Value.BackColor = Color.Transparent; pair.Value.ForeColor = selected ? RuneTheme.AmberSoft : RuneTheme.Bone; RuneTheme.SetButtonTone(pair.Value, selected ? RuneButtonTone.NavigationSelected : RuneButtonTone.Navigation); }
        if (pages.TryGetValue(id, out var page)) page.BringToFront();
        if (id == "mods") RefreshMods();
        if (id == "settings") { RefreshSettingsSummary(); if (IsHandleCreated && Opacity > 0) _ = RefreshAccountOverview(); }
    }

    private Control PageFrame(string title, string subtitle, Control body)
    {
        var page = new RuneSurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(14, 18, 14, 14) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = RuneTheme.Bone, Font = new Font("Georgia", 21, FontStyle.Bold) }, 0, 0);
        layout.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = RuneTheme.Muted, Font = new Font("Segoe UI", 9) }, 0, 1); layout.Controls.Add(body, 0, 2); page.Controls.Add(layout); return page;
    }

    private Control BuildCompanionsPage()
    {
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Color.Transparent };
        companionRosterRow = new RowStyle(SizeType.Absolute, preferences.Profiles.Length <= 3 ? 76 : 140); body.RowStyles.Add(companionRosterRow); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var cards = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = false, Margin = Padding.Empty, BackColor = Color.Transparent }; BuildCompanionCards(cards); body.Controls.Add(cards, 0, 0);
        var editor = new RuneSurfaceTable { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); editor.Controls.Add(BuildPortraitPanel(), 0, 0); editor.Controls.Add(BuildProfileEditor(), 1, 0); body.Controls.Add(editor, 0, 1);
        return PageFrame("Your Fellowship", "Create distinct local companions. Profiles and conversations stay on this PC.", body);
    }

    private Control BuildPortraitPanel()
    {
        var panel = new RunePanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0), Padding = new Padding(12, 8, 12, 12) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, BackColor = Color.Transparent, Padding = Padding.Empty, Margin = Padding.Empty }; portraitLayout = layout;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 200)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.SizeChanged += (_, _) => FitPortraitHeight();
        layout.Controls.Add(portrait, 0, 0); portraitTitle.ForeColor = RuneTheme.AmberSoft; portraitTitle.Font = new Font("Georgia", 14, FontStyle.Bold); portraitTitle.Padding = new Padding(8, 5, 0, 0); layout.Controls.Add(portraitTitle, 0, 1);
        appearanceHint.ForeColor = RuneTheme.Muted; appearanceHint.Font = new Font("Segoe UI", 9); appearanceHint.Padding = new Padding(8, 4, 8, 0); layout.Controls.Add(appearanceHint, 0, 2);
        companionInfo.ForeColor = RuneTheme.Bone; companionInfo.Font = new Font("Segoe UI", 9); companionInfo.Padding = new Padding(8, 8, 8, 0); layout.Controls.Add(companionInfo, 0, 3); panel.Controls.Add(layout); return panel;
    }

    private void FitPortraitHeight()
    {
        if (portraitLayout == null || portrait.Image == null) return;
        // Reserve only the image's natural height; spare space belongs below the details.
        int imageHeight = (int)Math.Round(portraitLayout.ClientSize.Width * portrait.Image.Height / (double)portrait.Image.Width);
        portraitLayout.RowStyles[0].Height = Math.Min(imageHeight, Math.Max(80, portraitLayout.ClientSize.Height - 212));
    }

    private Control BuildProfileEditor()
    {
        foreach (var field in new Control[] { profileName, recognitionName, personality, appearanceChoice, roleChoice, combatChoice, conversationChoice }) RuneTheme.Field(field);
        appearanceChoice.Items.AddRange(skinLabels); roleChoice.Items.AddRange(new object[] { "Builder & Gatherer", "Scout & Gatherer", "Guard & Explorer", "Quartermaster & Cook", "Balanced companion" }); combatChoice.Items.AddRange(new object[] { "Cautious", "Balanced", "Aggressive" }); conversationChoice.Items.AddRange(new object[] { "Quiet", "Natural", "Talkative" });
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = false, BackColor = Color.Transparent, Padding = new Padding(8, 8, 8, 4) };
        profileTable = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, ColumnCount = 2, RowCount = 6, BackColor = Color.Transparent, Margin = Padding.Empty }; profileTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); profileTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (float height in ProfileRowHeights()) profileTable.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        AddField(profileTable, 0, 0, "Name", profileName); AddField(profileTable, 1, 0, "Recognition nickname", recognitionName); AddField(profileTable, 0, 1, "Appearance", appearanceChoice); AddField(profileTable, 1, 1, "Voice", BuildVoiceChoices());
        var personalityPanel = new TableLayoutPanel { Dock = DockStyle.Fill, Height = 96, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 0, 8, 4), BackColor = Color.Transparent }; personalityPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); personalityPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); personality.Dock = DockStyle.Fill; personalityPanel.Controls.Add(personality, 0, 0); personalityCount.ForeColor = RuneTheme.Muted; personalityCount.Dock = DockStyle.Fill; personalityCount.TextAlign = ContentAlignment.MiddleRight; personalityPanel.Controls.Add(personalityCount, 0, 1); AddField(profileTable, 0, 2, "Personality · describe voice, humor, and temperament", personalityPanel, 2);
        AddField(profileTable, 0, 3, "Role", roleChoice); AddField(profileTable, 1, 3, "Combat style", combatChoice); AddField(profileTable, 0, 4, "Conversation frequency", conversationChoice, 2);
        BuildPermissionTiles(); AddField(profileTable, 0, 5, "Permissions", permissionsHost, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 52, FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent, Margin = new Padding(0, 8, 12, 0) };
        var summon = RuneTheme.Button("Summon", true); summon.Click += async (_, _) => { if (!SaveProfile()) return; if (bridge.State().roster.Any(c => c.id == bridge.CompanionId)) await SyncProfileToGame(); else await Submit("summon"); };
        var save = RuneTheme.Button("Save companion"); save.Click += async (_, _) => { if (SaveProfile()) await SyncProfileToGame(); }; var talk = RuneTheme.Button("Talk"); talk.Click += (_, _) => ShowPage("conversation"); var remove = RuneTheme.Button("Remove", danger: true); remove.Click += async (_, _) => await RemoveCurrentCompanion(); buttons.Controls.Add(summon); buttons.Controls.Add(save); buttons.Controls.Add(talk); buttons.Controls.Add(remove);
        profileNotice.ForeColor = RuneTheme.Good; profileNotice.Dock = DockStyle.Fill; profileNotice.AutoSize = false; profileNotice.AutoEllipsis = true; profileNotice.TextAlign = ContentAlignment.MiddleCenter; profileNotice.Margin = new Padding(8, 0, 8, 0);
        appearanceChoice.SelectedIndexChanged += (_, _) => { if (!restoring) { SetCapabilityAvailability(); UpdatePortrait(); if (CurrentProfile.Appearance != skins[Math.Max(0, appearanceChoice.SelectedIndex)] && bridge.State().roster.Any(c => c.id == bridge.CompanionId)) { profileNotice.ForeColor = RuneTheme.AmberSoft; profileNotice.Text = "Changing body type will rebuild this summoned companion after you save."; } } };
        personality.TextChanged += (_, _) => UpdateWordCount(); scroll.Controls.Add(profileTable);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Color.Transparent }; root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); root.Controls.Add(scroll, 0, 0);
        var footerHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty }; footerHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); footerHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 490)); footerHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        footerHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); footerHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var footer = new RuneSurfaceTable { Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0, 3, 0, 3) }; footer.Controls.Add(buttons, 0, 0); footerHost.Controls.Add(profileNotice, 0, 0); footerHost.SetColumnSpan(profileNotice, 3); footerHost.Controls.Add(footer, 1, 1); root.Controls.Add(footerHost, 0, 1); return root;
    }

    private static void AddField(TableLayoutPanel table, int column, int row, string label, Control field, int span = 1)
    {
        var group = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, RowCount = string.IsNullOrEmpty(label) ? 1 : 2, BackColor = Color.Transparent, Margin = new Padding(0, 0, column == 0 ? 8 : 0, 2) };
        if (!string.IsNullOrEmpty(label)) { group.RowStyles.Add(new RowStyle(SizeType.Absolute, 21)); group.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); group.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = RuneTheme.Muted, Font = new Font("Segoe UI Semibold", 9) }, 0, 0); group.Controls.Add(field, 0, 1); } else group.Controls.Add(field, 0, 0);
        table.Controls.Add(group, column, row); if (span > 1) table.SetColumnSpan(group, span);
    }

    private float[] ProfileRowHeights() => companionRosterRow == null || companionRosterRow.Height <= 76
        ? new[] { 54f, 64f, 158f, 62f, 62f, 72f }
        : new[] { 54f, 58f, 102f, 54f, 54f, 72f };

    private void UpdateCompanionLayout()
    {
        if (arrangingCompanions || companionCardsHost == null || companionCardsHost.ClientSize.Width < 100) return;
        arrangingCompanions = true;
        try
        {
            int count = companionCardsHost.Controls.Count;
            int columns = Math.Max(1, Math.Min(count, Math.Min(5, companionCardsHost.ClientSize.Width / 174)));
            int width = (companionCardsHost.ClientSize.Width - (columns - 1) * 8) / columns;
            int i = 0;
            foreach (Button card in companionCardsHost.Controls)
            {
                card.Width = width; card.Margin = new Padding(0, 0, ++i % columns == 0 ? 0 : 8, 6);
                bool selected = card.Tag is string id && id == bridge.CompanionId;
                RuneTheme.SetButtonTone(card, card.Tag == null ? RuneButtonTone.Primary : selected ? RuneButtonTone.Selected : RuneButtonTone.Neutral);
            }
            if (companionRosterRow != null) companionRosterRow.Height = ((count + columns - 1) / columns) * 68 + 8;
            if (profileTable != null) { var heights = ProfileRowHeights(); for (int row = 0; row < heights.Length && row < profileTable.RowStyles.Count; row++) profileTable.RowStyles[row].Height = heights[row]; }
        }
        finally { arrangingCompanions = false; }
    }

    private void BuildPermissionTiles()
    {
        permissionsHost.Controls.Clear(); permissionTiles.Clear();
        AddPermissionTile(storedMaterials, "Use chest supplies");
        AddPermissionTile(craftBuild, "Make items and structures");
        AddPermissionTile(cookSort, "Cook and sort storage");
        AddPermissionTile(bossFights, "Join boss encounters");
    }

    private void AddPermissionTile(CheckBox option, string description)
    {
        var tile = new RunePanel { Width = 128, Height = 47, Margin = new Padding(0, 0, 7, 0), Padding = new Padding(7, 2, 5, 2) };
        option.Font = new Font("Segoe UI Semibold", 8); option.ForeColor = RuneTheme.Bone; option.Location = new Point(7, 2); option.Margin = Padding.Empty;
        tile.Controls.Add(new Label { Text = description, Location = new Point(8, 24), Size = new Size(115, 19), BackColor = Color.Transparent, Font = new Font("Segoe UI", 6.5f), ForeColor = RuneTheme.Muted });
        tile.Controls.Add(option); permissionTiles[option] = tile; permissionsHost.Controls.Add(tile);
    }

    private Control BuildConversationPage()
    {
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent }; body.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 47)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        connection.Dock = DockStyle.Fill; connection.ForeColor = RuneTheme.Good; body.Controls.Add(connection, 0, 0); transcript.Dock = DockStyle.Fill; transcript.ReadOnly = true; transcript.BackColor = RuneTheme.Panel; transcript.ForeColor = RuneTheme.Bone; transcript.BorderStyle = BorderStyle.FixedSingle; transcript.Font = new Font("Segoe UI", 11); transcript.Margin = new Padding(0, 0, 0, 10); body.Controls.Add(transcript, 0, 1);
        input.Dock = DockStyle.Fill; RuneTheme.Field(input); input.PlaceholderText = "Talk about your day, ask anything, or give an order…"; input.MaxLength = 1000; input.Font = new Font("Segoe UI", 11); input.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; var text = input.Text; input.Clear(); await Submit(text); } }; body.Controls.Add(input, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent }; AddButton(actions, "Send", async () => { var text = input.Text; input.Clear(); await Submit(text); }, true); var talk = RuneTheme.Button("Hold to talk"); conversationTalk = talk; talk.Text = AlwaysOn ? (microphoneMuted ? "Unmute mic" : "Mute mic") : "Hold to talk"; talk.MouseDown += (_, _) => { if (AlwaysOn) { ToggleMicrophoneMute(); return; } if (microphoneMode.SelectedIndex != 1) microphoneMode.SelectedIndex = 1; buttonDown = true; BeginListening(); }; talk.MouseUp += (_, _) => { buttonDown = false; EndListening(); }; talk.MouseCaptureChanged += (_, _) => { if (buttonDown && !talk.Capture) { buttonDown = false; EndListening(); } }; actions.Controls.Add(talk); AddButton(actions, "Stop task", async () => await Submit("stop")); AddButton(actions, "Commands", () => { ShowCommands(); return Task.CompletedTask; }); body.Controls.Add(actions, 0, 3);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent }; voice.Text = "Speak replies"; voice.ForeColor = RuneTheme.Bone; options.Controls.Add(voice); options.Controls.Add(new Label { Text = "Qwen dialogue & local voices · ChatGPT commands available in Settings.", AutoSize = true, BackColor = Color.Transparent, ForeColor = RuneTheme.Muted, Margin = new Padding(22, 4, 0, 0) }); body.Controls.Add(options, 0, 4); listeningLabel.Dock = DockStyle.Fill; listeningLabel.BackColor = Color.Transparent; listeningLabel.ForeColor = RuneTheme.Muted; body.Controls.Add(listeningLabel, 0, 5); return PageFrame("Conversation", "Only the selected companion receives speech. Change companions with your keybind or a companion card.", body);
    }

    private Control BuildTasksPage()
    {
        var panel = new RunePanel { Dock = DockStyle.Fill, Padding = new Padding(24) }; var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent }; stack.Controls.Add(RuneTheme.Label("Task queue", 17, true)); stack.Controls.Add(new Label { Text = "Ask for a task list, review every step, then start it in Valheim.\nPlans can gather materials, craft known recipes, build boats, cook food, and organize storage.", AutoSize = false, Width = 850, Height = 70, BackColor = Color.Transparent, ForeColor = RuneTheme.Muted, Margin = new Padding(0, 12, 0, 18) }); var open = RuneTheme.Button("Open selected companion's task list", true); open.Click += (_, _) => OpenPlan(); stack.Controls.Add(open); var command = RuneTheme.Button("Browse command library"); command.Click += (_, _) => ShowCommands(); stack.Controls.Add(command); panel.Controls.Add(stack); return PageFrame("Tasks", "Structured work for gathering, crafting, base management, and boats.", panel);
    }

    private bool SaveProfile()
    {
        int words = WordCount(personality.Text); if (profileName.Text.Trim().Length < 2) { FailProfile("Give this companion a name of at least two characters."); return false; } if (recognitionName.Text.Trim().Length < 2) { FailProfile("Give voice recognition a nickname of at least two characters."); return false; } if (preferences.Profiles.Any(p => p.Id != bridge.CompanionId && (p.Name.Equals(profileName.Text.Trim(), StringComparison.OrdinalIgnoreCase) || p.RecognitionName.Equals(recognitionName.Text.Trim(), StringComparison.OrdinalIgnoreCase)))) { FailProfile("Names and recognition nicknames must be unique in the fellowship."); return false; } if (words > 200) { FailProfile("Personality is " + words + " words. Shorten it to 200 words."); return false; }
        string newAppearance = skins[Math.Max(0, appearanceChoice.SelectedIndex)]; bool bodyChange = CurrentProfile.Appearance != newAppearance && bridge.State().roster.Any(c => c.id == bridge.CompanionId);
        if (bodyChange && MessageBox.Show(this, "Changing body type rebuilds the companion in place and stops their active task. Their name, memories, health, inventory, base, and pickup exclusions will be preserved. Borrowed player gear must be returned first.\n\nChange " + CurrentProfile.Name + " from " + FriendlyAppearance(CurrentProfile.Appearance) + " to " + FriendlyAppearance(newAppearance) + "?", "Change companion body", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { LoadProfileEditor(); return false; }
        var profile = CurrentProfile; profile.Name = profileName.Text.Trim(); profile.RecognitionName = recognitionName.Text.Trim(); profile.Appearance = newAppearance; profile.Voice = CurrentVoiceId; profile.Personality = personality.Text.Trim(); profile.Traits = Array.Empty<string>(); profile.Role = roleChoice.Text; profile.CombatStyle = combatChoice.Text; profile.ConversationFrequency = conversationChoice.Text; profile.UseStoredMaterials = storedMaterials.Checked; profile.CraftAndBuild = craftBuild.Checked; profile.CookAndSort = cookSort.Checked; profile.JoinBossFights = bossFights.Checked;
        ApplyProfileToRuntime(profile); SavePreferences(); UpdateCompanionCards(); profileNotice.ForeColor = RuneTheme.Good; profileNotice.Text = profile.Name + " saved locally."; return true;
    }

    private void FailProfile(string message) { profileNotice.ForeColor = Color.IndianRed; profileNotice.Text = message; }
    private async Task SyncProfileToGame() { var state = bridge.State(); if (!state.ready || !state.roster.Any(c => c.id == bridge.CompanionId)) return; var reply = await bridge.Send(new Command { action = "update_profile" }, turn.Token); profileNotice.Text = reply.message; }

    private void LoadProfileEditor()
    {
        restoring = true; var p = CurrentProfile; profileName.Text = p.Name; recognitionName.Text = p.RecognitionName; personality.Text = p.Personality; appearanceChoice.SelectedIndex = Math.Max(0, Array.IndexOf(skins, p.Appearance)); SetVoice(p.Voice); roleChoice.SelectedItem = p.Role; if (roleChoice.SelectedIndex < 0) roleChoice.SelectedIndex = 4; combatChoice.SelectedItem = p.CombatStyle; if (combatChoice.SelectedIndex < 0) combatChoice.SelectedIndex = 1; conversationChoice.SelectedItem = p.ConversationFrequency; if (conversationChoice.SelectedIndex < 0) conversationChoice.SelectedIndex = 1; storedMaterials.Checked = p.UseStoredMaterials; craftBuild.Checked = p.CraftAndBuild; cookSort.Checked = p.CookAndSort; bossFights.Checked = p.JoinBossFights; restoring = false; profileNotice.Text = ""; UpdateWordCount(); SetCapabilityAvailability(); UpdatePortrait();
    }

    private void ApplyProfileToRuntime(CompanionProfile profile)
    {
        nextPerformance = DateTime.MinValue;
        bridge.CompanionId = profile.Id; bridge.Appearance = profile.Appearance; bridge.DisplayName = profile.Name; bridge.Gender = VoiceGender(profile.Voice); bridge.CombatStyle = profile.CombatStyle; bridge.JoinBossFights = profile.JoinBossFights; bridge.UseStoredMaterials = profile.UseStoredMaterials; bridge.AllowCrafting = profile.CraftAndBuild; bridge.AllowBaseWork = profile.CookAndSort; brain.CompanionId = profile.Id; brain.DisplayName = profile.Name; brain.Personality = profile.Personality; brain.Role = profile.Role; brain.CombatStyle = profile.CombatStyle; brain.ConversationFrequency = profile.ConversationFrequency; brain.Permissions = new[] { profile.UseStoredMaterials ? "may use stored materials" : "must not use stored materials", profile.CraftAndBuild ? "may craft and build" : "must not craft or build", profile.CookAndSort ? "may cook and sort storage" : "must not cook or sort storage", profile.JoinBossFights ? "may join boss fights" : "must avoid boss fights" }; neural.Engine = VoiceCatalog.NormalizeEngine(profile.VoiceEngine); neural.VoiceId = profile.Voice; neural.Language = preferences.Language; neural.Personality = profile.Personality;
    }

    private void SelectCompanion(string id) { if (!preferences.Profiles.Any(p => p.Id == id)) return; ResetRecipientAudio(); CancelTurn(); bridge.CompanionId = id; ApplyProfileToRuntime(CurrentProfile); LoadProfileEditor(); UpdateRoster(bridge.State()); }
    private void UpdateRoster(GameState state) { UpdateCompanionCards(state); var member = state.roster?.FirstOrDefault(c => c.id == bridge.CompanionId); companionInfo.Text = member == null ? "Not nearby\n\n150 base health · 32 inventory slots\nIndependent of your player stats" : $"{member.task}\n\nHealth  {member.health:0} / {member.maxHealth:0}    Cargo  {member.cargo}\nBase  {(member.hasBase ? "Remembered" : "Not set")}"; }

    private void SetCapabilityAvailability()
    {
        string skin = appearanceChoice.SelectedIndex >= 0 ? skins[appearanceChoice.SelectedIndex] : "skeleton"; bool wolf = Rules.IsWolf(skin);
        if (wolf) { storedMaterials.Checked = false; craftBuild.Checked = false; cookSort.Checked = false; }
        foreach (var option in new[] { storedMaterials, craftBuild, cookSort }) if (permissionTiles.TryGetValue(option, out var tile)) tile.Visible = !wolf;
        string currentRole = roleChoice.Text; string[] allowedRoles = wolf ? new[] { "Scout & Gatherer", "Guard & Explorer", "Balanced companion" } : new[] { "Builder & Gatherer", "Scout & Gatherer", "Guard & Explorer", "Quartermaster & Cook", "Balanced companion" };
        if (roleChoice.Items.Count != allowedRoles.Length || !roleChoice.Items.Cast<object>().Select(x => x.ToString() ?? "").SequenceEqual(allowedRoles)) { roleChoice.Items.Clear(); roleChoice.Items.AddRange(allowedRoles); }
        roleChoice.SelectedItem = allowedRoles.Contains(currentRole) ? currentRole : wolf ? "Guard & Explorer" : "Balanced companion";
        permissionsHost.PerformLayout();
    }
    private void UpdatePortrait()
    {
        string skin = appearanceChoice.SelectedIndex >= 0 ? skins[appearanceChoice.SelectedIndex] : CurrentProfile.Appearance; string file = skin == "dwarf" ? "dwarf-" + VoiceGender(CurrentVoiceId) + ".png" : skin + ".png"; if (!portraits.TryGetValue(file, out var image)) { string path = Path.Combine(AppContext.BaseDirectory, "Assets", file); if (File.Exists(path)) { image = RuneTheme.LoadArtwork(path); portraits[file] = image; } } portrait.Image = image; portraitTitle.Text = FriendlyAppearance(skin); appearanceHint.Text = skin switch { "direwolf" => "Saddled direwolf companion with running and bite animations. Interact with its saddle to ride; use Attack to bite. Cannot wear armour or use tools. Riding is awaiting in-game validation.", "wolf" => "Fast animal companion with a bite attack. Can fight and collect loose items, but cannot wear armour or use tools.", "dwarf" => (VoiceGender(CurrentVoiceId) == "female" ? "Female" : "Male") + " player-style dwarf. Can wear real armour and use weapons and tools.", "skeleton" => "Undead humanoid with native movement and weapons. Can use tools, craft, cook, and manage storage.", "draugr" => "Durable undead worker with native movement and weapons. Can use tools and manage your base.", _ => "Heavy undead warrior with native movement and weapons. Can use tools and perform base work." };
        FitPortraitHeight();
    }

    private void UpdateWordCount() { int words = WordCount(personality.Text); personalityCount.Text = words + " / 200 words"; personalityCount.ForeColor = words > 200 ? Color.IndianRed : RuneTheme.Muted; }
    private static int WordCount(string text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    private void SavePreferences() { preferences.Notes = brain.Notes; preferences.LocalModel = brain.LocalModel; preferences.MicrophoneKey = microphoneKey.SelectedIndex; preferences.MicrophoneMode = microphoneMode.SelectedIndex; preferences.MicrophoneDevice = microphoneDevice; preferences.OutputDeviceName = neural.OutputDeviceName; preferences.AllowGameOrders = allowGameOrders.Checked; preferences.VoiceReplies = voice.Checked; File.WriteAllText(settingsPath, JsonSerializer.Serialize(preferences, Brain.Json)); }

    private void ShowMemory()
    {
        using var dialog = ThemedDialog("Memory & personal notes", new Size(640, 430)); var text = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(24, 92), Size = new Size(590, 220), Text = brain.Notes, MaxLength = 3000 }; RuneTheme.Field(text); dialog.Controls.Add(new Label { Text = "What should every companion remember about you?\nConversations are saved locally and separately for each companion and world.", Location = new Point(24, 22), Size = new Size(590, 58), ForeColor = RuneTheme.Muted }); dialog.Controls.Add(text); var save = RuneTheme.Button("Save notes", true); save.Location = new Point(24, 335); save.Click += (_, _) => { brain.Notes = text.Text; SavePreferences(); dialog.Close(); }; dialog.Controls.Add(save); var forget = RuneTheme.Button("Forget all conversations"); forget.Location = new Point(150, 335); forget.Click += (_, _) => { CancelTurn(); brain.ClearMemory(); transcript.Clear(); text.Clear(); brain.Notes = ""; SavePreferences(); AddLine(DisplayName, "A fresh start. What shall we do next?"); dialog.Close(); }; dialog.Controls.Add(forget); dialog.ShowDialog(this);
    }
    private Form ThemedDialog(string title, Size size) => new RunePopupForm() { Text = title, ClientSize = size, StartPosition = FormStartPosition.CenterParent, BackColor = RuneTheme.Stone, ForeColor = RuneTheme.Bone, Font = Font };
    private void ShowCommands()
    {
        if (ShellCommandsRequested != null) { ShellCommandsRequested(); return; }
        using var dialog = ThemedDialog("Command library", new Size(820, 640)); var search = new TextBox { PlaceholderText = "Find a command…", Dock = DockStyle.Top, Font = new Font("Segoe UI", 11), Height = 38 }; RuneTheme.Field(search); var rows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16), BackColor = RuneTheme.Stone };
        var examples = Rules.Capabilities.Where(c => !c.hidden).Select(c => (Group:c.group, Command:c.example, Detail:c.description)).ToArray();
        void Populate() { rows.SuspendLayout(); foreach (Control child in rows.Controls.Cast<Control>().ToArray()) child.Dispose(); rows.Controls.Clear(); foreach (var example in examples.Where(e => (e.Group + e.Command + e.Detail).Contains(search.Text, StringComparison.OrdinalIgnoreCase))) { var row = new RunePanel { Width = 740, Height = 72, Margin = new Padding(0, 0, 0, 8) }; row.Controls.Add(new Label { Text = example.Group.ToUpperInvariant() + "  /  “" + example.Command + "”", Location = new Point(14, 9), Size = new Size(580, 22), ForeColor = RuneTheme.AmberSoft, Font = new Font("Segoe UI Semibold", 10) }); row.Controls.Add(new Label { Text = example.Detail, Location = new Point(14, 35), Size = new Size(580, 28), ForeColor = RuneTheme.Muted }); var use = RuneTheme.Button("Use"); use.Location = new Point(640, 16); use.Click += (_, _) => { input.Text = example.Command; dialog.Close(); ShowPage("conversation"); input.Focus(); }; row.Controls.Add(use); rows.Controls.Add(row); } rows.ResumeLayout(); }
        search.TextChanged += (_, _) => Populate(); dialog.Controls.Add(rows); dialog.Controls.Add(search); Populate(); dialog.ShowDialog(this);
    }
}
