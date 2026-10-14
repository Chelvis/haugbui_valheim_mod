#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Haugbui.Core.Recipes;
using Haugbui.Integration;
using Haugbui.Spikes;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>M1: plugin health checks.</summary>
    internal sealed class BasicsSuite
    {
        private readonly SelfTestContext _t;

        public BasicsSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Basics";
            _t.Check("game API self-check", GameApiSelfCheck.Report.Passed,
                string.Join(", ", GameApiSelfCheck.Report.Missing.Select(m => m.ToString())));
            _t.Check("all recipes valid", StartupValidation.Results.Count == DefaultRecipes.All.Count
                                          && StartupValidation.Results.Values.All(r => r.Status == RecipeStatus.Valid),
                string.Join("; ", StartupValidation.Results.Where(r => r.Value.Status != RecipeStatus.Valid).Select(r => r.Key)));

            string file = Path.Combine(Path.GetDirectoryName(HaugbuiPlugin.Instance.Info.Location), "Translations", "English", "haugbui.json");
            if (!File.Exists(file))
            {
                _t.Fail("translations deployed", file);
                yield break;
            }
            List<string> missing = Regex.Matches(File.ReadAllText(file), "\"(haugbui_[a-z0-9_]+)\"\\s*:")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Where(key => Localization.instance.Localize("$" + key) == "[" + key + "]")
                .ToList();
            _t.Check($"all translation keys resolve in {Localization.instance.GetSelectedLanguage()}", missing.Count == 0, string.Join(", ", missing));
        }
    }

    /// <summary>HB-T-008 automated: the creature crosses a wall back and forth, never gets stuck, can be hit.</summary>
    internal sealed class FlightSuite
    {
        private static readonly string[] WallPrefabs = { "woodwall", "stone_wall_2x1", "wood_wall_roof" };
        private const float Seconds = 25f;
        private const int WallStack = 4;

        private readonly SelfTestContext _t;

        public FlightSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Flight";
            Player player = _t.Player;
            Vector3 forward = player.transform.forward;
            Vector3 wallPosition = SelfTestContext.OnGround(player.transform.position - forward * 12f);

            // A stack, so the path crosses the wall whatever the piece's pivot height (a single one sat half underground).
            string wallPrefab = WallPrefabs.FirstOrDefault(name => ZNetScene.instance.GetPrefab(name) != null);
            var walls = new List<GameObject>();
            if (wallPrefab == null) _t.Skip("wall to fly through", "no known wall prefab found");
            else
            {
                for (int i = 0; i < WallStack; i++)
                {
                    GameObject wall = _t.SpawnVanilla(wallPrefab, wallPosition + Vector3.up * (2f * i), Quaternion.LookRotation(forward));
                    WearNTear wear = wall != null ? wall.GetComponent<WearNTear>() : null;
                    if (wear != null) wear.m_noSupportWear = false;
                    if (wall != null) walls.Add(wall);
                }
            }

            Vector3 a = SelfTestContext.OnGround(wallPosition - forward * 4f) + Vector3.up * 1.2f;
            Vector3 b = SelfTestContext.OnGround(wallPosition + forward * 4f) + Vector3.up * 1.2f;
            yield return null;
            if (walls.Count > 0)
            {
                Vector3 top = Vector3.up * 0.3f;
                bool crosses = Physics.Linecast(a + top, b + top, out RaycastHit wallHit, LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid"))
                               && walls.Any(w => wallHit.collider.transform.IsChildOf(w.transform));
                _t.Check("flight path crosses the wall", crosses, $"{walls.Count}x {wallPrefab}" + (crosses ? "" : ", nothing hit between the patrol points"));
            }
            FlightSpikeController flyer = _t.Track(FlightSpike.Spawn(a, 4f));
            if (!_t.Check("spawn non-persistent flyer", flyer != null)) yield break;
            _t.Check("flyer is not saved", !flyer.GetComponent<ZNetView>().GetZDO().Persistent);

            flyer.Patrol(new List<Vector3> { a, b });
            yield return new WaitForSeconds(Seconds);
            flyer.Stop();

            _t.Check("crossed the wall repeatedly", flyer.WaypointsReached >= 4, $"{flyer.WaypointsReached} waypoints in {Seconds}s");
            _t.Check("never stuck", flyer.StuckEvents == 0, flyer.Report());
            _t.Check("stayed above the ground", flyer.MinClearance > 0.2f, $"min clearance {flyer.MinClearance:0.00} m");

            Character character = flyer.Character;
            float before = character.GetHealth();
            var hit = new HitData { m_point = character.transform.position, m_dir = forward };
            hit.m_damage.m_blunt = 5f;
            character.Damage(hit);
            yield return SelfTestContext.WaitFrames(5);
            _t.Check("can be hit", character.GetHealth() < before || flyer.Hits > 0, $"health {before:0} → {character.GetHealth():0}");
            yield return Screenshots.Capture(_t.OutputFolder, "flyer", character.transform.position, 3f);
        }
    }

    /// <summary>HB-T-009 automated in-session: random destination-first moves with forced saves, then a reload.</summary>
    internal sealed class TransferSuite
    {
        private const float Seconds = 10f;
        private const int MovesPerFrame = 5;

        private readonly SelfTestContext _t;

        public TransferSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Transfers";
            Player player = _t.Player;
            Vector3 origin = SelfTestContext.OnGround(player.transform.position + player.transform.right * 8f);
            var chests = new List<GameObject>
            {
                _t.SpawnVanilla("piece_chest_wood", origin, Quaternion.identity),
                _t.SpawnVanilla("piece_chest_wood", SelfTestContext.OnGround(origin + player.transform.right * 2f), Quaternion.identity),
            };
            if (!_t.Check("spawn two chests", chests.All(c => c != null))) yield break;
            yield return null;

            Inventory first = chests[0].GetComponent<Container>().GetInventory();
            first.AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 50);
            first.AddItem(ObjectDB.instance.GetItemPrefab("Stone"), 30);
            first.AddItem(ObjectDB.instance.GetItemPrefab("Resin"), 20);
            string expected = Totals(chests);

            int moves = 0;
            int saves = 0;
            float end = Time.time + Seconds;
            float nextSave = Time.time + 2f;
            while (Time.time < end)
            {
                for (int i = 0; i < MovesPerFrame; i++)
                    if (MoveOne(chests)) moves++;
                if (Time.time >= nextSave)
                {
                    nextSave = Time.time + 2f;
                    ZNet.instance.Save(false);
                    saves++;
                }
                yield return null;
            }

            _t.Check("items moved", moves > 100, $"{moves} moves, {saves} saves");
            _t.Check("totals unchanged after moves", Totals(chests) == expected, Totals(chests));

            for (int i = 0; i < chests.Count; i++)
            {
                GameObject again = null;
                yield return _t.Reload(chests[i], go => again = go);
                chests[i] = again;
            }
            _t.Check("totals unchanged after reload", chests.All(c => c != null) && Totals(chests) == expected,
                chests.All(c => c != null) ? Totals(chests) : "chest missing");
        }

        private static bool MoveOne(List<GameObject> chests)
        {
            Inventory source = chests[Random.Range(0, chests.Count)].GetComponent<Container>().GetInventory();
            Inventory destination = chests[Random.Range(0, chests.Count)].GetComponent<Container>().GetInventory();
            List<ItemDrop.ItemData> items = source.GetAllItems();
            if (source == destination || items.Count == 0) return false;

            ItemDrop.ItemData item = items[Random.Range(0, items.Count)];
            int amount = Random.Range(1, item.m_stack + 1);
            ItemDrop.ItemData moving = item.Clone();
            moving.m_stack = amount;
            if (!destination.CanAddItem(moving) || !destination.AddItem(moving)) return false;
            source.RemoveItem(item, amount);
            return true;
        }

        private static string Totals(IEnumerable<GameObject> chests) =>
            SpikeUtils.Format(SpikeUtils.Totals(chests.Select(c => c.GetComponent<Container>().GetInventory())));
    }
}
#endif
