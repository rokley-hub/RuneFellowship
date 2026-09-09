#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rune.Shared;

namespace Rune.Voice;

// Observation only: never retries, cancels, or changes a game command.
internal sealed class TaskTrace : IDisposable
{
    internal const string Version = Rune.Shared.Release.Gameplay;
    private readonly string folder;
    private readonly Func<DateTimeOffset> clock;
    private readonly BlockingCollection<Action> writes = new(512);
    private readonly Task writer;
    private readonly object gate = new();
    private readonly Queue<object> recent = new();
    private readonly Dictionary<string, string> last = new();
    private readonly Dictionary<string, Tracked> active = new();
    private readonly Dictionary<string, Incident> incidents = new();
    private readonly Dictionary<string, int> counts = new();
    private readonly Dictionary<string, List<double>> timings = new();
    private readonly AsyncLocal<string?> context = new();
    private readonly JsonSerializerOptions json = new() { IncludeFields = true };
    private DateTimeOffset savedAt;
    private bool disposed;
    private int lost, errors;
    private string lastModError = "";
    internal string RequestId => context.Value ?? "";
    internal string SessionId { get; } = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
    internal string SessionFolder => Path.Combine(folder, "sessions", SessionId);
    internal string Health => errors > 0 ? "Recorder could not save some events" : lost > 0 ? $"Recorder busy: {lost} events dropped" : "Session recording locally";
    private sealed class Tracked
    {
        public string request = "", command = "", action = "", world = "", companion = "", signature = "";
        public DateTimeOffset started, progress, expectedBy;
        public DateTimeOffset? missingSince;
        public float x, y, z;
        public bool positioned, stalled;
    }
    internal sealed class Incident
    {
        public string id = "", kind = "", companion = "", action = "", message = "", status = "Needs review", request = "", command = "";
        public string evidence = "";
        public int occurrences;
        public DateTimeOffset first, latest;
    }
    public TaskTrace(string folder, Func<DateTimeOffset>? clock = null)
    {
        this.folder = folder; this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        writer = Task.Run(() => { foreach (var job in writes.GetConsumingEnumerable()) { try { job(); } catch { Interlocked.Increment(ref errors); } } });
        Enqueue(() => {
            Directory.CreateDirectory(SessionFolder);
            // Only our GUID-named session directories, never user worlds, accounts or memories.
            foreach (var old in new DirectoryInfo(Path.Combine(folder, "sessions")).GetDirectories()
                .Where(d => Regex.IsMatch(d.Name, @"^\d{8}-\d{6}-[0-9a-f]{8}$"))
                .OrderByDescending(d => d.Name).Skip(20)) {
                if ((old.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string name in new[] { "events.jsonl", "events.previous.jsonl", "summary.md", "incidents.json", "session.json" }) {
                    string file = Path.Combine(old.FullName, name); if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) File.Delete(file);
                }
                if (!old.EnumerateFileSystemInfos().Any()) old.Delete();
            }
            File.WriteAllText(Path.Combine(SessionFolder, "session.json"), JsonSerializer.Serialize(new { SessionId, version = Version, started = this.clock(), runtime = Environment.Version.ToString(), status = "Opened; session-end event indicates a clean close" }, json));
        });
        Record("recorder-start", new { version = Version });
    }
    private void Enqueue(Action action) { try { if (!writes.TryAdd(action)) Interlocked.Increment(ref lost); } catch (InvalidOperationException) { Interlocked.Increment(ref lost); } }
    internal string BeginRequest(string text, string source)
    {
        string id = Guid.NewGuid().ToString("N"); Record("request-received", new { text, source }, id); return id;
    }
    internal IDisposable Scope(string id) { string? old = context.Value; context.Value = id; return new ScopeEnd(() => context.Value = old); }
    private sealed class ScopeEnd(Action end) : IDisposable { public void Dispose() => end(); }
    public void Record(string stage, object detail, string? request = null)
    {
        lock (gate) {
            if (disposed) return;
            var entry = new { at = clock(), session = SessionId, request = request ?? RequestId, stage, detail };
            recent.Enqueue(entry); while (recent.Count > 160) recent.Dequeue();
            counts[stage] = counts.GetValueOrDefault(stage) + 1;
            Enqueue(() => {
                Directory.CreateDirectory(SessionFolder);
                string path = Path.Combine(SessionFolder, "events.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length >= 4 * 1024 * 1024) File.Move(path, Path.Combine(SessionFolder, "events.previous.jsonl"), true);
                File.AppendAllText(path, JsonSerializer.Serialize(entry, json) + Environment.NewLine);
            });
        }
    }
    internal void Timing(string stage, double milliseconds, string? request = null)
    {
        lock (gate) { if (!timings.TryGetValue(stage, out var values)) timings[stage] = values = new(); values.Add(milliseconds); if (values.Count > 512) values.RemoveAt(0); }
        Record("timing", new { stage, milliseconds }, request);
    }
    internal string LatestTimings()
    {
        lock (gate) {
            var stages = new[] { ("speech-recognition", "Recognition"), ("speech-queue-wait", "Queue"), ("command-model", "ChatGPT"), ("local-model", "Qwen"), ("local-dialogue", "Local chat"), ("game-acknowledgment", "Game acceptance"), ("voice-first-playback", "Speech start") };
            return string.Join(" · ", stages.Where(s => timings.TryGetValue(s.Item1, out var v) && v.Count > 0).Select(s => $"{s.Item2} {timings[s.Item1][^1] / 1000:0.00}s"));
        }
    }
    internal void Dispatch(Command command, GameState before)
    {
        Record("command-dispatched", new { command.id, command.companionId, command.action, command.amount, command.item, command.world, command.steps });
    }
    internal void Acknowledge(Command command, Reply reply, GameState before, double milliseconds)
    {
        Timing("game-acknowledgment", milliseconds);
        Record("command-result", new { command.id, command.companionId, command.action, reply.accepted, reply.message });
        if (!reply.accepted) {
            IncidentFor(reply.outcome == "unconfirmed" ? "acknowledgment-timeout" : "rejected-order", command.companionId, command.action, reply.message, RequestId, command.id);
            if (reply.outcome != "unconfirmed") return;
        }
        if (command.action is "status" or "exclude_item" or "include_item" or "set_base" or "lend_tools" or "equip_gear" or "equip_weapon" or "focus_enemy" or "combat_auto") return;
        string key = command.world + ":" + command.companionId;
        if (active.Remove(key, out var prior)) Record("task-interrupted", new { prior.command, prior.action, reason = "Replaced by " + command.action }, prior.request);
        // Follow, defend and pickup-all are intentionally continuous; they have no completion deadline.
        if (!(Rules.PlanActions.Contains(command.action) || command.action == "run_plan")) return;
        var c = before.roster.FirstOrDefault(c => c.id == command.companionId);
        active[key] = new Tracked { request = RequestId, command = command.id, companion = command.companionId, world = command.world, action = command.action, started = clock(), progress = clock(), expectedBy = clock().AddSeconds(10), x = c?.x ?? 0, y = c?.y ?? 0, z = c?.z ?? 0, positioned = c?.diagnosticsVersion > 0 };
    }
    internal void IncidentFor(string kind, string companion, string action, string message, string? request = null, string command = "")
    {
        string fingerprint = kind + "|" + companion + "|" + action + "|" + Regex.Replace(message.ToLowerInvariant(), @"\d+(?:\.\d+)?", "#");
        lock (gate) {
            if (!incidents.TryGetValue(fingerprint, out var issue)) {
                if (incidents.Count >= 200) { Interlocked.Increment(ref lost); return; }
                incidents[fingerprint] = issue = new Incident { id = "I" + (incidents.Count + 1).ToString("D3"), kind = kind, companion = companion, action = action, message = message, request = request ?? RequestId, command = command, first = clock(), evidence = Recent() };
            }
            issue.latest = clock(); issue.occurrences++;
            Record("incident", new { issue.id, kind, companion, action, message, issue.occurrences, command }, request);
        }
        SaveSummary();
    }
    internal void Bookmark(string kind, string note, string companion)
    {
        IncidentFor(kind, companion, "manual-note", string.IsNullOrWhiteSpace(note) ? "Player bookmarked recent events; description needed." : note);
    }
    internal static bool ParseBookmark(string text, out string kind, out string note)
    {
        var match = Regex.Match(text.Trim(), @"^(report a bug|note an improvement)(?:$|(?:\s*[:,.!?]\s*|\s+)(.*)$)", RegexOptions.IgnoreCase);
        kind = match.Success && match.Groups[1].Value.StartsWith("note", StringComparison.OrdinalIgnoreCase) ? "improvement" : "player-report";
        note = match.Success ? match.Groups[2].Value.Trim() : ""; return match.Success;
    }
    public void Observe(GameState state, bool readSucceeded = true)
    {
        if (!readSucceeded || (state.timestamp > 0 && clock().ToUnixTimeSeconds() - state.timestamp > 5)) {
            foreach (var pair in active.ToArray()) ObserveGap(pair.Value, pair.Key, "State unreadable or stale for 30 seconds; outcome unknown");
            return;
        }
        if (!string.IsNullOrEmpty(state.error) && lastModError != state.world + ":" + state.error) {
            lastModError = state.world + ":" + state.error;
            IncidentFor("mod-error", "", "game-executor", state.error);
        }
        foreach (var pair in active.ToArray()) {
            var t = pair.Value;
            if (state.ready && state.world != t.world) { Record("task-unobserved", new { t.command, reason = "Confirmed world change; outcome unknown" }, t.request); active.Remove(pair.Key); continue; }
            if (!state.ready) { ObserveGap(t, pair.Key, "Game unavailable for 30 seconds; outcome unknown"); continue; }
            if (state.simulationPaused) { t.progress = clock(); t.expectedBy = clock().AddSeconds(10); continue; }
            var c = state.roster.FirstOrDefault(c => c.id == t.companion);
            if (c == null) { ObserveGap(t, pair.Key, "Companion unavailable for 30 seconds; outcome unknown"); continue; }
            t.missingSince = null;
            if (c.diagnosticsVersion == 0) continue; // Older mods cannot supply reliable progress evidence.
            if (c.commandId != t.command) {
                if (c.outcome == "rejected") { Record("task-interrupted", new { t.command, reason = "Stopped by a newer order that was rejected", rejectedCommand = c.commandId }, t.request); active.Remove(pair.Key); continue; }
                if (clock() > t.expectedBy) { IncidentFor("command-not-observed", t.companion, t.action, "Accepted command did not appear in the loaded companion state.", t.request, t.command); active.Remove(pair.Key); }
                continue;
            }
            string signature = c.workProgress + "|" + c.cargo + "|" + c.tools + "|" + c.plan;
            if (c.outcome is "completed" or "blocked" or "ended-unverified") {
                Record("task-" + c.outcome, new { t.command, t.action, c.task, c.workPhase, elapsedMs = (clock() - t.started).TotalMilliseconds }, t.request);
                if (c.outcome != "completed") IncidentFor(c.outcome == "blocked" ? "blocked-task" : "unverified-outcome", t.companion, t.action, c.task, t.request, t.command);
                active.Remove(pair.Key); continue;
            }
            bool moved = t.positioned && Math.Pow(c.x - t.x, 2) + Math.Pow(c.y - t.y, 2) + Math.Pow(c.z - t.z, 2) >= 2.25;
            if (signature != t.signature || moved || c.safetyPaused || c.inCombat || c.workPhase == "maintenance") {
                t.signature = signature; t.progress = clock(); t.x = c.x; t.y = c.y; t.z = c.z; t.positioned = true;
                if (t.stalled) { Record("task-progress-resumed", new { t.command }, t.request); t.stalled = false; }
            }
            if (!t.stalled && (clock() - t.progress).TotalSeconds >= 45) {
                t.stalled = true;
                IncidentFor("possible-stall", t.companion, t.action, "No observed movement, inventory or work progress for 45 seconds. Needs investigation; not proof of a bug.", t.request, t.command);
            }
        }
        foreach (var c in state.roster) {
            string signature = c.commandId + "|" + c.task + "|" + c.plan + "|" + c.cargo + "|" + c.tools + "|" + c.workProgress + "|" + c.safetyPaused + "|" + c.inCombat;
            string key = state.world + ":" + c.id;
            if (last.GetValueOrDefault(key) != signature) { last[key] = signature; Record("game-progress", new { state.world, companion = c.id, c.commandId, c.task, c.plan, c.cargo, c.tools, c.outcome, c.workPhase, c.workProgress, c.x, c.y, c.z, c.health, c.safetyPaused, c.inCombat }, active.GetValueOrDefault(key)?.request ?? ""); }
        }
        if (last.Count > 64) last.Clear();
        if ((clock() - savedAt).TotalSeconds >= 10) SaveSummary();
    }
    private void ObserveGap(Tracked task, string key, string reason)
    {
        if (task.missingSince == null) { task.missingSince = clock(); Record("task-observation-paused", new { task.command }, task.request); }
        task.progress = clock(); task.expectedBy = clock().AddSeconds(10);
        if ((clock() - task.missingSince.Value).TotalSeconds >= 30) { Record("task-unobserved", new { task.command, reason }, task.request); active.Remove(key); }
    }
    public string Recent() { lock (gate) { var lines = new List<string>(); int size = 0; foreach (var entry in recent.Reverse()) { string line = JsonSerializer.Serialize(entry, json); if (size + line.Length > 24000) break; lines.Add(line); size += line.Length; } lines.Reverse(); return string.Join(Environment.NewLine, lines); } }
    internal string Summary()
    {
        lock (gate) {
            var b = new StringBuilder($"# Rune play session {SessionId}\n\nApp/mod release: {Version}. Local recording; no audio files or credentials collected.\n\n{Health}. Dropped events: {lost}; write failures: {errors}.\n\n");
            foreach (string stage in new[] { "request-received", "command-dispatched", "task-completed", "task-blocked", "task-ended-unverified", "task-interrupted", "task-unobserved", "request-cancelled", "request-failed" }) b.AppendLine($"- {stage}: {counts.GetValueOrDefault(stage)}");
            b.AppendLine($"\nPending observed tasks: {active.Count}. Acceptance alone never counts as completion.\n\n## Timings (last 512 samples per stage)\n");
            foreach (var pair in timings) { var sorted = pair.Value.Order().ToArray(); b.AppendLine($"- {pair.Key}: median {sorted[sorted.Length / 2]:F0} ms; p95 {sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * .95) - 1)]:F0} ms ({sorted.Length} samples)"); }
            b.AppendLine("\n## Issues and improvements\n\nCandidates require review. Missing resources or unsupported features are not automatically bugs.\n");
            foreach (var i in incidents.Values) b.AppendLine($"- {i.id} [{i.status}] {i.kind}, {i.companion}, {i.action} — {i.message} (seen {i.occurrences} times; command {i.command})");
            return b.ToString();
        }
    }
    internal void SaveSummary()
    {
        lock (gate) {
            savedAt = clock(); string summary = Summary();
            var issues = incidents.Values.Select(i => new Incident { id = i.id, kind = i.kind, companion = i.companion, action = i.action, message = i.message, status = i.status, request = i.request, command = i.command, first = i.first, latest = i.latest, occurrences = i.occurrences, evidence = i.evidence }).ToArray();
            Enqueue(() => {
                Directory.CreateDirectory(SessionFolder);
                void Save(string name, string content) { string file = Path.Combine(SessionFolder, name); File.WriteAllText(file + ".tmp", content); File.Move(file + ".tmp", file, true); }
                Save("summary.md", summary); Save("incidents.json", JsonSerializer.Serialize(issues, json));
            });
        }
    }
    internal bool Flush(int milliseconds = 1500) { var done = new TaskCompletionSource(); var watch = Stopwatch.StartNew(); try { if (!writes.TryAdd(() => done.TrySetResult(), milliseconds)) return false; } catch (InvalidOperationException) { return false; } return done.Task.Wait(Math.Max(0, milliseconds - (int)watch.ElapsedMilliseconds)); }
    public void Dispose()
    {
        if (disposed) return;
        foreach (var t in active.Values) Record("task-unobserved", new { t.command, reason = "App closed; outcome unknown" }, t.request);
        active.Clear(); Record("session-end", new { clean = true }); SaveSummary(); disposed = true; writes.CompleteAdding(); writer.Wait(1500);
    }
}
