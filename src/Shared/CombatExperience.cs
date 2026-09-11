using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rune.Shared
{
    // Companion progression, independent of player skills and biome names.
    public sealed class CombatExperience
    {
        private readonly Dictionary<int, double> experience = new Dictionary<int, double>();
        public static bool Supported(int skill) => skill >= 1 && skill <= 14 && skill != 13;
        public static double Required(int level) => Math.Pow(Math.Floor(level + 1d), 1.5) * .5 + .5;
        private static double LegacyRequired(int level) => 8 + 2 * level + level * level * .05;
        public static readonly double Maximum = Enumerable.Range(0, 100).Sum(Required);
        public double Xp(int skill) => experience.TryGetValue(skill, out var value) ? value : 0;
        public int Level(int skill)
        {
            double value = Xp(skill); int level = 0;
            if (value >= Maximum) return 100;
            double threshold = Required(0);
            while (level < 100 && value >= threshold) { level++; if (level < 100) threshold += Required(level); }
            return level;
        }
        public int Mastery => experience.Keys.Where(k => k != 6).Select(Level).DefaultIfEmpty(0).Max();
        public static float DamageFactor(int level, float roll)
        {
            float center = .4f + Math.Max(0, Math.Min(100, level)) * .006f;
            float minimum = Math.Max(0, center - .15f), maximum = Math.Min(1, center + .15f);
            return minimum + (maximum - minimum) * Math.Max(0, Math.Min(1, roll));
        }
        public void Add(int skill, double amount)
        {
            if (!Supported(skill) || double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0) return;
            // Native Skill.Raise: one level per event, discard overflow on level-up.
            int level = Level(skill);
            double next = Enumerable.Range(0, Math.Min(100, level + 1)).Sum(Required);
            experience[skill] = Math.Min(next, Xp(skill) + amount);
        }
        public string Serialize() => "2|" + string.Join(";", experience.OrderBy(p => p.Key).Select(p => p.Key + ":" + p.Value.ToString("R", CultureInfo.InvariantCulture)));
        public static CombatExperience Parse(string text)
        {
            var result = new CombatExperience();
            if (string.IsNullOrEmpty(text) || text.Length > 2048 || !(text.StartsWith("1|", StringComparison.Ordinal) || text.StartsWith("2|", StringComparison.Ordinal))) return result;
            foreach (var entry in text.Substring(2).Split(';').Take(14)) {
                var pair = entry.Split(':');
                if (pair.Length == 2 && int.TryParse(pair[0], out var skill) && Supported(skill) &&
                    double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var xp) && !double.IsNaN(xp) && !double.IsInfinity(xp) && xp >= 0 && !result.experience.ContainsKey(skill)) {
                    if (text[0] == '1') {
                        int level = 0; double total = 0;
                        while (level < 100 && xp >= LegacyRequired(level)) { xp -= LegacyRequired(level); total += Required(level++); }
                        xp = level == 100 ? Maximum : total + xp / LegacyRequired(level) * Required(level);
                    }
                    result.experience[skill] = Math.Min(Maximum, xp);
                }
            }
            return result;
        }
    }
}
