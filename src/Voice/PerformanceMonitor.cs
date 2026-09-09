using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Rune.Voice;

// Sample off the UI thread. Measurements never enter model prompts or change gameplay.
internal sealed class PerformanceMonitor
{
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    private long previousIdle, previousTotal;
    internal string Summary { get; private set; } = "Waiting for the first hardware sample…";
    internal async Task Sample(TaskTrace trace, CancellationToken token)
    {
        double? cpu = null, gpu = null, used = null, total = null;
        if (GetSystemTimes(out long idle, out long kernel, out long user)) {
            long sum = kernel + user;
            if (previousTotal > 0 && sum > previousTotal) cpu = Math.Clamp(100.0 * (1 - (double)(idle - previousIdle) / (sum - previousTotal)), 0, 100);
            previousIdle = idle; previousTotal = sum;
        }
        string gpuStatus = "GPU measurement unavailable";
        try {
            using var process = new Process { StartInfo = new ProcessStartInfo("nvidia-smi", "--query-gpu=utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            process.Start();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(2000);
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var errors = process.StandardError.ReadToEndAsync(deadline.Token);
            try {
                await process.WaitForExitAsync(deadline.Token);
                var parts = (await output).Split('\n')[0].Split(','); await errors;
                if (process.ExitCode == 0 && parts.Length == 3 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out double g) && double.TryParse(parts[1], CultureInfo.InvariantCulture, out double u) && double.TryParse(parts[2], CultureInfo.InvariantCulture, out double t)) {
                    gpu = g; used = u; total = t;
                    gpuStatus = $"GPU {g:0}% · Video memory {u / 1024:0.0} / {t / 1024:0.0} GB";
                }
            } finally { if (!process.HasExited) process.Kill(); }
        } catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or OperationCanceledException) { }
        Summary = (cpu.HasValue ? $"CPU {cpu:0}%" : "CPU measuring…") + " · " + gpuStatus;
        if (gpu >= 95) Summary += "\nGPU is busy. Compare a lower Valheim FPS cap; Rune does not change game settings.";
        if (total > 0 && used / total > .9) Summary += "\nVideo memory is nearly full. Lightweight voice can free the speech model's GPU memory.";
        trace.Record("hardware-sample", new { cpuPercent = cpu, gpuPercent = gpu, videoMemoryMiB = used, videoMemoryTotalMiB = total });
    }
}
