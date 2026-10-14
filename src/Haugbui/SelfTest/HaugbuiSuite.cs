#if DEBUG
using System;
using System.Collections;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>M3: creature prefab, presence by day/night/Altar/Rest, movement, deposit, death and cooldown, destruction.</summary>
    internal sealed class HaugbuiSuite
    {
        private sealed class FakeTime : ITimeSource
        {
            public float Fraction = 0.5f;
            public double Offset = 100000;
            public bool IsNight => Fraction <= PresencePolicy.Dawn || Fraction >= PresencePolicy.Dusk;
            public float DayFraction => Fraction;
            public double DayLengthSeconds => 1800;
            public double Now => Offset + Time.timeAsDouble;
        }

        private const float Noon = 0.5f;
        private const float Night = 0.9f;

        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private Vector3 _arena;
        private TombController _tomb;

        public HaugbuiSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Haugbui";
            TimeSource.Override = _time;
            try
            {
                if (!Prefab()) yield break;
                yield return Setup();
                if (_tomb == null) yield break;
                yield return DayAndNight();
                yield return Movement();
                yield return ReloadWhileOut();
                yield return Deposit();
                yield return Rest();
                yield return Death();
                yield return Altar();
                yield return Toggles();
                yield return Destruction();
            }
            finally
            {
                TimeSource.Override = null;
            }
        }

        private bool Prefab()
        {
            GameObject prefab = CreaturePrefab.Prefab;
            if (!_t.Check("creature prefab registered", prefab != null && ZNetScene.instance.GetPrefab(CreaturePrefab.Name) != null)) return false;
            _t.Check("creature prefab not persistent", !prefab.GetComponent<ZNetView>().m_persistent);
            _t.Check("creature has no drops", prefab.GetComponent<CharacterDrop>() == null);
            _t.Check("creature tamed", prefab.GetComponent<Character>().m_tamed);
            _t.Check("creature materials tinted purple (copies)", CreaturePrefab.TintedMaterials > 0,
                $"{CreaturePrefab.TintedMaterials} materials: " + string.Join("; ", CreaturePrefab.MaterialDiagnostics));
            _t.Check("creature aura particles found", CreaturePrefab.AuraPaths.Count > 0, string.Join(", ", CreaturePrefab.AuraPaths));
            _t.Check("never spawns naturally", !SpawnSystemLists(CreaturePrefab.Name));
            return true;
        }

        private static bool SpawnSystemLists(string name) =>
            UnityEngine.Object.FindObjectsByType<SpawnSystem>(FindObjectsSortMode.None)
                .SelectMany(s => s.m_spawnLists).SelectMany(l => l.m_spawners)
                .Any(s => s.m_prefab != null && s.m_prefab.name == name);

        private IEnumerator Setup()
        {
            Player player = _t.Player;
            _arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.forward * 9f);
            GameObject bed = _t.SpawnVanilla("bed", SelfTestContext.OnGround(_arena + new Vector3(-6, 0, 0)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            bed.GetComponent<Bed>().SetOwner(player.GetPlayerID(), player.GetPlayerName());

            HaugbuiStructure tomb = Place(StructureKind.Tomb, _arena);
            if (!_t.Check("tomb placed", tomb != null)) yield break;
            yield return SelfTestContext.WaitFrames(3);
            _tomb = tomb.GetComponent<TombController>();
            _t.Check("tomb has a controller", _tomb != null);
        }

        private IEnumerator DayAndNight()
        {
            yield return SetTime(Noon);
            _t.Check("dormant at noon without Altar", _tomb.Machine.State == HaugbuiState.Dormant && _tomb.Creature == null, _tomb.Machine.State.ToString());

            yield return SetTime(Night);
            _t.Check("emerges at night", _tomb.Creature != null, _tomb.Machine.State.ToString());
            if (_tomb.Creature == null) yield break;
            _t.Check("creature is not saved", !_tomb.Creature.View.GetZDO().Persistent);
            _t.Check("vanilla AI disabled", _tomb.Creature.GetComponent<BaseAI>()?.enabled != true);
            yield return new WaitForSeconds(2f);
            _t.Check("idle after emerging", _tomb.Machine.State == HaugbuiState.Idle, _tomb.Machine.State.ToString());
            _t.Check("exactly one creature", CountCreatures() == 1, CountCreatures().ToString());
            _t.Check("no dark aura without Altar", _tomb.Creature.Visuals != null && !_tomb.Creature.Visuals.DarkAuraShown);
            _t.Check("max health from level 1", Mathf.Approximately(_tomb.Creature.Character.GetMaxHealth(), _tomb.Attributes.MaxHealth),
                $"{_tomb.Creature.Character.GetMaxHealth()}");

            _time.Fraction = 0.24f; // inside the dawn lead
            _tomb.TickNow();
            _t.Check("starts returning before dawn", _tomb.Machine.State == HaugbuiState.Returning, _tomb.Machine.State.ToString());
            yield return WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 20f);
            _t.Check("entered the tomb before dawn", _tomb.Machine.State == HaugbuiState.Dormant && _tomb.Creature == null, _tomb.Machine.State.ToString());
        }

        private IEnumerator Movement()
        {
            yield return Emerge();
            HaugbuiCreature creature = _tomb.Creature;
            if (creature == null) yield break;

            _tomb.Machine.TryEnter(HaugbuiState.Traveling);
            float max = _tomb.Attributes.MaxSpeed;
            creature.MaxSpeed = max;
            creature.ResetTelemetry();
            Vector3 target = SelfTestContext.OnGround(_arena + new Vector3(12, 0, 4)) + Vector3.up;
            creature.MoveTo(target);
            yield return WaitFor(() => creature == null || creature.Arrived, 25f);
            if (creature == null) yield break;
            _t.Check("arrives within 1 m", creature.Arrived && Vector3.Distance(creature.transform.position, target) <= HaugbuiCreature.ArriveDistance + 0.05f,
                $"{Vector3.Distance(creature.transform.position, target):0.00} m");
            _t.Check("never exceeds level max speed", creature.MaxObservedSpeed <= max * 1.15f, $"max {creature.MaxObservedSpeed:0.00} of {max} m/s");
            _t.Check("no position jumps over 1 m", creature.MaxFrameJump < 1f, $"{creature.MaxFrameJump:0.00} m");
            creature.Stop();
            _tomb.Machine.TryEnter(HaugbuiState.Idle);
        }

        private IEnumerator ReloadWhileOut()
        {
            if (_tomb.Creature == null) yield return Emerge();
            GameObject reloaded = null;
            yield return _t.Reload(_tomb.gameObject, go => reloaded = go);
            _tomb = reloaded != null ? reloaded.GetComponent<TombController>() : null;
            if (!_t.Check("tomb reloaded", _tomb != null)) yield break;
            yield return new WaitForSeconds(1.5f);
            _t.Check("one creature after reload, resumed", CountCreatures() == 1 && _tomb.Creature != null, $"{CountCreatures()} creatures");
        }

        private IEnumerator Deposit()
        {
            if (_tomb == null) yield break;
            if (_tomb.Creature == null) yield return Emerge();
            _tomb.Carried.Add(Stack("Wood", 12));
            _t.Check("carried items saved", _tomb.Structure.Zdo.GetByteArray(ZdoKeys.Carried.GetStableHashCode()) != null);
            int before = Count("Wood");
            yield return SetTime(Noon);
            yield return WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 20f);
            _t.Check("carried empty after entering", _tomb.Carried.IsEmpty);
            _t.Check("carried items in tomb storage", Count("Wood") == before + 12, $"{Count("Wood") - before}/12");

            Inventory storage = _tomb.GetComponent<Container>().GetInventory();
            storage.AddItem(ObjectDB.instance.GetItemPrefab("Stone"), 1000);
            while (storage.GetEmptySlots() > 0) storage.AddItem(ObjectDB.instance.GetItemPrefab("Stone"), 50);
            // More than the free room of the existing Wood stack, so some must overflow.
            int total = Count("Wood") + 100;
            _tomb.Carried.Restore(new[] { Stack("Wood", 100) });
            _tomb.DepositCarried();
            int overflow = _tomb.Overflow.Where(s => s.Prefab == "Wood").Sum(s => s.Amount);
            _t.Check("full storage: fills the open stack, rest to overflow, totals kept",
                Count("Wood") == 50 && Count("Wood") + overflow == total,
                $"storage {Count("Wood")}, overflow {overflow}, expected total {total}");
            storage.RemoveAll();
        }

        private IEnumerator Rest()
        {
            yield return Emerge();
            _tomb.SetToggle(TombToggles.Resting, true, out _);
            yield return WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 20f);
            yield return new WaitForSeconds(1.2f);
            _t.Check("Rest: returns and stays inside at night", _tomb.Machine.State == HaugbuiState.Dormant && _tomb.Creature == null);
            _tomb.SetToggle(TombToggles.Resting, false, out _);
            yield return SetTime(Night);
            _t.Check("Rest off: emerges again", _tomb.Creature != null);
        }

        private IEnumerator Death()
        {
            if (_tomb.Creature == null) yield return Emerge();
            HaugbuiCreature creature = _tomb.Creature;
            if (creature == null) yield break;
            _tomb.Carried.Add(Stack("Resin", 5));
            int before = Count("Resin");
            var hit = new HitData();
            hit.m_damage.m_damage = 100000f;
            creature.Character.Damage(hit);
            yield return WaitFor(() => _tomb.Machine.State == HaugbuiState.Dead, 5f);
            _t.Check("dies", _tomb.Machine.State == HaugbuiState.Dead, _tomb.Machine.State.ToString());
            _t.Check("death items moved to tomb storage", _tomb.Carried.IsEmpty && Count("Resin") == before + 5);
            double cooldown = HaugbuiPlugin.Instance.Settings.CooldownSeconds.Value;
            _t.Check("cooldown running", _tomb.CooldownRemaining > cooldown - 10, $"{_tomb.CooldownRemaining:0}s");
            yield return new WaitForSeconds(1.2f);
            CountdownLabel label = _tomb.GetComponent<CountdownLabel>();
            _t.Check("countdown label shown", label != null && label.Shown.Length > 0, label?.Shown);
            _t.Check("countdown label has a font", label != null && label.HasFont);
            _t.Check("death time saved", !double.IsNaN(_tomb.State.DeathTime));

            _tomb.TickNow();
            _t.Check("stays dead during cooldown at night", _tomb.Creature == null);
            _time.Offset += cooldown + 5;
            yield return SetTime(Noon);
            _t.Check("cooldown over at noon: waits for night", _tomb.Creature == null && _tomb.CooldownRemaining == 0);
            yield return SetTime(Night);
            _t.Check("resurrects at night", _tomb.Creature != null, _tomb.Machine.State.ToString());
        }

        private IEnumerator Altar()
        {
            HaugbuiStructure altar = Place(StructureKind.Altar, SelfTestContext.OnGround(_arena + new Vector3(3, 0, 0)));
            if (!_t.Check("Altar placed", altar != null)) yield break;
            yield return SelfTestContext.WaitFrames(3);
            yield return SetTime(Noon);
            yield return WaitFor(() => _tomb.Machine.State == HaugbuiState.Idle, 10f);
            yield return new WaitForSeconds(0.5f);
            _t.Check("Altar: out at noon", _tomb.Creature != null && _tomb.Machine.State == HaugbuiState.Idle, _tomb.Machine.State.ToString());
            _t.Check("Altar: dark aura on", _tomb.Creature != null && _tomb.Creature.Visuals.DarkAuraShown,
                $"{_tomb.Creature?.Visuals?.AuraParts} aura parts");

            ZNetScene.instance.Destroy(altar.gameObject);
            yield return SelfTestContext.WaitFrames(3);
            _tomb.TickNow();
            _t.Check("Altar removed at noon: returns", _tomb.Machine.State == HaugbuiState.Returning,
                _tomb.Machine.State.ToString());
            if (_tomb.Creature != null)
            {
                yield return new WaitForSeconds(0.6f);
                _t.Check("Altar removed: dark aura off", _tomb.Creature == null || !_tomb.Creature.Visuals.DarkAuraShown);
            }
            yield return WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 20f);
        }

        private IEnumerator Toggles()
        {
            _t.Check("combat refused without Longsword", !_tomb.SetToggle(TombToggles.Combat, true, out string refusal) && refusal != null);
            _tomb.SetToggle(TombToggles.Harvesting, false, out _);
            GameObject reloaded = null;
            yield return _t.Reload(_tomb.gameObject, go => reloaded = go);
            _tomb = reloaded != null ? reloaded.GetComponent<TombController>() : null;
            if (!_t.Check("tomb reloaded for toggles", _tomb != null)) yield break;
            _t.Check("toggles persist across reload", _tomb.State != null && !_tomb.State.Has(TombToggles.Harvesting) && _tomb.State.Has(TombToggles.GroundItems));
        }

        private IEnumerator Destruction()
        {
            if (_tomb == null) yield break;
            yield return SetTime(Night);
            _tomb.Carried.Restore(new[] { Stack("Flint", 7) });
            _tomb.Overflow.Add(Stack("Flint", 3));
            Vector3 at = _tomb.transform.position;
            _tomb.GetComponent<WearNTear>().Remove();
            yield return new WaitForSeconds(1f);
            int flint = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
                .Where(d => d.m_itemData.m_dropPrefab != null && d.m_itemData.m_dropPrefab.name == "Flint" && Vector3.Distance(d.transform.position, at) < 5f)
                .Sum(d => d.m_itemData.m_stack);
            _t.Check("tomb destroyed: carried and overflow dropped", flint == 10, $"{flint}/10 flint on the ground");
            _t.Check("tomb destroyed: creature gone", CountCreatures() == 0);
            foreach (ItemDrop drop in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
                         .Where(d => Vector3.Distance(d.transform.position, at) < 5f))
                _t.Track(drop.gameObject);
        }

        private IEnumerator Emerge()
        {
            if (_tomb.Creature != null) yield break;
            yield return SetTime(Night);
            yield return WaitFor(() => _tomb.Machine.State == HaugbuiState.Idle, 5f);
        }

        private IEnumerator SetTime(float fraction)
        {
            _time.Fraction = fraction;
            _tomb.TickNow();
            yield return SelfTestContext.WaitFrames(2);
        }

        private static IEnumerator WaitFor(Func<bool> condition, float timeout)
        {
            float end = Time.time + timeout;
            while (Time.time < end && !condition()) yield return null;
        }

        private static int CountCreatures() => HaugbuiCreature.All.Count(c => c != null && !c.Character.IsDead());

        private int Count(string prefab)
        {
            string token = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            return _tomb.GetComponent<Container>().GetInventory().CountItems(token, -1, false);
        }

        private static ItemStack Stack(string prefab, int amount)
        {
            var stack = new ItemStack { Prefab = prefab, Amount = amount };
            ItemBridge.RefreshShared(stack);
            return stack;
        }

        private HaugbuiStructure Place(StructureKind kind, Vector3 position)
        {
            Piece piece = ZNetScene.instance.GetPrefab(StructureDefinitions.For(kind).PieceId)?.GetComponent<Piece>();
            if (piece == null) return null;
            _t.Player.PlacePiece(piece, position, Quaternion.identity, doAttack: false);
            HaugbuiStructure placed = StructureRegistry.Loaded
                .Where(s => s.Kind == kind && Vector3.Distance(s.transform.position, position) < 0.1f)
                .OrderByDescending(s => s.GetInstanceID())
                .FirstOrDefault();
            return placed != null ? _t.Track(placed) : null;
        }
    }
}
#endif
