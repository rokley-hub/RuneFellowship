namespace Rune.Voice;
public sealed partial class RuneWindow
{
    private async Task ModOperation(Func<CancellationToken, Task<string>> action)
    {
        if (modBusy) return;
        modBusy = true; modProfiles.Enabled = false; modCancellation = new CancellationTokenSource();
        try { modNotice.Text = "Working…"; string result = await action(modCancellation.Token); RefreshMods(); modNotice.Text = result; }
        catch (OperationCanceledException) { modNotice.Text = "Cancelled. The active profile was kept intact."; }
        catch (Exception e) { modNotice.Text = e.Message; }
        finally { modBusy = false; modProfiles.Enabled = true; modCancellation.Dispose(); modCancellation = null; }
    }
    private Task RefreshCatalog() => ModOperation(async token => {
        catalogMods = await Task.Run(() => ModCatalog.Refresh(ModsRoot, new Progress<string>(s => { if (!IsDisposed) BeginInvoke(() => modNotice.Text = s); }), token));
        FilterMods(); return catalogMods.Count + " mods available. Choose Browse mods, search, then Install.";
    });
    private string? AskProfileName(string title, string suggestion)
    {
        using var dialog = new RunePopupForm { Text = title, ClientSize = new Size(440, 125), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = RuneTheme.Coal, ForeColor = RuneTheme.Bone };
        var name = new TextBox { Text = suggestion, Location = new Point(18, 20), Width = 402 }; RuneTheme.Field(name); dialog.Controls.Add(name);
        var ok = RuneTheme.Button("Continue"); ok.Location = new Point(292, 68); ok.Size = new Size(128, 36); ok.DialogResult = DialogResult.OK; dialog.Controls.Add(ok); dialog.AcceptButton = ok;
        return dialog.ShowDialog(this) == DialogResult.OK ? name.Text.Trim() : null;
    }
    private async Task CreateModProfile()
    {
        if (modBusy) return; string? name = AskProfileName("New Rune mod profile", "New fellowship"); if (name == null) return;
        await ModOperation(async _ => { string path = await Task.Run(() => { var p = OwnedMods.Create(ModsRoot, name); AttachRune(p); return p; }); preferences.ModProfilePath = path; DiscoverModProfiles(); return "Profile created with Rune. Install BepInExPack_Valheim and Jotunn from Browse mods."; });
    }
    private async Task ImportModProfile()
    {
        if (modBusy) return;
        using var picker = new FolderBrowserDialog { Description = "Choose a profile containing BepInEx. Rune copies it; the original stays unchanged.", UseDescriptionForTitle = true, InitialDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "r2modmanPlus-local", "Valheim", "profiles") };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        string? name = AskProfileName("Name the independent copy", System.IO.Path.GetFileName(picker.SelectedPath)); if (name == null) return;
        await ModOperation(async _ => { string path = await Task.Run(() => { var p = OwnedMods.Import(ModsRoot, name, picker.SelectedPath); AttachRune(p); return p; }); preferences.ModProfilePath = path; DiscoverModProfiles(); return "Imported an independent copy with Rune. The original is unchanged."; });
    }
    private void AttachRune(string profile) => OwnedMods.AddRune(profile, bridge.Folder, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(bridge.Folder)!, "payload", "BepInEx", "plugins", "RuneCompanion", "RuneCompanion.dll"));
    private async Task InstallCatalogMod(CatalogMod mod)
    {
        if (modBusy || modProfiles.SelectedItem is not ProfileChoice profile) return;
        var current = installedMods.FirstOrDefault(m => m.Id == mod.Id);
        if (current?.Version == mod.Version) { modNotice.Text = "That version is already installed."; return; }
        await ModOperation(async token => { await Task.Run(() => OwnedMods.Install(profile.Path, new() { mod }, catalogMods, new Progress<string>(s => { if (!IsDisposed) BeginInvoke(() => modNotice.Text = s); }), token)); return mod.Name + " " + mod.Version + " installed with its dependencies. Applies on next launch."; });
    }
    private async Task UpdateSelectedMod()
    {
        if (modGrid.SelectedRows.Count == 0 || modBusy) return;
        string? id = modGrid.SelectedRows[0].Tag switch { InstalledMod m => m.Id, CatalogMod c => c.Id, _ => null };
        if (catalogMods.Count == 0) await RefreshCatalog();
        var latest = catalogMods.FirstOrDefault(c => c.Id == id);
        if (latest == null) { modNotice.Text = "No online package found. Local Rune updates come with the app."; return; }
        await InstallCatalogMod(latest);
    }
    private async Task RemoveSelectedMod()
    {
        if (modBusy || modProfiles.SelectedItem is not ProfileChoice profile || modGrid.SelectedRows.Count == 0 || modGrid.SelectedRows[0].Tag is not InstalledMod mod) return;
        // Removal is reversible through the retained profile backup; configuration is deliberately retained.
        await ModOperation(async _ => { await Task.Run(() => OwnedMods.Remove(profile.Path, mod.Id)); return mod.Name + " removed. Its settings and a profile backup were kept."; });
    }
    private void EditModConfigs(string? profilePath = null, string? modId = null)
    {
        if (modBusy) return;
        ProfileChoice? profile = profilePath == null ? modProfiles.SelectedItem as ProfileChoice : new ProfileChoice(profilePath, System.IO.Path.GetFileName(profilePath));
        if (profile == null) return;
        string directory = System.IO.Path.Combine(profile.Path, "BepInEx", "config"); Directory.CreateDirectory(directory);
        var selected = modId == null ? null : ModProfiles.Read(profile.Path).FirstOrDefault(m => m.Id.Equals(modId, StringComparison.OrdinalIgnoreCase));
        string modName = selected?.Name ?? "profile";
        using var dialog = new RunePopupForm { Text = "Settings · " + profile.Label + " · " + modName, ClientSize = new Size(900, 620), MinimumSize = new Size(650, 450), StartPosition = FormStartPosition.CenterParent, BackColor = RuneTheme.Coal, ForeColor = RuneTheme.Bone };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 3 }; layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); dialog.Controls.Add(layout);
        var files = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList }; RuneTheme.Field(files);
        IEnumerable<string> configFiles = modId == null
            ? ModProfiles.SafeFiles(directory).Where(f => new[] { ".cfg", ".json", ".yml", ".yaml", ".ini" }.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).Select(f => System.IO.Path.GetRelativePath(profile.Path, f))
            : OwnedMods.ConfigFilesFor(profile.Path, modId);
        foreach (string file in configFiles) files.Items.Add(file);
        layout.Controls.Add(files, 0, 0);
        var text = new TextBox { Multiline = true, AcceptsTab = true, AcceptsReturn = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 10) }; RuneTheme.Field(text); layout.Controls.Add(text, 0, 1);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; var save = RuneTheme.Button("Save settings"); var notice = new Label { Width = 630, Height = 38, Text = "Changes apply after restarting Valheim. Existing settings are backed up.", ForeColor = RuneTheme.Muted }; bottom.Controls.Add(save); bottom.Controls.Add(notice); layout.Controls.Add(bottom, 0, 2);
        files.SelectedIndexChanged += (_, _) => { try { string path = OwnedMods.SafePath(profile.Path, files.SelectedItem!.ToString()!); if (new FileInfo(path).Length > 2_000_000) throw new IOException("This file is too large for the settings editor."); text.Text = File.ReadAllText(path); } catch (Exception e) { notice.Text = e.Message; text.Clear(); } };
        save.Click += async (_, _) => { if (files.SelectedItem == null) return; save.Enabled = false; try { string relative = files.SelectedItem.ToString()!, value = text.Text; await Task.Run(() => OwnedMods.SaveConfig(profile.Path, relative, value)); notice.Text = "Saved. Restart Valheim to apply."; } catch (Exception e) { notice.Text = e.Message; } finally { save.Enabled = true; } };
        if (files.Items.Count > 0) files.SelectedIndex = 0; else { save.Enabled = false; notice.Text = modId == null ? "No settings yet. Launch the modded game once to generate them." : "No settings file could be matched to " + modName + ". Launch the profile once if this mod creates settings at runtime."; }
        dialog.ShowDialog(this);
    }
}
