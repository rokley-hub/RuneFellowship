using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Rune.Shared
{
    public sealed class CrossbowLoad
    {
        public float Progress { get; private set; }
        public bool Loaded { get; private set; }
        public static float Duration(float seconds, int skill) => Math.Max(.2f, seconds * (1 - Math.Max(0, Math.Min(100, skill)) * .005f));
        public bool Advance(float dt, float duration, bool safe, bool resourcesPaid)
        {
            if (Loaded) return true;
            if (!safe || !resourcesPaid) { Progress = 0; return false; }
            if (!float.IsNaN(dt) && !float.IsInfinity(dt) && dt > 0) Progress += Math.Min(dt, .25f) / Math.Max(.2f, duration);
            if (Progress >= 1) { Progress = 1; Loaded = true; }
            return Loaded;
        }
        public void Interrupt() { if (!Loaded) Progress = 0; }
        public void Reset() { Progress = 0; Loaded = false; }
    }
    // Companion food/eitr ledger; consumed inventory items are never recreated from this data.
    public sealed class MagicResources
    {
        private sealed class Food { public string name; public float value, health, stamina, regen, duration, remaining; public bool legacy; }
        private readonly List<Food> foods = new List<Food>();
        public float Current { get; private set; }
        public float Maximum => foods.Sum(f => f.value * (float)Math.Pow(Math.Max(0, f.remaining / f.duration), .3));
        private static float Decay(Food food) => (float)Math.Pow(Math.Max(0, food.remaining / food.duration), .3);
        public float Health => foods.Sum(f => f.health * Decay(f));
        public float Stamina => foods.Sum(f => f.stamina * Decay(f));
        public float Healing => foods.Sum(f => f.regen);
        public int Count => foods.Count;
        public string[] LegacyNames => foods.Where(f => f.legacy).Select(f => f.name).ToArray();
        public void RestoreFoodValues(string name, float health, float stamina, float regen)
        {
            var food = foods.FirstOrDefault(f => f.name == name && f.legacy);
            if (food == null || !ValidValue(health) || !ValidValue(stamina) || !ValidValue(regen)) return;
            food.health = health; food.stamina = stamina; food.regen = regen; food.legacy = false;
        }
        public bool CanEat(string name) => !string.IsNullOrEmpty(name) && name.Length <= 120 && (foods.Any(f => f.name == name) ? foods.Any(f => f.name == name && f.remaining < f.duration * .5f) : foods.Count < 3);
        public bool Eat(string name, float value, float duration) => Eat(name, 0, 0, value, 0, duration);
        private static bool ValidValue(float value) => Finite(value) && value >= 0 && value <= 1000;
        public bool Eat(string name, float health, float stamina, float eitr, float regen, float duration)
        {
            if (!CanEat(name) || !ValidValue(health) || !ValidValue(stamina) || !ValidValue(eitr) || !ValidValue(regen) ||
                health + stamina + eitr <= 0 || !Finite(duration) || duration <= 0 || duration > 86400) return false;
            foods.RemoveAll(f => f.name == name);
            foods.Add(new Food { name = name, value = eitr, health = health, stamina = stamina, regen = regen, duration = duration, remaining = duration }); return true;
        }
        private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        public bool Have(float amount) => Finite(amount) && amount >= 0 && (amount == 0 || Current > amount);
        public void Spend(float amount) { if (Finite(amount) && amount > 0) Current = Math.Max(0, Current - amount); }
        public void Add(float amount) { if (Finite(amount) && amount > 0) Current = Math.Min(Maximum, Current + amount); }
        public void Tick(float dt, float regeneration, float foodRate = 1)
        {
            if (!Finite(dt) || dt <= 0) return;
            foreach (var f in foods) f.remaining -= dt * (Finite(foodRate) ? Math.Max(0, foodRate) : 1);
            foods.RemoveAll(f => f.remaining <= 0);
            Current = Math.Min(Current, Maximum);
            if (Finite(regeneration) && regeneration > 0) Add(regeneration * dt);
        }
        public string Serialize() => "2|" + Current.ToString("R", CultureInfo.InvariantCulture) + "|" + string.Join(";", foods.Select(f =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(f.name)) + "," + string.Join(",", new[] { f.value, f.duration, f.remaining, f.health, f.stamina, f.regen, f.legacy ? 1f : 0f }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)))));
        public static MagicResources Parse(string text)
        {
            var result = new MagicResources(); if (string.IsNullOrEmpty(text) || text.Length > 4096) return result;
            var parts = text.Split('|'); if (parts.Length != 3 || (parts[0] != "1" && parts[0] != "2")) return result;
            bool Number(string s, out float n) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && Finite(n);
            foreach (string row in parts[2].Split(';').Take(3)) {
                var fields = row.Split(','); if (fields.Length != (parts[0] == "1" ? 4 : 8)) continue;
                try {
                    string name = Encoding.UTF8.GetString(Convert.FromBase64String(fields[0]));
                    float hp = 0, stamina = 0, regen = 0, legacy = 1;
                    if (parts[0] == "2" && !(Number(fields[4], out hp) && Number(fields[5], out stamina) && Number(fields[6], out regen) && Number(fields[7], out legacy))) continue;
                    if (result.foods.Any(f => f.name == name)) continue;
                    if (Number(fields[1], out var value) && Number(fields[2], out var duration) && Number(fields[3], out var remaining) && remaining > 0 && remaining <= duration && result.Eat(name, hp, stamina, value, regen, duration)) {
                        result.foods.Last().remaining = remaining; result.foods.Last().legacy = legacy == 1;
                    }
                } catch (FormatException) { }
            }
            if (Number(parts[1], out var current)) result.Current = Math.Max(0, Math.Min(current, result.Maximum));
            return result;
        }
    }
}
