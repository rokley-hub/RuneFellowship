namespace Rune.Voice;

public sealed partial class RuneWindow
{
    internal static void TestVisualState(string output)
    {
        var lines = new List<string>();
        string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "visual-state-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        ApplicationConfiguration.Initialize();
        using var window = new RuneWindow(folder);
        void Check(string name, bool value) { lines.Add((value ? "PASS " : "FAIL ") + name); }
        window.microphoneMode.SelectedIndex = 2;
        window.recognizing = true; window.busyGeneration = window.generation;
        Check("Always-on input does not hide thinking", window.ShellActivity == "Thinking");
        window.busyGeneration = -1; window.speakingReply = true;
        Check("Pending synthesis is preparing voice, not speaking", window.ShellActivity == "Preparing voice");
        window.microphoneMuted = true;
        Check("Muting input does not hide pending voice output", window.ShellActivity == "Preparing voice");
        window.speakingReply = false;
        Check("Muted input is visible after output completes", window.ShellActivity == "Muted");
        window.microphoneMuted = false; window.recognizing = false;
        Check("Always-on without capture reports offline", window.ShellActivity == "Mic offline");
        window.recognizing = true;
        Check("Active always-on capture reports listening", window.ShellActivity == "Listening");
        window.recognizing = false; window.microphoneMode.SelectedIndex = 0;
        Check("Disabled microphone is muted", window.ShellActivity == "Muted");
        string mailbox = "test" + "@" + "example.org";
        string input = "File " + "C:" + "\\Users\\" + "ExamplePerson\\Rune\\log.txt contact " + mailbox + " Bearer secret-value";
        var cleaned = FeedbackPrivacy.Redact(input);
        Check("Diagnostic export redacts home path, email and bearer token", !cleaned.Contains("ExamplePerson") && !cleaned.Contains(mailbox) && !cleaned.Contains("secret-value"));
        Check("Diagnostic export retains useful failure details", FeedbackPrivacy.Redact("gather_stone blocked: no reachable target") == "gather_stone blocked: no reachable target");
        File.WriteAllLines(output, lines);
        if (lines.Any(l => l.StartsWith("FAIL"))) Environment.ExitCode = 1;
    }
}

