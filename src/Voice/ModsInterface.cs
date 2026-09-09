using System.Diagnostics;
namespace Rune.Voice;

public sealed partial class RuneWindow
{
    private ComboBox modProfiles = null!, modFilter = null!;
    private TextBox modSearch = null!;
    private DataGridView modGrid = null!;
    private Label modNotice = null!, modDetails = null!;
    private List<InstalledMod> installedMods = new();
    private List<CatalogMod> catalogMods = new();
    private bool modBusy;
    private CancellationTokenSource? modCancellation;
    private string ModsRoot => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(bridge.Folder)!, "mod-profiles");
    private sealed record ProfileChoice(string Path, string Label) { public override string ToString() => Label; }
    private Control BuildModsPage()
    {
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, BackColor = Color.Transparent };
        foreach (int height in new[] { 44, 46, 46, 42 }) body.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        modProfiles = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList }; RuneTheme.Field(modProfiles);
        top.Controls.Add(modProfiles, 0, 0);
        var locate = RuneTheme.Button("Locate Valheim"); locate.AutoSize = false; locate.Size = new Size(170, 36); locate.Click += (_, _) => { using var picker = new OpenFileDialog { Filter = "Valheim|valheim.exe", FileName = "valheim.exe" }; if (picker.ShowDialog(this) == DialogResult.OK) { preferences.GamePath = picker.FileName; SavePreferences(); DiscoverModProfiles(); } }; top.Controls.Add(locate, 1, 0); body.Controls.Add(top, 0, 0);
        var launch = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, WrapContents = false };
        AddButton(launch, "Start modded", () => { LaunchValheim(true); return Task.CompletedTask; }, true);
        AddButton(launch, "Start vanilla", () => { LaunchValheim(false); return Task.CompletedTask; });
        AddButton(launch, "New profile", CreateModProfile);
        AddButton(launch, "Import profile", ImportModProfile);
        AddButton(launch, "Profile folder", () => { if (modProfiles.SelectedItem is ProfileChoice p) Process.Start(new ProcessStartInfo(p.Path) { UseShellExecute = true }); return Task.CompletedTask; });
        body.Controls.Add(launch, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, WrapContents = false };
        AddButton(actions, "Refresh catalog", RefreshCatalog);
        AddButton(actions, "Update selected", UpdateSelectedMod);
        AddButton(actions, "Remove selected", RemoveSelectedMod);
        AddButton(actions, "Edit configs", () => { EditModConfigs(); return Task.CompletedTask; });
        AddButton(actions, "Restore backup", () => ModOperation(async _ => { if (modProfiles.SelectedItem is not ProfileChoice p) return "Select a profile first."; await Task.Run(() => OwnedMods.RestoreLast(p.Path)); return "Previous profile restored. The replaced version is also backed up."; }));
        AddButton(actions, "Cancel", () => { modCancellation?.Cancel(); return Task.CompletedTask; });
        body.Controls.Add(actions, 0, 2);
        void FitButtons(FlowLayoutPanel row) {
            var buttons = row.Controls.OfType<Button>().ToArray(); if (buttons.Length == 0) return;
            int width = Math.Max(72, (row.ClientSize.Width - 6) / buttons.Length - 8);
            foreach (var button in buttons) { button.AutoSize = false; button.MinimumSize = Size.Empty; button.Size = new Size(Math.Min(148, width), 36); button.Margin = new Padding(0, 0, 8, 0); }
        }
        launch.Resize += (_, _) => FitButtons(launch); actions.Resize += (_, _) => FitButtons(actions);
        var filter = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        modSearch = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Search mods by name or author…" }; RuneTheme.Field(modSearch); filter.Controls.Add(modSearch, 0, 0);
        modFilter = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList }; modFilter.Items.AddRange(new object[] { "Installed", "Enabled", "Disabled", "Browse mods", "Updates" }); modFilter.SelectedIndex = 0; RuneTheme.Field(modFilter); filter.Controls.Add(modFilter, 1, 0); body.Controls.Add(filter, 0, 3);
        modGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = RuneTheme.Coal, BorderStyle = BorderStyle.None, GridColor = RuneTheme.Iron, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EnableHeadersVisualStyles = false };
        modGrid.DefaultCellStyle.BackColor = RuneTheme.Coal; modGrid.DefaultCellStyle.ForeColor = RuneTheme.Bone; modGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(65, 61, 39); modGrid.DefaultCellStyle.SelectionForeColor = RuneTheme.AmberSoft; modGrid.RowTemplate.Height = 42;
        modGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(38, 35, 28); modGrid.ColumnHeadersDefaultCellStyle.ForeColor = RuneTheme.AmberSoft; modGrid.ColumnHeadersHeight = 36; modGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = RuneTheme.Panel;
        modGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mod", FillWeight = 65 }); modGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Version", FillWeight = 15 }); modGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", FillWeight = 15 }); modGrid.Columns.Add(new DataGridViewButtonColumn { Name = "Change", FillWeight = 20, FlatStyle = FlatStyle.Flat });
        body.Controls.Add(modGrid, 0, 4);
        modDetails = new Label { Dock = DockStyle.Fill, ForeColor = RuneTheme.Muted, BackColor = Color.Transparent, Padding = new Padding(0, 8, 0, 0), AutoEllipsis = true }; body.Controls.Add(modDetails, 0, 5);
        modNotice = new Label { Dock = DockStyle.Fill, ForeColor = RuneTheme.AmberSoft, BackColor = Color.Transparent }; body.Controls.Add(modNotice, 0, 6);
        modProfiles.SelectedIndexChanged += (_, _) => { if (modProfiles.SelectedItem is ProfileChoice p) { preferences.ModProfilePath = p.Path; SavePreferences(); RefreshMods(); } };
        modSearch.TextChanged += (_, _) => FilterMods(); modFilter.SelectedIndexChanged += async (_, _) => { FilterMods(); if (modFilter.SelectedIndex >= 3 && catalogMods.Count == 0) await RefreshCatalog(); };
        modGrid.SelectionChanged += (_, _) => {
            if (modGrid.SelectedRows.Count == 0) return;
            if (modGrid.SelectedRows[0].Tag is InstalledMod m) modDetails.Text = m.Id + "\n" + (m.Dependencies.Length == 0 ? "No listed dependencies." : "Requires: " + string.Join(", ", m.Dependencies));
            if (modGrid.SelectedRows[0].Tag is CatalogMod c) modDetails.Text = c.Id + " · " + c.Description + "\n" + (c.Deprecated ? "Deprecated · " : "") + "Requires: " + string.Join(", ", c.Dependencies);
        };
        modGrid.CellContentClick += async (_, e) => {
            if (modBusy || e.RowIndex < 0 || e.ColumnIndex != 3 || modProfiles.SelectedItem is not ProfileChoice p) return;
            if (modGrid.Rows[e.RowIndex].Tag is CatalogMod c) { await InstallCatalogMod(c); return; }
            if (modGrid.Rows[e.RowIndex].Tag is not InstalledMod m) return;
            await ModOperation(async _ => { await Task.Run(() => ModProfiles.Toggle(p.Path, m.Id)); return m.Name + (m.Enabled ? " disabled." : " enabled."); });
        };
        catalogMods = ModCatalog.Cached(ModsRoot);
        DiscoverModProfiles();
        return PageFrame("Mods", "Your mods, managed by Rune. Each profile keeps its own mods and settings.", body);
    }
    private void DiscoverModProfiles()
    {
        string previous = preferences.ModProfilePath;
        modProfiles.Items.Clear();
        foreach (string path in OwnedMods.Profiles(ModsRoot)) modProfiles.Items.Add(new ProfileChoice(path, System.IO.Path.GetFileName(path)));
        var selected = modProfiles.Items.Cast<ProfileChoice>().FirstOrDefault(p => string.Equals(p.Path, previous, StringComparison.OrdinalIgnoreCase));
        modProfiles.SelectedItem = selected ?? modProfiles.Items.Cast<ProfileChoice>().FirstOrDefault();
        if (modProfiles.Items.Count == 0) modNotice.Text = "Import your existing profile, or create a new one to get started.";
    }
    private void RefreshMods()
    {
        if (modGrid == null || modProfiles.SelectedItem is not ProfileChoice profile) return;
        try { installedMods = ModProfiles.Read(profile.Path); FilterMods(); modNotice.Text = installedMods.Count + " installed · " + profile.Path; }
        catch (Exception e) { modNotice.Text = "Could not read this profile: " + e.Message; }
    }
    private void FilterMods()
    {
        if (modGrid == null) return;
        modGrid.Rows.Clear();
        if (modFilter.SelectedIndex == 3) {
            foreach (var mod in catalogMods.Where(m => (m.Name + " " + m.Id + " " + m.Description).Contains(modSearch.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(m => m.Deprecated).ThenBy(m => m.Name).Take(500)) {
                var installed = installedMods.FirstOrDefault(m => m.Id == mod.Id);
                int row = modGrid.Rows.Add(mod.Name, mod.Version, mod.Deprecated ? "Deprecated" : installed == null ? "Available" : "Installed", installed?.Version == mod.Version ? "Installed" : installed == null ? "Install" : "Update"); modGrid.Rows[row].Tag = mod;
            }
            return;
        }
        foreach (var mod in installedMods.Where(m => (m.Name + " " + m.Id).Contains(modSearch.Text, StringComparison.OrdinalIgnoreCase) && (modFilter.SelectedIndex == 0 || (modFilter.SelectedIndex == 4 ? catalogMods.Any(c => c.Id == m.Id && Version.TryParse(m.Version, out var v) && Version.Parse(c.Version) > v) : m.Enabled == (modFilter.SelectedIndex == 1)))).OrderBy(m => m.Name)) {
            int row = modGrid.Rows.Add(mod.Name, mod.Version, mod.Enabled ? "Enabled" : "Disabled", mod.Folders.Length == 0 ? "Core" : mod.Enabled ? "Disable" : "Enable"); modGrid.Rows[row].Tag = mod;
        }
    }
    private void LaunchValheim(bool modded)
    {
        try {
            if (modBusy) { modNotice.Text = "Wait until the profile change finishes."; return; }
            if (ModProfiles.GameRunning) { modNotice.Text = "Valheim is already running."; return; }
            if (modProfiles.SelectedItem is not ProfileChoice profile) throw new InvalidOperationException("Select a mod profile first.");
            var liveMods = ModProfiles.Read(profile.Path);
            if (modded) foreach (var mod in liveMods.Where(m => m.Enabled)) {
                string issue = ModProfiles.DependencyError(liveMods, mod, true); if (issue.Length > 0) throw new InvalidOperationException(mod.Name + ": " + issue);
            }
            var info = ModProfiles.LaunchInfo(preferences.GamePath, profile.Path, modded);
            if (modded) OwnedMods.PrepareLaunch(preferences.GamePath, profile.Path);
            Process.Start(info);
            modNotice.Text = "Starting " + (modded ? profile.Label : "vanilla Valheim") + "…";
        } catch (Exception e) { modNotice.Text = e.Message; }
    }
}

