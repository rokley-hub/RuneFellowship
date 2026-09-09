namespace Rune.Voice;

internal static class LocalServices
{
    // An explicit alternate range lets release fixtures avoid a player's services.
    private static readonly int Port = int.TryParse(Environment.GetEnvironmentVariable("RUNE_SERVICE_BASE_PORT"), out int value) && value >= 1024 && value <= 65532 ? value : 11439;
    internal static string Brain => "http://127.0.0.1:" + Port;
    internal static string Audio => "http://127.0.0.1:" + (Port + 2);
    internal static string Expressive => "http://127.0.0.1:" + (Port + 3);
}
