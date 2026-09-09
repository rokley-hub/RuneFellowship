using System.Text.Json;
using Rune.Shared;
namespace Rune.Voice;
internal static class LocalBrainChecks
{
    internal static async Task Run(string installation,string output)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var results=new List<object>();
        var before=await LocalBrainService.Inspect("qwen3.5:4b",timeout.Token);
        var statuses=await Task.WhenAll(LocalBrainService.Ensure(installation,"qwen3.5:4b",timeout.Token),LocalBrainService.Ensure(installation,"qwen3.5:4b",timeout.Token));
        results.Add(new {test="Concurrent startup",before,statuses});
        if(statuses.Any(s=>!s.Ready))throw new Exception("Local Qwen did not start: "+statuses[0].Message);
        foreach(var profile in CompanionProfile.Defaults().Take(2)) {
            var brain=new Brain {DisplayName=profile.Name,CompanionId=profile.Id,Personality=profile.Personality,LocalModel="qwen3.5:4b",MemoryFolder=Path.Combine(Path.GetDirectoryName(output)!,"memories")};
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var thought=await brain.Chat("Follow me, please.",new GameState{world="isolated-model-fixture",ready=true,companion=true,health=150,playerHealth=100},timeout.Token);
            results.Add(new {test="Local intent",profile.Id,seconds=watch.Elapsed.TotalSeconds,thought});
            if(thought.action!="follow")throw new Exception("Unexpected local follow interpretation for "+profile.Id+": "+thought.action);
        }
        results.Add(new {test="9B availability (no download)",status=await LocalBrainService.Inspect("qwen3.5:9b",timeout.Token)});
        File.WriteAllText(output,JsonSerializer.Serialize(results,Brain.Json));
    }
}
