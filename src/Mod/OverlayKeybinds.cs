using System;
using System.Linq;
using UnityEngine;
namespace Rune.Mod
{
    public partial class Plugin
    {
        private static bool OverlayKeyDown(string shortcut)
        {
            if (string.IsNullOrEmpty(shortcut) || shortcut == "Off") return false;
            var parts = shortcut.Split('+').Select(p => p.Trim()).ToArray();
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool modifiersMatch = ctrl == parts.Contains("Ctrl") && alt == parts.Contains("Alt") && shift == parts.Contains("Shift");
            string final = parts.Last();
            if (final == "Ctrl") return modifiersMatch && (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl));
            if (final == "Alt") return modifiersMatch && (Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt));
            if (final == "Shift") return modifiersMatch && (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift));
            return modifiersMatch && TryOverlayKey(final, out KeyCode key) && Input.GetKeyDown(key);
        }

        private static bool TryOverlayKey(string capturedName, out KeyCode key)
        {
            string name = capturedName;
            if (capturedName.Length == 2 && capturedName[0] == 'D' && char.IsDigit(capturedName[1])) name = "Alpha" + capturedName[1];
            else if (capturedName.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase)) name = "Keypad" + capturedName.Substring(6);
            else name = capturedName switch {
                "Enter" => "Return", "Back" => "Backspace", "Capital" => "CapsLock",
                "Prior" => "PageUp", "Next" => "PageDown", "Snapshot" => "Print", "Scroll" => "ScrollLock",
                "Left" => "LeftArrow", "Right" => "RightArrow", "Up" => "UpArrow", "Down" => "DownArrow",
                "Add" => "KeypadPlus", "Subtract" => "KeypadMinus", "Multiply" => "KeypadMultiply", "Divide" => "KeypadDivide", "Decimal" => "KeypadPeriod",
                "OemSemicolon" => "Semicolon", "Oemplus" => "Equals", "Oemcomma" => "Comma", "OemMinus" => "Minus",
                "OemPeriod" => "Period", "OemQuestion" => "Slash", "Oemtilde" => "BackQuote", "OemOpenBrackets" => "LeftBracket",
                "OemPipe" => "Backslash", "OemCloseBrackets" => "RightBracket", "OemQuotes" => "Quote",
                _ => name
            };
            return Enum.TryParse(name, true, out key);
        }
    }
}
