using System.Text.RegularExpressions;
namespace Rune.Voice;

internal static class FeedbackPrivacy
{
    internal static string Redact(string text)
    {
        text = Regex.Replace(text, @"(?i)[A-Z]:[\\/]Users[\\/][^\\/\r\n""<>]+", "[user folder]");
        text = Regex.Replace(text, @"[\w.+%-]+@[\w.-]+\.[A-Za-z]{2,}", "[email]");
        text = Regex.Replace(text, @"\b(?:sk-[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9]{20,})\b", "[credential]");
        text = Regex.Replace(text, @"(?i)(authorization\s*[:=]\s*|bearer\s+)[^\s""<>]+", "$1[credential]");
        return text;
    }
}
