using System.Collections.Generic;
using UnityEngine;

namespace Haugbui.Brain
{
    /// <summary>
    /// A single egg with EggGrow is the one the game incubates. A stack does not.
    /// Food stays when a tamed animal that eats it is inside that animal's search range.
    /// </summary>
    internal static class Livestock
    {
        private static int _frame = -1;
        private static readonly List<Feeder> Feeders = new List<Feeder>();

        private struct Feeder
        {
            public Vector3 Position;
            public float RangeSq;
            public string[] Names;
        }

        internal static bool HatchingEgg(ItemDrop drop)
        {
            if (drop == null || drop.GetComponent<EggGrow>() == null || drop.m_itemData == null) return false;
            if (drop.m_itemData.m_stack <= 1) return true;
            ZNetView view = drop.m_nview;
            return view != null && view.IsValid() && view.GetZDO().GetFloat(ZDOVars.s_growStart) > 0f;
        }

        internal static bool FeedInReach(ItemDrop drop)
        {
            string name = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                ? drop.m_itemData.m_shared.m_name
                : null;
            if (string.IsNullOrEmpty(name)) return false;
            Refresh();
            Vector3 at = drop.transform.position;
            for (int i = 0; i < Feeders.Count; i++)
            {
                Feeder feeder = Feeders[i];
                if ((feeder.Position - at).sqrMagnitude > feeder.RangeSq) continue;
                for (int n = 0; n < feeder.Names.Length; n++)
                    if (feeder.Names[n] == name) return true;
            }
            return false;
        }

        private static void Refresh()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            Feeders.Clear();
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || character.IsDead() || !character.IsTamed()) continue;
                MonsterAI ai = character.GetComponent<MonsterAI>();
                if (ai == null || ai.m_consumeItems == null || ai.m_consumeSearchRange <= 0f) continue;
                var names = new List<string>();
                foreach (ItemDrop food in ai.m_consumeItems)
                {
                    string foodName = food != null && food.m_itemData != null && food.m_itemData.m_shared != null
                        ? food.m_itemData.m_shared.m_name
                        : null;
                    if (!string.IsNullOrEmpty(foodName)) names.Add(foodName);
                }
                if (names.Count == 0) continue;
                Feeders.Add(new Feeder
                {
                    Position = character.transform.position,
                    RangeSq = ai.m_consumeSearchRange * ai.m_consumeSearchRange,
                    Names = names.ToArray(),
                });
            }
        }
    }
}
