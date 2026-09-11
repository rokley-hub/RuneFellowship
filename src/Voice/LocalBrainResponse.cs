using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Rune.Voice;

internal static class LocalBrainResponse
{
    internal const int OutputTokens = 768, MaximumContext = 16384;
    // This is deliberately a conservative estimate, not a tokenizer. Reserve room
    // for the reply and chat template; detect server-side truncation independently.
    internal static int Context(IReadOnlyList<Brain.Message> messages)
    {
        long bytes = messages.Sum(m => (long)Encoding.UTF8.GetByteCount(m.content) + 64);
        long required = (bytes + 2) / 3 + OutputTokens + 768;
        foreach (int size in new[] { 4096, 8192, MaximumContext }) if (required <= size) return size;
        return 0;
    }

    internal static List<Brain.Message> Fit(IReadOnlyList<Brain.Message> original)
    {
        var messages = original.ToList();
        // Drop oldest complete exchanges only. Never cut the current request,
        // personality, permissions, capabilities or live recipe facts mid-string.
        while (Context(messages) == 0 && messages.Count >= 4)
            messages.RemoveRange(1, 2);
        if (Context(messages) == 0)
            throw new InvalidOperationException("This request has too much context for the local model. Shorten the request or background notes and try again.");
        return messages;
    }

    internal static async Task<string> Complete(HttpClient http, string model, IReadOnlyList<Brain.Message> original, object schema, CancellationToken token)
    {
        var messages = Fit(original);
        int context = Context(messages);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var response = await http.PostAsJsonAsync("/api/chat", new {
                model, think = false, messages, format = schema, stream = false,
                keep_alive = "10m", options = new { temperature = .8, num_ctx = context, num_predict = OutputTokens }
            }, token);
            response.EnsureSuccessStatusCode();
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
            var root = body.RootElement;
            bool truncated = root.TryGetProperty("done_reason", out var reason) && reason.GetString() == "length";
            string content = root.GetProperty("message").GetProperty("content").GetString() ?? "";
            // Only retry a known truncated generation, before any decision is
            // parsed, remembered or executed. Do not repair partial command JSON.
            if (truncated)
            {
                if (attempt == 0 && context < MaximumContext) { context *= 2; continue; }
                throw new InvalidOperationException("The local model ran out of room before finishing its reply. No order was started. Try a shorter request.");
            }
            Parse(content);
            return content;
        }
        throw new InvalidOperationException("The local model could not finish its reply.");
    }

    internal static Brain.Thought Parse(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
            foreach (string name in new[] { "kind", "delivery", "reply", "objective", "action", "item" })
                if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) throw new JsonException();
            if (!root.TryGetProperty("amount", out var amount) || !amount.TryGetInt32(out _) ||
                !root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array ||
                steps.EnumerateArray().Any(s => s.ValueKind != JsonValueKind.Object)) throw new JsonException();
            return JsonSerializer.Deserialize<Brain.Thought>(content, Brain.Json) ?? throw new JsonException();
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("The local model returned an incomplete or invalid reply. No order was started. Please try again.", error);
        }
    }
}
