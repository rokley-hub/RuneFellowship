using System.Net.Http.Json;
using System.Text.Json;
using Rune.Shared;

namespace Rune.Voice;

public sealed partial class RuneWindow
{
    private string PlanFile => Path.Combine(bridge.Folder, "plans", bridge.CompanionId + "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(bridge.State().world))).Substring(0, 16) + ".json");
    private void OpenPlan()
    {
        try { var plan = JsonSerializer.Deserialize<PlanDocument>(File.ReadAllText(PlanFile), Brain.Json); if (plan != null && plan.steps.Length > 0) { ShowPlan(plan.steps, plan.objective); return; } } catch { }
        input.Text = "Make a task list to prepare my base for our next adventure."; input.Focus(); ShowPage("conversation"); listeningLabel.Text = "Describe your goal and send it. Review the resulting plan before starting.";
    }
    private void ShowPlan(PlanStep[] steps, string objective = "")
    {
        string error = Rules.ValidatePlan(steps); if (error.Length > 0) { AddLine("Plan", error); return; }
        if (!allowGameOrders.Checked || steps.Any(s => !ChatGptPlanner.Permitted(CurrentProfile, s.action))) { AddLine("Plan", "This plan includes work disabled by the companion's permissions."); return; }
        string planCompanion = bridge.CompanionId, planWorld = bridge.State().world;
        objective = Rules.NormalizeObjective(objective.Length > 0 ? objective : "Complete " + steps.Length + " step plan");
        Directory.CreateDirectory(Path.GetDirectoryName(PlanFile)!); File.WriteAllText(PlanFile, JsonSerializer.Serialize(new PlanDocument { objective = objective, steps = steps }, Brain.Json));
        listeningLabel.Text = "Plan ready · review the steps before starting.";
        using var dialog = new RunePopupForm { Text = DisplayName + " • Task list", ClientSize = new Size(680, 580), BackColor = RuneTheme.Stone, ForeColor = RuneTheme.Bone, StartPosition = FormStartPosition.CenterParent, Font = Font };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.Controls.Add(new Label { Text = objective, Font = new Font("Segoe UI Semibold", 20), Dock = DockStyle.Fill }, 0, 0);
        var member = bridge.State().roster.FirstOrDefault(c => c.id == bridge.CompanionId);
        layout.Controls.Add(new Label { Text = "The game executes these steps locally. A blocked step stops the plan; a new manual task replaces it.\n" + (member?.plan ?? "Choose Start plan when you are ready."), Dock = DockStyle.Fill }, 0, 1);
        var list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font("Segoe UI", 12), ItemHeight = 34 };
        foreach (var pair in steps.Select((step, index) => (step, index))) list.Items.Add($"{pair.index + 1:00}   {pair.step.action.Replace('_', ' ')}  {pair.step.item}  {(pair.step.action is "gather_wood" or "gather_stone" or "gather_item" ? "× " + pair.step.amount : "")}");
        layout.Controls.Add(list, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        AddButton(buttons, "Start plan", async () => {
            if (bridge.CompanionId != planCompanion || bridge.State().world != planWorld || !allowGameOrders.Checked || steps.Any(s => !ChatGptPlanner.Permitted(CurrentProfile, s.action))) { AddLine("Plan", "The companion, world or permissions changed. Reopen the plan before starting."); dialog.Close(); return; }
            CancelTurn(); var reply = await bridge.Send(new Command { action = "run_plan", objective = objective, steps = steps }, turn.Token); AddLine("Plan", reply.message); taskTrace.Record("plan-start", new { companion = planCompanion, objective, steps, reply.accepted, reply.message }); dialog.Close();
        });
        AddButton(buttons, "Keep for later", () => { dialog.Close(); return Task.CompletedTask; }); AddButton(buttons, "Stop current plan", async () => { await Submit("stop"); dialog.Close(); });
        layout.Controls.Add(buttons, 0, 3); dialog.Controls.Add(layout); dialog.ShowDialog(this);
    }
    private async Task ShowModelSettings()
    {
        using var dialog = new RunePopupForm { Text = "Local AI model", ClientSize = new Size(560, 310), StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, Font = Font };
        var choice = new ComboBox { Location = new Point(24, 62), Width = 505, DropDownStyle = ComboBoxStyle.DropDownList };
        choice.Items.AddRange(new object[] { "Qwen3.5 4B · recommended while gaming", "Qwen3.5 9B · stronger, uses more memory" }); choice.SelectedIndex = brain.LocalModel.EndsWith("9b") ? 1 : 0;
        var label = new Label { Location = new Point(24, 104), Size = new Size(505, 105), Text = "4B shares the GPU more comfortably with Valheim.\n9B is a separate 6.6 GB download and may run more slowly.\nBoth run locally without credits." };
        dialog.Controls.Add(new Label { Text = "Choose your local conversation model", Font = new Font("Segoe UI Semibold", 16), Location = new Point(24, 18), Size = new Size(510, 34) }); dialog.Controls.Add(choice); dialog.Controls.Add(label);
        var buttons = new FlowLayoutPanel { Location = new Point(24, 219), Size = new Size(510, 65) };
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) }; using var lifetime = new CancellationTokenSource();
        AddButton(buttons, "Use model", async () => {
            try {
                string model = choice.SelectedIndex == 0 ? "qwen3.5:4b" : "qwen3.5:9b";
                await LocalBrainService.Ensure(Path.GetDirectoryName(bridge.Folder)!,model,lifetime.Token);
                using var tags = await http.GetFromJsonAsync<JsonDocument>((LocalServices.Brain + "/api/tags"), lifetime.Token);
                if (tags == null || !tags.RootElement.GetProperty("models").EnumerateArray().Any(m => m.GetProperty("name").GetString() == model)) { label.Text = "This model is not downloaded. Use Download selected model first.\nYour current model remains selected."; return; }
                CancelTurn(); brain.LocalModel = model; SavePreferences(); dialog.Close();
            } catch (Exception e) { if (!dialog.IsDisposed) label.Text = "Model service unavailable: " + e.Message; }
        });
        AddButton(buttons, "Download selected model", async () => {
            choice.Enabled = false; buttons.Enabled = false;
            try {
                string model = choice.SelectedIndex == 0 ? "qwen3.5:4b" : "qwen3.5:9b";
                await LocalBrainService.Ensure(Path.GetDirectoryName(bridge.Folder)!,model,lifetime.Token);
                label.Text = "Downloading " + model + "… This can take several minutes.\nNo API key or credits are used. You can close this window to cancel.";
                using var result = await http.PostAsJsonAsync((LocalServices.Brain + "/api/pull"), new { model, stream = false }, lifetime.Token); result.EnsureSuccessStatusCode();
                if (!dialog.IsDisposed) label.Text = "Downloaded. Select Use model to switch.";
            } catch (OperationCanceledException) { } catch (Exception e) { if (!dialog.IsDisposed) label.Text = "Download failed: " + e.Message; }
            finally { if (!dialog.IsDisposed) { choice.Enabled = true; buttons.Enabled = true; } }
        });
        dialog.FormClosing += (_, _) => lifetime.Cancel(); dialog.Controls.Add(buttons); dialog.ShowDialog(this); await Task.CompletedTask;
    }
}
