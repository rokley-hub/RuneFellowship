using System.Text.Json;
using Rune.Shared;
using Rune.Voice;

internal static class RecorderChecks
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("Recorder: " + name); checks++; }
        string root = Path.Combine(Path.GetTempPath(), "rune-recorder-check-" + Guid.NewGuid().ToString("N"));
        var now = DateTimeOffset.UtcNow;
        string path;
        using (var trace = new TaskTrace(root, () => now)) {
            path = trace.SessionFolder;
            string request = trace.BeginRequest("gather ten stone", "synthetic");
            using var scope = trace.Scope(request);
            var c = new CompanionState { id = "rune", diagnosticsVersion = 1, workPhase = "gather", outcome = "active", workProgress = "0/10" };
            var state = new GameState { ready = true, world = "test", roster = new[] { c } };
            Command Start(string action = "gather_stone", string companion = "rune") {
                var command = new Command { id = Guid.NewGuid().ToString(), companionId = companion, action = action, world = "test" };
                trace.Dispatch(command, state); trace.Acknowledge(command, new Reply { accepted = true, message = "Accepted" }, state, 140); c.commandId = command.id; return command;
            }
            var first = Start(); trace.Observe(state);
            Check(!trace.Summary().Contains("task-completed: 1"), "Acceptance is not completion");
            now = now.AddSeconds(46); trace.Observe(state);
            Check(trace.Summary().Contains("possible-stall"), "No-progress threshold detects a candidate");
            for (int i = 0; i < 5; i++) { now = now.AddSeconds(10); trace.Observe(state); }
            Check(trace.Summary().Contains("seen 1 times"), "Unchanged stalls are not spammed");
            c.x = 3; trace.Observe(state); Check(trace.Recent().Contains("task-progress-resumed"), "Movement resumes progress");
            c.outcome = "completed"; trace.Observe(state);
            Check(trace.Summary().Contains("task-completed: 1"), "Confirmed game completion counted");
            c.outcome = "active"; Start(); trace.Observe(state); c.safetyPaused = true;
            now = now.AddSeconds(60); trace.Observe(state); c.safetyPaused = false; now = now.AddSeconds(20); trace.Observe(state);
            Check(trace.Summary().Contains("seen 1 times"), "Safety pause does not trigger a stall");
            c.inCombat = true; now = now.AddSeconds(60); trace.Observe(state); c.inCombat = false; c.workPhase = "maintenance";
            now = now.AddSeconds(60); trace.Observe(state);
            Check(trace.Summary().Contains("seen 1 times"), "Combat and maintenance do not trigger stalls");
            state.simulationPaused = true; now = now.AddMinutes(5); trace.Observe(state); state.simulationPaused = false;
            Check(trace.Summary().Contains("seen 1 times"), "Paused simulation does not trigger stalls");
            Start("follow"); Check(trace.Summary().Contains("task-interrupted: 1"), "Follow supersedes work");
            Start("pickup_all"); now = now.AddMinutes(5); trace.Observe(state);
            Check(trace.Summary().Contains("Pending observed tasks: 0"), "Continuous pickup has no completion deadline");
            c.workPhase = "gather"; Start(); trace.Observe(state); state.ready = false; trace.Observe(state);
            Check(trace.Summary().Contains("task-unobserved: 0"), "Brief disconnect preserves task observation");
            now = now.AddSeconds(31); trace.Observe(state);
            Check(trace.Summary().Contains("task-unobserved: 1"), "Disconnect is unknown, not failed or completed");
            state.ready = true; c.diagnosticsVersion = 0; Start(); trace.Observe(state); now = now.AddMinutes(3); trace.Observe(state);
            Check(!trace.Summary().Contains("command-not-observed"), "Old mods do not generate false diagnostics");
            c.diagnosticsVersion = 1; var mismatch = Start(); c.commandId = "another-command"; now = now.AddSeconds(11); trace.Observe(state);
            Check(trace.Summary().Contains("command-not-observed"), "Acknowledged command missing from game is captured");
            Start(); c.outcome = "blocked"; c.task = "Blocked: need a workbench"; trace.Observe(state);
            Check(trace.Summary().Contains("blocked-task"), "Blocked reason retained separately");
            state.error = "Synthetic native exception"; trace.Observe(state); trace.Observe(state);
            Check(trace.Summary().Contains("mod-error") && trace.Summary().Contains("Synthetic native exception (seen 1 times"), "Native errors are captured once per unchanged error");
            trace.IncidentFor("voice-error", "rune", "speech", "Failed 400"); trace.IncidentFor("voice-error", "rune", "speech", "Failed 400");
            Check(trace.Summary().Contains("seen 2 times"), "Repeated errors grouped");
            Check(TaskTrace.ParseBookmark("report a bug: she ignored the stone", out var kind, out var note) && kind == "player-report" && note == "she ignored the stone", "Bug bookmark with description");
            Check(TaskTrace.ParseBookmark("note an improvement: use spare axes", out kind, out note) && kind == "improvement", "Improvement bookmark");
            Check(!TaskTrace.ParseBookmark("don't report a bug", out _, out _) && !TaskTrace.ParseBookmark("report a buggy thing", out _, out _), "Negation and unrelated prefixes are not bookmarks");
            trace.Bookmark("improvement", "Try spare tools", "eira");
            var timeout = new Command { id = Guid.NewGuid().ToString(), action = "gather_stone", companionId = "eira", world = "test" };
            trace.Acknowledge(timeout, new Reply { outcome = "unconfirmed", message = "No reply received" }, state, 4000);
            Check(trace.Summary().Contains("acknowledgment-timeout") && !trace.Summary().Contains("rejected-order"), "Missing acknowledgment is unknown, not a rejection");
            trace.Timing("synthetic-stage", 50); trace.Timing("synthetic-stage", 100);
            trace.SaveSummary(); Check(trace.Flush(5000), "Writer flushes");
            var rows = File.ReadLines(Path.Combine(path, "events.jsonl")).Select(s => JsonDocument.Parse(s)).ToArray();
            Check(rows.Any(r => r.RootElement.GetProperty("request").GetString() == request && r.RootElement.GetProperty("stage").GetString() == "command-result"), "Request correlation survives storage");
            Check(File.ReadAllText(Path.Combine(path, "summary.md")).Contains("p95 100"), "Latency summary persisted");
            Check(File.ReadAllText(Path.Combine(path, "incidents.json")).Contains("Try spare tools"), "Issue ledger saved");
            Check(!trace.Recent().Contains("password") && !Directory.Exists(Path.Combine(path, "audio")), "No credentials or audio added by recorder");
        }
        Check(File.ReadAllText(Path.Combine(path, "events.jsonl")).Contains("session-end"), "Clean shutdown recorded");
        string blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "not a directory");
        using (var trace = new TaskTrace(blocked)) {
            trace.Record("test", new { value = 1 }); trace.Flush(5000);
            Check(trace.Health.Contains("could not save"), "Storage failure stays visible and does not crash");
        }
        using (var trace = new TaskTrace(Path.Combine(root, "stress"))) {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20000; i++) trace.Record("synthetic-load", new { index = i, text = "bounded writer stress check" });
            watch.Stop();
            Check(watch.Elapsed.TotalSeconds < 5, "Full writer queue never blocks producers");
            Check(trace.Health.Contains("events dropped"), "Overload reports dropped events");
            Check(trace.Flush(10000), "Writer recovers after overload");
            Console.WriteLine($"20,000 synthetic enqueue attempts: {watch.Elapsed.TotalMilliseconds:F0} ms; {trace.Health}");
        }
        Console.WriteLine($"Passed {checks} recorder checks. Synthetic files: {root}");
    }
}
