using System.Collections.Generic;
using System.Linq;
using Rune.Shared;

namespace Rune.Mod
{
    public partial class Companion
    {
        // Read-only snapshot, refreshed twice per second while the overview is open.
        internal string[] OverviewLines()
        {
            var lines=new List<string> { DisplayName, "Now · "+TaskLabel };
            if(ObjectiveOngoing) lines.Add("Goal · "+Objective);
            if(NextPlanLabel.Length>0) lines.Add("Next · "+NextPlanLabel);
            lines.Add("Health · "+Body.GetHealth().ToString("F0")+" / "+Body.GetMaxHealth().ToString("F0"));
            lines.Add("Combat stamina · "+CombatStamina.ToString("F0")+" / "+MaxCombatStamina.ToString("F0"));
            if(direwolfMount) lines.Add("Mount stamina · "+direwolfMount.Stamina.ToString("F0")+" / "+direwolfMount.MaxStamina.ToString("F0"));
            if(Rules.IsWolf(Appearance)) {
                var tame=GetComponent<Tameable>();
                lines.Add("Food · "+(tame && tame.IsHungry() ? "Hungry" : "Fed"));
            } else {
                lines.Add("Food · "+magic.Count+" / 3 active meals"+(magic.Count==0 ? " — carry food to recover and gain food bonuses" : ""));
                if(magic.Maximum>0) lines.Add("Eitr · "+magic.Current.ToString("F0")+" / "+magic.Maximum.ToString("F0"));
            }
            lines.Add(EquippedWeaponStatus);
            var items=Body.GetInventory().GetAllItems();
            lines.Add("Carrying · "+items.Count+" / "+(Body.GetInventory().GetWidth()*Body.GetInventory().GetHeight())+" slots");
            lines.AddRange(items.GroupBy(i=>Localization.instance.Localize(i.m_shared.m_name)).OrderBy(g=>g.Key).Select(g=>g.Key+" × "+g.Sum(i=>i.m_stack)));
            if(items.Count==0) lines.Add("Nothing carried");
            if(HasBorrowedEquipment) lines.Add("Borrowed equipment · return it when finished");
            var quest=QuestSnapshot();
            if(quest!=null && quest.status=="Active") { lines.Add("Crafting · "+quest.item);if(quest.note.Length>0) lines.Add(quest.note); }
            return lines.ToArray();
        }
    }
}
