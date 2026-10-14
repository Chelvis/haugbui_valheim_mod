#if DEBUG
using System.Collections;
using System.Linq;
using Haugbui.Brain;
using Haugbui.Core.Creature;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>HB-T-110: same-owner tomb spacing and territories where one owner's tomb areas overlap.</summary>
    internal sealed class TerritorySuite
    {
        private const float Apart = 20f;

        private readonly SelfTestContext _t;
        private Vector3 _arena;

        public TerritorySuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Territory";
            Player player = _t.Player;
            _arena = SelfTestContext.FindDryArena(player.transform.position - player.transform.forward * 12f, 9f);
            GameObject bed = _t.SpawnVanilla("bed", Ground(new Vector3(Apart / 2f, 0, -6)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            bed.GetComponent<Bed>().SetOwner(player.GetPlayerID(), player.GetPlayerName());

            HaugbuiStructure first = _t.PlaceStructure(StructureKind.Tomb, Ground(Vector3.zero));
            if (!_t.Check("first tomb placed", first != null)) yield break;
            yield return SelfTestContext.WaitFrames(2);
            PlacementResult refused = PlacementCheck.Check(StructureKind.Tomb, Ground(new Vector3(Apart, 0, 0)), player);
            _t.Check($"own tomb {Apart:0} m away refused (TombSpacingSameOwner {PlacementCheck.SameOwnerSpacing:0})",
                refused == PlacementResult.OwnTombTooClose, refused.ToString());

            float spacing = PlacementCheck.SameOwnerSpacing;
            PlacementCheck.SameOwnerSpacing = Apart / 2f;
            HaugbuiStructure second;
            try
            {
                second = _t.PlaceStructure(StructureKind.Tomb, Ground(new Vector3(Apart, 0, 0)));
            }
            finally
            {
                PlacementCheck.SameOwnerSpacing = spacing;
            }
            if (!_t.Check("second tomb placed with the spacing lowered for the test", second != null)) yield break;

            Container nearFirst = Chest(new Vector3(3, 0, 2));
            Container nearSecond = Chest(new Vector3(Apart - 3, 0, 2));
            HaugbuiStructure collection = _t.PlaceStructure(StructureKind.CollectionChest, Ground(new Vector3(Apart - 3, 0, -2)));
            yield return SelfTestContext.WaitFrames(3);
            TombController a = first.GetComponent<TombController>(), b = second.GetComponent<TombController>();
            if (!_t.Check("territory chests placed", nearFirst != null && nearSecond != null && collection != null && a != null && b != null)) yield break;

            string idFirst = ContainerEndpoint.IdOf(nearFirst), idSecond = ContainerEndpoint.IdOf(nearSecond);
            string idCollection = ContainerEndpoint.IdOf(collection.GetComponent<Container>());
            WorkContext ca = Context(a), cb = Context(b);
            _t.Check("both chests are inside both radii", Vector3.Distance(a.transform.position, nearSecond.transform.position) < a.Attributes.Radius
                                                          && Vector3.Distance(b.transform.position, nearFirst.transform.position) < b.Attributes.Radius);
            _t.Check("deposits go only to chests of its own territory",
                Has(ca.Destinations, idFirst) && !Has(ca.Destinations, idSecond) && Has(cb.Destinations, idSecond) && !Has(cb.Destinations, idFirst),
                $"A: {Ids(ca.Destinations)} · B: {Ids(cb.Destinations)}");
            _t.Check("storage chests stay shared as sources",
                Has(ca.Sources, idFirst) && Has(ca.Sources, idSecond) && Has(cb.Sources, idFirst) && Has(cb.Sources, idSecond),
                $"A: {Ids(ca.Sources)} · B: {Ids(cb.Sources)}");
            _t.Check("a Collection Chest belongs to the nearest tomb only",
                !ca.CollectionChests.Any(c => c.Id == idCollection) && cb.CollectionChests.Any(c => c.Id == idCollection));
            _t.Check("a crop or ground item position is split the same way",
                ca.Mine(nearFirst.transform.position) && !ca.Mine(nearSecond.transform.position) && cb.Mine(nearSecond.transform.position));

            WorkContext.TerritorySuspendedForTests = true;
            try
            {
                WorkContext shared = Context(a);
                _t.Check("TerritoryMode off: overlapping areas are shared again", Has(shared.Destinations, idFirst) && Has(shared.Destinations, idSecond));
            }
            finally
            {
                WorkContext.TerritorySuspendedForTests = false;
            }
        }

        private static WorkContext Context(TombController tomb)
        {
            var context = new WorkContext();
            context.Refresh(tomb, SinglePlayerAccessPolicy.Instance, probe: true);
            return context;
        }

        private static bool Has(System.Collections.Generic.IEnumerable<Core.Storage.ContainerSnapshot> list, string id) => list.Any(c => c.Id == id);

        private static string Ids(System.Collections.Generic.IEnumerable<Core.Storage.ContainerSnapshot> list) => string.Join(",", list.Select(c => c.Id));

        private Container Chest(Vector3 offset)
        {
            GameObject go = _t.PlaceVanilla("piece_chest_wood", Ground(offset), Quaternion.identity);
            Container container = go != null ? go.GetComponentInChildren<Container>() : null;
            if (container == null) return null;
            var stone = new ItemStack { Prefab = "Stone", Amount = 1, WorldLevel = Game.m_worldLevel };
            ItemBridge.RefreshShared(stone);
            ItemBridge.AddTo(container.GetInventory(), stone);
            return container;
        }

        private Vector3 Ground(Vector3 offset) => SelfTestContext.OnGround(_arena + offset);
    }
}
#endif
