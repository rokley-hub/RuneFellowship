using System.Text.Json;
using Rune.Shared;

namespace Rune.Voice;
internal static class SessionFixChecks
{
    internal static void Run(string output)
    {
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        try {
            foreach (var pair in new[] { ("switch to club", "equip_weapon"), ("equip club", "equip_weapon"), ("block", "block"), ("parry", "parry"), ("shoot arrows", "shoot"), ("focus on greydwarf", "focus_enemy") })
                Check(Rules.Parse(pair.Item1)?.action == pair.Item2, "Combat intent: " + pair.Item1);
            foreach (string text in new[] { "don't equip club", "can you explain parrying", "I like shooting arrows", "do not shoot" })
                Check(Rules.Parse(text) == null, "No action from negation or conversation: " + text);
            byte[] pcm = BitConverter.GetBytes((short)16000).Concat(BitConverter.GetBytes((short)-16000)).ToArray();
            OutputAudio.ScalePcm(pcm, 16, 50);
            Check(BitConverter.ToInt16(pcm, 0) == 8000 && BitConverter.ToInt16(pcm, 2) == -8000, "Voice PCM half volume preserves polarity");
            OutputAudio.ScalePcm(pcm, 16, 0); Check(pcm.All(b => b == 0), "Zero voice volume silences PCM");
            var profiles = new[] { new CompanionProfile(), new CompanionProfile { Id = "eira", Name = "Eira", RecognitionName = "Eira" } };
            foreach (var text in new[] { "Hey, Rune, gather wood", "I Rune, gather wood", "gather wood Rune", "gather wood, Rune" }) {
                var address = CompanionAddress.Parse(text, profiles);
                Check(address.Companion == "rune" && Rules.Parse(address.Text)?.action == "gather_wood", "Address: " + text);
            }
            Check(CompanionAddress.Parse("Hey, Rune.", profiles).NameOnly, "Name-only speech identified without an AI call");
            Check(CompanionAddress.Parse("follow me Eira", profiles).Companion == "eira", "Trailing address identifies another recipient");
            foreach (string text in new[] { "gather Blueprint Rune", "Get good", "get me 20 soon", "runes are beautiful" })
                Check(CompanionAddress.Parse(text, profiles).Text == text, "No invented correction: " + text);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "trace-fixture-" + Guid.NewGuid().ToString("N"));
            using var trace = new TaskTrace(root, () => now);
            var companion = new CompanionState { id = "rune", diagnosticsVersion = 1, commandId = "wood", outcome = "ongoing", workPhase = "gathering" };
            var state = new GameState { ready = true, world = "fixture", roster = new[] { companion } };
            void Start(string id) { companion.commandId = id; companion.outcome = "ongoing"; trace.Acknowledge(new Command { id = id, action = "gather_wood", companionId = "rune", world = "fixture" }, new Reply { accepted = true }, state, 100); trace.Observe(state); }
            Start("wood");
            trace.Observe(new GameState()); now = now.AddSeconds(2); trace.Observe(state, false); now = now.AddSeconds(2); trace.Observe(state);
            Check(!trace.Recent().Contains("task-unobserved"), "Brief offline and unreadable states preserve work");
            companion.outcome = "completed"; trace.Observe(state);
            Check(trace.Summary().Contains("task-completed: 1"), "Completion retained after a state gap");
            Start("old-work"); companion.commandId = "rejected-stone"; companion.outcome = "rejected"; trace.Observe(state);
            Check(trace.Summary().Contains("task-interrupted: 1") && trace.Summary().Contains("task-blocked: 0"), "Rejected new order is not blamed on old work");
            Start("missing"); trace.Observe(state, false); now = now.AddSeconds(31); trace.Observe(state, false);
            Check(trace.Summary().Contains("task-unobserved: 1"), "Prolonged unreadable state has a bounded observation timeout");
            Start("stale"); state.timestamp = now.ToUnixTimeSeconds() - 60; trace.Observe(state); now = now.AddSeconds(2); state.timestamp = 0; companion.outcome = "completed"; trace.Observe(state);
            Check(trace.Summary().Contains("task-completed: 2"), "Stale snapshot does not terminate a task");
            Start("world-change"); trace.Observe(new GameState { ready = true, world = "other" });
            Check(trace.Summary().Contains("task-unobserved: 2"), "Confirmed world change stops observing the previous world");
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, checks }, Brain.Json));
        } catch (Exception error) { File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() }, Brain.Json)); Environment.ExitCode = 1; }
    }
}
