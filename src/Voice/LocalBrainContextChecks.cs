using System.Net;
using System.Text;
using System.Text.Json;

namespace Rune.Voice;

internal static class LocalBrainContextChecks
{
    private const string Reply = "{\"kind\":\"chat\",\"delivery\":\"warm\",\"reply\":\"I hear you.\",\"objective\":\"\",\"action\":\"none\",\"amount\":20,\"item\":\"\",\"steps\":[]}";
    private sealed class Handler(Func<int, JsonElement, string> response) : HttpMessageHandler
    {
        internal int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            return new(HttpStatusCode.OK) { Content = new StringContent(response(++Calls, body.RootElement), Encoding.UTF8, "application/json") };
        }
    }
    private static string Envelope(string content, string reason) => JsonSerializer.Serialize(new { message = new { content }, done_reason = reason });
    internal static async Task Run(string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string description) { if (!condition) throw new Exception(description); passed.Add(description); }
        var simple = new[] { new Brain.Message("system", "Keep permissions and facts."), new Brain.Message("user", "Can you hear me?") };
        var full = new[] { new Brain.Message("system", new string('a', 18000)), simple[1] };
        Check(LocalBrainResponse.Context(simple) == 4096 && LocalBrainResponse.Context(full) == 8192, "Long instructions reserve output capacity; short requests retain small context.");
        var history = new[] { full[0], new Brain.Message("user", new string('h', 14000)), new Brain.Message("assistant", new string('h', 14000)), simple[1] };
        var fitted = LocalBrainResponse.Fit(history);
        Check(fitted.Count == 2 && fitted[0] == full[0] && fitted[^1] == simple[1], "Budget trimming removes complete old exchanges without cutting instructions or current intent.");
        bool oversized = false;
        try { LocalBrainResponse.Fit(new[] { new Brain.Message("system", new string('x', 50000)), simple[1] }); } catch (InvalidOperationException) { oversized = true; }
        Check(oversized, "Oversized required facts fail explicitly rather than silently truncating permissions.");
        foreach (string invalid in new[] { "{\"kind\":\"command\",", "{}", Reply.Replace("\"steps\":[]", "\"steps\":[null]"), Reply.Replace("\"reply\":\"I hear you.\"", "\"reply\":null") })
        {
            bool rejected = false; try { LocalBrainResponse.Parse(invalid); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Incomplete or null decision rejected without command repair.");
        }
        using var handler = new Handler((call, body) => {
            Check(body.GetProperty("options").GetProperty("num_ctx").GetInt32() == (call == 1 ? 4096 : 8192), "Truncated generation retries once with a larger context.");
            Check(body.GetProperty("options").GetProperty("num_predict").GetInt32() == 768, "Reply receives enough output tokens for a structured decision.");
            return call == 1 ? Envelope("{\"kind\":", "length") : Envelope(Reply, "stop");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var result = await LocalBrainResponse.Complete(http, "fixture", simple, new { }, CancellationToken.None);
        Check(handler.Calls == 2 && LocalBrainResponse.Parse(result).action == "none", "Only the completed retry is returned for interpretation.");
        using var broken = new Handler((_, _) => Envelope("{", "length"));
        using var brokenHttp = new HttpClient(broken) { BaseAddress = new Uri("http://localhost") };
        bool stopped = false;
        try { await LocalBrainResponse.Complete(brokenHttp, "fixture", simple, new { }, CancellationToken.None); } catch (InvalidOperationException) { stopped = true; }
        Check(stopped && broken.Calls == 2, "Repeated truncation stops after one retry with a readable failure.");
        File.WriteAllText(output, string.Join("\n", passed.Select(p => "PASS: " + p)));
    }
}
