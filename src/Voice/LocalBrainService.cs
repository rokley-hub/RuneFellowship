using System.Diagnostics;
using System.Text.Json;
namespace Rune.Voice;

internal static class LocalBrainService
{
    private static readonly SemaphoreSlim Gate = new(1,1);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    internal sealed record Status(bool Ready, string Message);
    internal static async Task<Status> Inspect(string model, CancellationToken token)
    {
        try {
            using var response=await Http.GetAsync(LocalServices.Brain+"/api/tags",token);
            response.EnsureSuccessStatusCode();
            using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            bool installed=doc.RootElement.GetProperty("models").EnumerateArray().Any(m=>m.GetProperty("name").GetString()==model);
            return new(installed,installed ? model+" ready" : "Download "+model+" in Qwen model");
        } catch (OperationCanceledException) when(token.IsCancellationRequested) { throw; }
        catch { return new(false,"Local service is stopped"); }
    }
    internal static async Task<Status> Ensure(string root, string model, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try {
            var status=await Inspect(model,token);
            if(status.Message!="Local service is stopped") return status;
            string runtime=Path.Combine(Path.GetFullPath(root),"runtime");
            string executable=Path.Combine(runtime,"ollama","ollama.exe");
            if(!File.Exists(executable)) return new(false,"Local brain runtime missing; install the optional local brain pack");
            string helper=Path.Combine(AppContext.BaseDirectory,"Assets","StartLocalBrain.ps1");
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true,RedirectStandardOutput=true};
            // Some launch hosts supply both Path and PATH. Windows PowerShell's
            // Start-Process rejects those duplicate case-insensitive keys.
            var environment=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
            foreach(var pair in start.Environment)environment[pair.Key]=pair.Value;
            start.Environment.Clear();
            foreach(var pair in environment)start.Environment[pair.Key]=pair.Value;
            foreach(var argument in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",helper,"-Runtime",runtime,"-Port",new Uri(LocalServices.Brain).Port.ToString()}) start.ArgumentList.Add(argument);
            using var process=Process.Start(start) ?? throw new IOException("Qwen service could not start.");
            var helperError=process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            for(int i=0;i<30;i++) {
                await Task.Delay(300,token);
                status=await Inspect(model,token);
                if(status.Message!="Local service is stopped")return status;
                if(process.HasExited && process.ExitCode!=0) {
                    File.WriteAllText(Path.Combine(runtime,"ollama-startup-errors.log"),await helperError);
                    return new(false,"Qwen service could not start; check runtime/ollama-startup-errors.log");
                }
            }
            return new(false,"Qwen is still starting; checking again shortly");
        } catch(OperationCanceledException) {throw;}
        catch(Exception ex) {return new(false,"Cannot start Qwen: "+ex.Message);}
        finally {Gate.Release();}
    }
}
