#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Combat;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>
    /// M8 combat on the fake clock: the toggle and its confirmation, the target matrix, fighting and level scaling,
    /// the interrupt of a delivery, flight with combat off, losing the Longsword and sleep fast mode. Spawned monsters
    /// carry no loot; the player is in god mode while the suite runs.
    /// </summary>
    internal sealed class CombatSuite
    {
        private const float FightTimeout = 30f;
        private const int Wood = 30;

        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private Vector3 _arena;
        private TombController _a;
        private TombController _b;
        private HaugbuiStructure _longswordA;
        private Container _source;
        private DeliveryChest _delivery;
        private bool _godMode;
        private float _takenLevel1 = float.NaN;

        public CombatSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Combat";
            TimeSource.Override = _time;
            _godMode = _t.Player.InGodMode();
            _t.Player.SetGodMode(true);
            try
            {
                yield return Setup();
                if (_a == null || _b == null) yield break;
                yield return ToggleWithoutLongsword();
                yield return Emerge();
                if (_a.Creature == null) yield break;
                yield return LevelOne();
                yield return PlaceLongswords();
                if (_longswordA == null) yield break;
                yield return ToggleConfirmation();
                yield return TargetMatrix();
                yield return Attack();
                yield return RaidMode();
                yield return TwoHaugbuis();
                yield return Interrupt();
                yield return CombatOff();
                yield return LongswordRemoved();
                yield return Sleep();
                yield return LevelSix();
                yield return Settle();
            }
            finally
            {
                TombController.SkippingForTests = false;
                TombController.TributeSuspendedForTests = true;
                TimeSource.Override = null;
                if (_t.Player != null) _t.Player.SetGodMode(_godMode);
                if (CombatWarning.Pending != null) CombatWarning.Pending.noCallback?.Invoke();
            }
        }

        private IEnumerator Setup()
        {
            Player player = _t.Player;
            _arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.forward * 22f, 10f);
            GameObject bed = _t.SpawnVanilla("bed", Ground(new Vector3(-6, 0, 0)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            bed.GetComponent<Bed>().SetOwner(player.GetPlayerID(), player.GetPlayerName());

            _time.Fraction = FakeTime.Noon;
            HaugbuiStructure a = _t.PlaceStructure(StructureKind.Tomb, _arena);
            HaugbuiStructure b = _t.PlaceStructure(StructureKind.Tomb, Ground(new Vector3(0, 0, 12)));
            HaugbuiStructure delivery = _t.PlaceStructure(StructureKind.DeliveryChest, Ground(new Vector3(16, 0, -10)));
            GameObject source = _t.PlaceVanilla("piece_chest_wood", Ground(new Vector3(-16, 0, -10)), Quaternion.identity);
            yield return SelfTestContext.WaitFrames(3);
            _a = a != null ? a.GetComponent<TombController>() : null;
            _b = b != null ? b.GetComponent<TombController>() : null;
            _delivery = delivery != null ? delivery.GetComponent<DeliveryChest>() : null;
            _source = source != null ? source.GetComponentInChildren<Container>() : null;
            if (!_t.Check("two tombs, a delivery chest and a wood chest placed", _a != null && _b != null && _delivery != null && _source != null))
            {
                _a = null;
                yield break;
            }
            var wood = new ItemStack { Prefab = "Wood", Amount = Wood, WorldLevel = Game.m_worldLevel };
            ItemBridge.RefreshShared(wood);
            ItemBridge.AddTo(_source.GetInventory(), wood);
            Harden(_source);
            Harden(_delivery);
            Harden(a);
            Harden(b);
            foreach (TombController tomb in new[] { _a, _b })
            {
                tomb.Worker.Paused = true;
                tomb.SetToggle(TombToggles.GroundItems, false, out _);
                tomb.SetToggle(TombToggles.Harvesting, false, out _);
                tomb.SetToggle(TombToggles.Production, false, out _);
            }
        }

        private IEnumerator ToggleWithoutLongsword()
        {
            _t.Check("combat toggle OFF and unacknowledged by default",
                !_a.State.Has(TombToggles.Combat) && !_a.State.Has(TombToggles.CombatAcknowledged), _a.State.Toggles.ToString());
            bool? on = null;
            string refusal = null;
            CombatWarning.RequestEnable(_a, (o, r) => { on = o; refusal = r; });
            _t.Check("combat refused without the Longsword, no confirmation shown",
                on == false && refusal == "$haugbui_panel_needs_longsword" && CombatWarning.Pending == null, $"on {on}, refusal {refusal}");
            yield break;
        }

        private IEnumerator Emerge()
        {
            yield return SetTime(FakeTime.Night);
            yield return SelfTestContext.WaitFor(() => _a.Machine.State == HaugbuiState.Idle && _b.Machine.State == HaugbuiState.Idle, 10f);
            _t.Check("both Haugbuis out at night", _a.Creature != null && _b.Creature != null, $"{_a.Machine.State}, {_b.Machine.State}");
        }

        /// <summary>Level 1 cannot fight (the Longsword is itself an upgrade), but it takes damage at its own factor.</summary>
        private IEnumerator LevelOne()
        {
            float baseMultiplier = HaugbuiPlugin.Instance.Settings.CombatDamageMultiplier.Value;
            _t.Check("level 1: hits ×1.0 (times the base multiplier), damage taken ÷1.0",
                _a.Attributes.Level == 1 && Mathf.Approximately(_a.Creature.DealtFactor, baseMultiplier) && Mathf.Approximately(_a.Creature.TakenFactor, 1f),
                $"level {_a.Attributes.Level}, dealt ×{_a.Creature.DealtFactor}, taken ×{_a.Creature.TakenFactor}, base {baseMultiplier}");
            float lost = 0f;
            yield return MeasureTaken(_a, 10f, l => lost = l);
            _takenLevel1 = lost;
            _t.Check("level 1: a hit of 10 is taken in full (scaling ÷1.0)",
                Mathf.Approximately(_a.Creature.LastTakenScaled, _a.Creature.LastTakenIncoming) && lost > 0f,
                $"incoming {_a.Creature.LastTakenIncoming:0.##}, after scaling {_a.Creature.LastTakenScaled:0.##}, health lost {lost:0.##}");
            yield return WaitCalm(_a);
        }

        private IEnumerator PlaceLongswords()
        {
            _longswordA = _t.PlaceStructure(StructureKind.Longsword, Ground(new Vector3(6, 0, 0)));
            HaugbuiStructure longswordB = _t.PlaceStructure(StructureKind.Longsword, Ground(new Vector3(6, 0, 12)));
            Harden(_longswordA);
            Harden(longswordB);
            yield return SelfTestContext.WaitFrames(5);
            DismissWarnings();
            bool ok = _longswordA != null && longswordB != null
                      && _a.Structure.Progress.Has(TombAbilities.Combat) && _b.Structure.Progress.Has(TombAbilities.Combat);
            if (!_t.Check("Longswords bound to both tombs (level 2)", ok && _a.Attributes.Level == 2, $"levels {_a.Attributes.Level}/{_b.Attributes.Level}"))
                _longswordA = null;
            _t.Check("binding the Longsword does not turn combat on", !_a.CombatEnabled && !_a.State.Has(TombToggles.Combat));
            yield return new WaitForSeconds(0.6f);
        }

        private IEnumerator ToggleConfirmation()
        {
            bool? on = null;
            CombatWarning.RequestEnable(_a, (o, _) => on = o);
            YesNoPopup popup = CombatWarning.Pending;
            _t.Check("first enable opens the confirmation", popup != null && UnifiedPopup.IsVisible() && on == null && !_a.State.Has(TombToggles.Combat));
            if (popup == null) yield break;
            popup.noCallback();
            yield return null;
            _t.Check("cancel: combat stays OFF and unacknowledged",
                on == false && !_a.State.Has(TombToggles.Combat) && !_a.State.Has(TombToggles.CombatAcknowledged) && CombatWarning.Pending == null,
                $"on {on}, toggles {_a.State.Toggles}");

            on = null;
            CombatWarning.RequestEnable(_a, (o, _) => on = o);
            popup = CombatWarning.Pending;
            _t.Check("after a cancel the next enable asks again", popup != null);
            if (popup == null) yield break;
            popup.yesCallback();
            yield return null;
            _t.Check("confirm: combat_ack stored and combat ON",
                on == true && _a.State.Has(TombToggles.Combat | TombToggles.CombatAcknowledged) && _a.CombatEnabled, $"on {on}, toggles {_a.State.Toggles}");

            _a.SetToggle(TombToggles.Combat, false, out _);
            on = null;
            CombatWarning.RequestEnable(_a, (o, _) => on = o);
            _t.Check("later enables need no confirmation", on == true && CombatWarning.Pending == null && _a.State.Has(TombToggles.Combat));

            _b.AcknowledgeCombat();
            SetCombat(_a, false);
            SetCombat(_b, false);
        }

        /// <summary>
        /// AI states are set by hand in the same frame as the judgement, so the matrix does not depend on what the
        /// monsters decide on their own.
        /// </summary>
        private IEnumerator TargetMatrix()
        {
            Vector3 center = _a.transform.position;
            Character wolf = Spawn("Wolf", new Vector3(-8, 0, -6));
            yield return SelfTestContext.WaitFrames(2);
            wolf?.SetTamed(true);
            Character attacker = Spawn("Greydwarf", new Vector3(8, 0, -6));
            Character skeleton = Spawn(Prefer("Skeleton_NoArcher", "Skeleton"), new Vector3(8, 0, 6));
            Character sieger = Spawn("Greydwarf", new Vector3(-14, 0, -6));
            Character calm = Spawn("Greydwarf", new Vector3(10, 0, 0));
            Character fleeing = Spawn("Greydwarf", new Vector3(-10, 0, 8));
            Character deer = Spawn("Deer", new Vector3(-8, 0, 6));
            Character boar = Spawn("Boar", new Vector3(-10, 0, -2));
            Character angryBoar = Spawn("Boar", new Vector3(6, 0, 10));
            Character boss = ZNetScene.instance.GetPrefab("Eikthyr") != null ? Spawn("Eikthyr", new Vector3(12, 0, -12)) : null;
            float radius = _a.Attributes.Radius;
            Vector3 toPlayer = _t.Player.transform.position - center;
            toPlayer.y = 0f;
            Vector3 farOffset = (toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : Vector3.back) * (radius + 6f);
            Character far = Spawn("Greydwarf", farOffset);
            yield return SelfTestContext.WaitFrames(3);

            Character haugbuiB = _b.Creature != null ? _b.Creature.Character : null;
            Piece chest = _source.GetComponentInParent<Piece>();
            Provoke(attacker, _t.Player);
            Provoke(skeleton, haugbuiB);
            Siege(sieger, chest);
            Calm(calm);
            Provoke(fleeing, _t.Player);
            if (fleeing != null && fleeing.GetComponent<MonsterAI>() is MonsterAI fleeAI) fleeAI.m_lastFlee = Time.time;
            Calm(deer);
            Calm(boar);
            Provoke(angryBoar, _t.Player);
            Provoke(wolf, _t.Player);
            Calm(boss);
            Provoke(far, _t.Player);

            Expect("greydwarf attacking the player: target", attacker, false, TargetVerdict.Valid);
            Expect("skeleton attacking a Haugbui: target", skeleton, false, TargetVerdict.Valid);
            Expect("greydwarf attacking a building: target", sieger, false, TargetVerdict.Valid, chest != null ? "" : "no chest piece");
            Expect("calm greydwarf ignored", calm, false, TargetVerdict.NotAttacking);
            Expect("fleeing greydwarf ignored", fleeing, false, TargetVerdict.NotAttacking);
            Expect("deer ignored", deer, false, TargetVerdict.NotAttacking);
            Expect("calm wild boar ignored", boar, false, TargetVerdict.NotAttacking);
            Expect("wild boar attacking the player: target", angryBoar, false, TargetVerdict.Valid);
            Expect("tamed wolf ignored", wolf, false, TargetVerdict.Tamed);
            Expect("player ignored", _t.Player, false, TargetVerdict.Player);
            Expect("another Haugbui ignored", haugbuiB, false, TargetVerdict.Haugbui);
            Expect("attacker outside the radius ignored", far, false, TargetVerdict.OutOfRadius);
            if (boss != null) Expect("calm boss ignored outside a raid", boss, false, TargetVerdict.NotAttacking);
            else _t.Skip("calm boss ignored outside a raid", "Eikthyr prefab not found; the Core target matrix covers bosses");

            Expect("raid: calm greydwarf is a target", calm, true, TargetVerdict.Valid);
            Expect("raid: deer is a target", deer, true, TargetVerdict.Valid);
            Expect("raid: calm wild boar is a target", boar, true, TargetVerdict.Valid);
            Expect("raid: tamed wolf still ignored", wolf, true, TargetVerdict.Tamed);
            Expect("raid: player still ignored", _t.Player, true, TargetVerdict.Player);
            Expect("raid: other Haugbui still ignored", haugbuiB, true, TargetVerdict.Haugbui);
            Expect("raid: outside the radius still ignored", far, true, TargetVerdict.OutOfRadius);
            if (boss != null) Expect("raid: boss is a target", boss, true, TargetVerdict.Valid);
            else _t.Skip("raid: boss is a target", "Eikthyr prefab not found; the Core target matrix covers bosses");

            foreach (Character character in new[] { wolf, attacker, skeleton, sieger, calm, fleeing, deer, boar, angryBoar, boss, far }) Remove(character);
            yield return SelfTestContext.WaitFrames(3);
            yield return WaitCalm(_a);
            yield return WaitCalm(_b);
        }

        /// <summary>A real random event started at the tomb: offensive while it lasts, attackers only once it is over.</summary>
        private IEnumerator RaidMode()
        {
            RandEventSystem events = RandEventSystem.instance;
            RandomEvent template = events != null ? events.m_events.FirstOrDefault(e => e.m_name == "army_eikthyr") ?? events.m_events.FirstOrDefault() : null;
            if (template == null)
            {
                _t.Skip("raid mode", "no random event defined");
                yield break;
            }
            yield return EnsureOut(_a);
            Heal(_a);
            SetCombat(_a, true);
            Character deer = Spawn("Deer", _a.Creature.transform.position - _arena + new Vector3(6, 0, 4));
            Character boar = Spawn("Boar", _a.Creature.transform.position - _arena + new Vector3(-6, 0, 4));
            bool attacked = false;
            float end = Time.time + 2.5f;
            while (Time.time < end)
            {
                Calm(deer);
                Calm(boar);
                attacked |= _a.Machine.State == HaugbuiState.Combat;
                yield return null;
            }
            _t.Check("no raid: a calm deer and boar are left alone", !attacked && !_a.Combat.Raid, _a.Machine.State.ToString());

            try
            {
                events.SetRandomEventByName(template.m_name, _a.transform.position);
                _t.Check("raid started at the tomb reaches its radius", CombatGateway.RaidReaches(_a.transform.position, _a.Attributes.Radius), template.m_name);
                end = Time.time + 4f;
                while (Time.time < end && !(_a.Combat.Raid && _a.Machine.State == HaugbuiState.Combat))
                {
                    Calm(deer);
                    Calm(boar);
                    yield return null;
                }
                _t.Check("raid in range: attacks a creature that is not attacking", _a.Combat.Raid && _a.Machine.State == HaugbuiState.Combat
                    && (_a.Combat.Target == deer || _a.Combat.Target == boar), $"raid {_a.Combat.Raid}, {_a.Machine.State}, target {_a.Combat.Target?.m_name}");
            }
            finally
            {
                events.ResetRandomEvent();
            }

            end = Time.time + 3f;
            while (Time.time < end && _a.Combat.Raid)
            {
                Calm(deer);
                Calm(boar);
                yield return null;
            }
            for (int i = 0; i < 5; i++)
            {
                Calm(deer);
                Calm(boar);
                yield return null;
            }
            Character target = _a.Combat.Target;
            _t.Check("raid over: only creatures still attacking stay targets", !_a.Combat.Raid
                && (target == null || _a.Combat.Judge(target, raid: false) == TargetVerdict.Valid),
                $"raid {_a.Combat.Raid}, target {target?.m_name}");
            Remove(deer);
            Remove(boar);
            yield return WaitCalm(_a);
        }

        private IEnumerator Attack()
        {
            yield return EnsureOut(_a);
            Heal(_a);
            SetCombat(_a, true);
            int alerts = _a.Combat.Alerts, attacks = _a.Combat.AttacksStarted, hits = _a.Creature.HitsDealt;
            Character near = Spawn("Greydwarf", new Vector3(5, 0, -5));
            Character farther = Spawn(Prefer("Skeleton_NoArcher", "Skeleton"), new Vector3(-12, 0, -8));
            yield return Hold(near, _a.Creature.Character, () => _a.Machine.State == HaugbuiState.Combat, 3f);
            _t.Check("hostile detected: fights it", _a.Machine.State == HaugbuiState.Combat, _a.Machine.State.ToString());
            _t.Check("always the nearest target", _a.Combat.Target == near, _a.Combat.Target != null ? _a.Combat.Target.m_name : "none");
            if (CreatureVisuals.LowQuality) _t.Skip("alert sound on detection", "EffectsQuality Low");
            else _t.Check("alert sound on detection", _a.Combat.Alerts > alerts);

            yield return SelfTestContext.WaitFor(() => Gone(near), FightTimeout);
            _t.Check("kills a greydwarf", Gone(near),
                $"attacks {_a.Combat.AttacksStarted - attacks}, hits {_a.Creature.HitsDealt - hits}, greydwarf health {(near != null ? near.GetHealth() : 0):0}");
            float expected = CombatMath.DealtFactor(_a.Attributes.CombatMultiplier, HaugbuiPlugin.Instance.Settings.CombatDamageMultiplier.Value);
            _t.Check("level 2: hit damage × its multiplier", _a.Creature.HitsDealt > hits && Ratio(_a.Creature.LastDealtScaled, _a.Creature.LastDealtBase, expected),
                $"base {_a.Creature.LastDealtBase:0.##} → {_a.Creature.LastDealtScaled:0.##} (expected ×{expected:0.##})");
            _t.Check("weapon equipped (Wraith default weapon)", CombatGateway.Weapon(_a.Creature.Humanoid) != null,
                CombatGateway.Describe(CombatGateway.Weapon(_a.Creature.Humanoid)));

            if (_a.Creature != null)
                yield return Hold(farther, _a.Creature.Character, () => _a.Machine.State == HaugbuiState.Combat && _a.Combat.Target == farther, 8f);
            _t.Check("next hostile engaged after the first one", _a.Combat.Target == farther, _a.Combat.Target != null ? _a.Combat.Target.m_name : "none");
            yield return SelfTestContext.WaitFor(() => Gone(farther), FightTimeout);
            Remove(farther);
            yield return SelfTestContext.WaitFor(() => _a.Machine.State == HaugbuiState.Idle, 8f);
            _t.Check("no hostile left: back to Idle", _a.Machine.State == HaugbuiState.Idle, _a.Machine.State.ToString());
        }

        private IEnumerator TwoHaugbuis()
        {
            yield return EnsureOut(_a);
            yield return EnsureOut(_b);
            Heal(_a);
            Heal(_b);
            SetCombat(_a, true);
            SetCombat(_b, true);
            Character enemy = Spawn(Prefer("Greydwarf_Elite", "Greydwarf"), new Vector3(0, 0, 6));
            bool both = false;
            float end = Time.time + 6f;
            while (Time.time < end && !both)
            {
                Provoke(enemy, _a.Creature != null ? _a.Creature.Character : null);
                both = enemy != null && _a.Combat.Target == enemy && _b.Combat.Target == enemy
                       && _a.Machine.State == HaugbuiState.Combat && _b.Machine.State == HaugbuiState.Combat;
                yield return null;
            }
            _t.Check("two Haugbuis attack the same enemy", both, $"{_a.Machine.State}/{_b.Machine.State}");
            int hitsA = _a.Creature != null ? _a.Creature.HitsDealt : 0, hitsB = _b.Creature != null ? _b.Creature.HitsDealt : 0;
            yield return SelfTestContext.WaitFor(() => Gone(enemy), FightTimeout + 15f);
            _t.Check("the shared enemy dies, both back to Idle without conflicts", Gone(enemy),
                $"hits {(_a.Creature != null ? _a.Creature.HitsDealt - hitsA : -1)}/{(_b.Creature != null ? _b.Creature.HitsDealt - hitsB : -1)}");
            Remove(enemy);
            yield return SelfTestContext.WaitFor(() => _a.Machine.State == HaugbuiState.Idle && _b.Machine.State == HaugbuiState.Idle, 8f);
            _t.Check("both idle after the shared fight", _a.Machine.State == HaugbuiState.Idle && _b.Machine.State == HaugbuiState.Idle,
                $"{_a.Machine.State}/{_b.Machine.State}");
            SetCombat(_b, false);
        }

        private IEnumerator Interrupt()
        {
            yield return EnsureOut(_a);
            Heal(_a);
            SetCombat(_a, true);
            _delivery.Edit(l => l.Request("Wood", 20) == RequestEditResult.Ok);
            _a.Worker.Paused = false;
            _a.Worker.PlanNow();
            yield return SelfTestContext.WaitFor(() => _a.Carried.CountPrefab("Wood") > 0 && _a.Machine.State == HaugbuiState.Traveling, 30f);
            if (!_t.Check("delivery trip under way with wood carried", _a.Carried.CountPrefab("Wood") > 0, $"{_a.Machine.State}, plan {_a.Worker.Plan}"))
            {
                _a.Worker.Paused = true;
                yield break;
            }

            Vector3 at = _a.Creature.transform.position - _arena;
            Character greydwarf = Spawn("Greydwarf", at + new Vector3(3, 0, 3));
            yield return Hold(greydwarf, _a.Creature.Character, () => _a.Machine.State == HaugbuiState.Combat, 3f);
            int carried = _a.Carried.CountPrefab("Wood");
            _t.Check("hostile during a delivery: the task is suspended after the atomic step",
                _a.Machine.State == HaugbuiState.Combat && _a.Worker.Plan == null && _a.Worker.LastAbort == "combat" && WoodTotal() == Wood,
                $"state {_a.Machine.State}, abort {_a.Worker.LastAbort}, carried {carried}, total {WoodTotal()}");

            yield return SelfTestContext.WaitFor(() => Gone(greydwarf), FightTimeout);
            _t.Check("carried items kept through the fight", _a.Carried.CountPrefab("Wood") == carried && WoodTotal() == Wood,
                $"carried {carried} → {_a.Carried.CountPrefab("Wood")}, total {WoodTotal()}");
            Remove(greydwarf);
            yield return SelfTestContext.WaitFor(() => _delivery.Ledger.Remaining("Wood") == 0 && _a.Carried.IsEmpty, 60f);
            _t.Check("delivery completes after the fight (re-planned)", _delivery.Ledger.Remaining("Wood") == 0,
                $"remaining {_delivery.Ledger.Remaining("Wood")}, state {_a.Machine.State}");
            _t.Check("wood totals conserved", WoodTotal() == Wood && Delivered() == 20, $"delivered {Delivered()}, total {WoodTotal()}");
            _a.Worker.Paused = true;
        }

        private IEnumerator CombatOff()
        {
            yield return EnsureOut(_a);
            Heal(_a);
            SetCombat(_a, false);
            _b.Worker.Paused = true;
            TombController.TributeSuspendedForTests = false;
            _a.State.Balance = 50;
            long shift = WorldClock.ShiftId(_time.Now, _time.DayLengthSeconds);
            _a.State.LastPaidShift = shift;
            _b.State.LastPaidShift = shift;
            try
            {
                _a.Worker.Paused = false;
                Vector3 pinAt = SelfTestContext.OnGround(_a.transform.position + new Vector3(-14, 0, 14));
                Character greyling = Spawn("Greyling", pinAt - _arena);
                if (greyling == null)
                {
                    _t.Skip("combat OFF checks", "Greyling prefab not found");
                    yield break;
                }
                _delivery.Edit(l => l.Request("Wood", 5) == RequestEditResult.Ok);
                _a.Worker.PlanNow();
                yield return SelfTestContext.WaitFor(() => _a.Machine.State == HaugbuiState.Traveling && DistanceToTomb(_a) > 6f, 20f);

                int attacks = _a.Combat.AttacksStarted, taken = _a.Creature.HitsTaken;
                bool targeted = false, reacted = false;
                float end = Time.time + 2f;
                while (Time.time < end)
                {
                    targeted |= Pin(greyling, pinAt, _a);
                    reacted |= _a.Machine.State == HaugbuiState.Fleeing || _a.Machine.State == HaugbuiState.Combat;
                    yield return null;
                }
                _t.Check("combat OFF: being targeted without damage does not trigger flight",
                    !reacted && _a.Creature.HitsTaken == taken, $"targeted {targeted}, state {_a.Machine.State}");

                Hit(_a, 5f, greyling);
                Heal(_a);
                yield return SelfTestContext.WaitFrames(3);
                _t.Check("combat OFF: damage triggers flight", _a.Machine.State == HaugbuiState.Fleeing && _a.Worker.Plan == null,
                    $"{_a.Machine.State}, plan {_a.Worker.Plan}");

                bool entered = false;
                end = Time.time + 15f;
                while (Time.time < end && DistanceToTomb(_a) > 5f)
                {
                    Pin(greyling, pinAt, _a);
                    entered |= _a.Machine.State == HaugbuiState.Entering || _a.Machine.State == HaugbuiState.Dormant;
                    yield return null;
                }
                end = Time.time + 1.5f;
                while (Time.time < end)
                {
                    Pin(greyling, pinAt, _a);
                    entered |= _a.Machine.State == HaugbuiState.Entering || _a.Machine.State == HaugbuiState.Dormant;
                    yield return null;
                }
                _t.Check("flees to its tomb and waits outside while the attacker remains",
                    !entered && _a.Creature != null && _a.Machine.State == HaugbuiState.Fleeing && DistanceToTomb(_a) <= 6f,
                    $"state {_a.Machine.State}, {DistanceToTomb(_a):0.0} m from the tomb, entered {entered}");
                _t.Check("no new tribute charge", _a.State.Balance == 50 && _a.State.LastPaidShift == shift,
                    $"balance {_a.State.Balance}, shift {_a.State.LastPaidShift}/{shift}");
                _t.Check("combat OFF never attacks", _a.Combat.AttacksStarted == attacks);

                Remove(greyling);
                yield return SelfTestContext.WaitFor(() => _a.Machine.State != HaugbuiState.Fleeing, (float)ThreatTracker.CalmSeconds + 4f);
                _t.Check("no attacker left: stops fleeing", _a.Machine.State == HaugbuiState.Idle || _a.Machine.IsWorking, _a.Machine.State.ToString());
                yield return SelfTestContext.WaitFor(() => _delivery.Ledger.Remaining("Wood") == 0 && _a.Carried.IsEmpty, 60f);
                _t.Check("work resumes after the flight", _delivery.Ledger.Remaining("Wood") == 0 && WoodTotal() == Wood && Delivered() == 25,
                    $"remaining {_delivery.Ledger.Remaining("Wood")}, delivered {Delivered()}, {WoodBreakdown()}");
                _t.Check("still the same paid shift", _a.State.LastPaidShift == shift && _a.State.Balance == 50);
            }
            finally
            {
                _a.Worker.Paused = true;
                TombController.TributeSuspendedForTests = true;
            }
        }

        private IEnumerator LongswordRemoved()
        {
            yield return EnsureOut(_a);
            Heal(_a);
            SetCombat(_a, true);
            Character greydwarf = Spawn("Greydwarf", _a.Creature.transform.position - _arena + new Vector3(-8, 0, -8));
            yield return Hold(greydwarf, _a.Creature.Character, () => _a.Machine.State == HaugbuiState.Combat, 3f);
            bool fighting = _a.Machine.State == HaugbuiState.Combat;
            if (_longswordA)
            {
                ZNetScene.instance.Destroy(_longswordA.gameObject);
                _longswordA = null;
            }
            yield return SelfTestContext.WaitFrames(3);
            int attacks = _a.Combat.AttacksStarted;
            _t.Check("Longsword removed mid-fight: combat stops at once",
                fighting && _a.Machine.State != HaugbuiState.Combat && _a.Combat.Target == null && !_a.CombatEnabled,
                $"was fighting {fighting}, now {_a.Machine.State}");
            yield return new WaitForSeconds(1f);
            _t.Check("no swing after the Longsword is gone", _a.Combat.AttacksStarted == attacks && _a.Machine.State != HaugbuiState.Combat);
            Remove(greydwarf);
            yield return WaitCalm(_a);

            _longswordA = _t.PlaceStructure(StructureKind.Longsword, Ground(new Vector3(6, 0, 0)));
            Harden(_longswordA);
            yield return SelfTestContext.WaitFrames(5);
            DismissWarnings();
            _t.Check("Longsword rebuilt: combat available again (toggle and acknowledgement kept)", _longswordA != null && _a.CombatEnabled);
        }

        private IEnumerator Sleep()
        {
            if (!HaugbuiPlugin.Instance.Settings.SleepFastMode.Value)
            {
                _t.Skip("sleep fast mode pauses combat and flight", "SleepFastMode is off in the config");
                yield break;
            }
            yield return EnsureOut(_a);
            Heal(_a);
            SetCombat(_a, true);
            TombController.SkippingForTests = true;
            Character greydwarf = Spawn("Greydwarf", _a.Creature.transform.position - _arena + new Vector3(-8, 0, 4));
            bool reacted = false;
            float end = Time.time + 3f;
            while (Time.time < end)
            {
                Provoke(greydwarf, _a.Creature != null ? _a.Creature.Character : null);
                reacted |= _a.Machine.State == HaugbuiState.Combat || _a.Machine.State == HaugbuiState.Fleeing || _a.Combat.Target != null;
                yield return null;
            }
            Hit(_a, 2f, greydwarf);
            end = Time.time + 1f;
            while (Time.time < end)
            {
                Provoke(greydwarf, _a.Creature != null ? _a.Creature.Character : null);
                reacted |= _a.Machine.State == HaugbuiState.Combat || _a.Machine.State == HaugbuiState.Fleeing;
                yield return null;
            }
            _t.Check("sleep fast mode: no combat or flight while skipping", !reacted, _a.Machine.State.ToString());
            TombController.SkippingForTests = false;
            yield return Hold(greydwarf, _a.Creature != null ? _a.Creature.Character : null, () => _a.Machine.State == HaugbuiState.Combat, 3f);
            _t.Check("combat resumes when the skip ends", _a.Machine.State == HaugbuiState.Combat, _a.Machine.State.ToString());
            yield return SelfTestContext.WaitFor(() => Gone(greydwarf), FightTimeout);
            Remove(greydwarf);
            yield return WaitCalm(_a);
        }

        private IEnumerator LevelSix()
        {
            var positions = new Dictionary<StructureKind, Vector3>
            {
                [StructureKind.Offering] = new Vector3(-3, 0, -3),
                [StructureKind.PersonalTreasure] = new Vector3(0, 0, -4),
                [StructureKind.SilverBracelets] = new Vector3(-3, 0, 3),
                [StructureKind.Altar] = new Vector3(3, 0, 2),
            };
            foreach (KeyValuePair<StructureKind, Vector3> upgrade in positions) _t.PlaceStructure(upgrade.Key, Ground(upgrade.Value));
            yield return SelfTestContext.WaitFrames(5);
            DismissWarnings();
            yield return new WaitForSeconds(1f);
            if (!_t.Check("all upgrades: level 6", _a.Attributes.Level == 6, _a.Attributes.Level.ToString())) yield break;
            yield return EnsureOut(_a);
            float baseMultiplier = HaugbuiPlugin.Instance.Settings.CombatDamageMultiplier.Value;
            _t.Check("level 6: hits ×2.0, damage taken ÷2.0",
                Mathf.Approximately(_a.Creature.DealtFactor, 2f * baseMultiplier) && Mathf.Approximately(_a.Creature.TakenFactor, 0.5f),
                $"dealt ×{_a.Creature.DealtFactor}, taken ×{_a.Creature.TakenFactor}");

            Heal(_a);
            SetCombat(_a, true);
            int hits = _a.Creature.HitsDealt;
            Character greydwarf = Spawn("Greydwarf", _a.Creature.transform.position - _arena + new Vector3(-8, 0, -4));
            yield return Hold(greydwarf, _a.Creature.Character, () => Gone(greydwarf), FightTimeout);
            _t.Check("level 6: kills a greydwarf, hit damage ×2.0",
                Gone(greydwarf) && _a.Creature.HitsDealt > hits && Ratio(_a.Creature.LastDealtScaled, _a.Creature.LastDealtBase, 2f * baseMultiplier),
                $"base {_a.Creature.LastDealtBase:0.##} → {_a.Creature.LastDealtScaled:0.##}");
            Remove(greydwarf);
            yield return WaitCalm(_a);

            SetCombat(_a, false);
            float lost = 0f;
            yield return MeasureTaken(_a, 10f, l => lost = l);
            _t.Check("level 6: a hit of 10 is halved",
                Ratio(_a.Creature.LastTakenScaled, _a.Creature.LastTakenIncoming, 0.5f),
                $"incoming {_a.Creature.LastTakenIncoming:0.##}, after scaling {_a.Creature.LastTakenScaled:0.##}, health lost {lost:0.##}");
            if (float.IsNaN(_takenLevel1) || _takenLevel1 <= 0f) _t.Skip("taken damage level 1 / level 6 = 2", "no level 1 measurement");
            else _t.Check("taken damage level 1 / level 6 = 2", Ratio(_takenLevel1, lost, 2f), $"{_takenLevel1:0.##} / {lost:0.##}");
            yield return WaitCalm(_a);
        }

        private IEnumerator Settle()
        {
            SetCombat(_a, false);
            SetCombat(_b, false);
            yield return SetTime(FakeTime.Noon);
            yield return SelfTestContext.WaitFor(() => _b.Machine.State == HaugbuiState.Dormant, 20f);
        }

        // --- helpers ---

        private void SetCombat(TombController tomb, bool on)
        {
            if (on) tomb.AcknowledgeCombat();
            tomb.SetToggle(TombToggles.Combat, on, out _);
        }

        private Character Spawn(string prefab, Vector3 offset)
        {
            GameObject go = _t.SpawnVanilla(prefab, Ground(offset) + Vector3.up * 0.2f, Quaternion.identity);
            if (go == null) return null;
            CharacterDrop drops = go.GetComponent<CharacterDrop>();
            if (drops != null) UnityEngine.Object.Destroy(drops);
            Character character = go.GetComponent<Character>();
            DisarmPieces(character);
            return character;
        }

        private static string Prefer(string first, string fallback) => ZNetScene.instance.GetPrefab(first) != null ? first : fallback;

        private static void Remove(Character character)
        {
            if (character == null) return;
            ZNetView view = character.GetComponent<ZNetView>();
            if (view != null && view.IsValid()) ZNetScene.instance.Destroy(character.gameObject);
        }

        private static bool Gone(Character character) => character == null || character.IsDead();

        private void Expect(string name, Character character, bool raid, TargetVerdict verdict, string note = "")
        {
            if (character == null)
            {
                _t.Fail(name, "not spawned");
                return;
            }
            TargetVerdict actual = _a.Combat.Judge(character, raid);
            _t.Check(name, actual == verdict, $"{character.m_name}: {actual}, faction {character.GetFaction()} {note}".TrimEnd());
        }

        /// <summary>The monster goes for <paramref name="target"/>, alerted and not fleeing. Re-applied each frame in
        /// wait loops because MonsterAI.UpdateTarget overwrites the target on its own timer.</summary>
        private static void Provoke(Character monster, Character target)
        {
            if (monster == null || target == null || !(monster.GetComponent<MonsterAI>() is MonsterAI ai)) return;
            ai.m_targetCreature = target;
            ai.m_targetStatic = null;
            ai.m_lastFlee = 0f;
            ai.m_attackPlayerObjects = false;
            if (!ai.IsAlerted()) ai.SetAlerted(true);
        }

        private static IEnumerator Hold(Character monster, Character target, Func<bool> done, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end && !done())
            {
                Provoke(monster, target);
                yield return null;
            }
        }

        /// <summary>Vanilla greydwarves smash nearby player pieces; the Longsword is one.</summary>
        private static void DisarmPieces(Character monster)
        {
            if (monster != null && monster.GetComponent<MonsterAI>() is MonsterAI ai) ai.m_attackPlayerObjects = false;
        }

        private static void Harden(Component component)
        {
            WearNTear wear = component != null ? component.GetComponent<WearNTear>() : null;
            if (wear != null) wear.m_health = 100000f;
        }

        private static void Siege(Character monster, Piece piece)
        {
            if (monster == null || piece == null || !(monster.GetComponent<MonsterAI>() is MonsterAI ai)) return;
            ai.m_targetCreature = null;
            ai.m_targetStatic = piece;
            ai.m_lastFlee = 0f;
        }

        private static void Calm(Character creature)
        {
            if (creature == null || !(creature.GetComponent<MonsterAI>() is MonsterAI ai)) return;
            ai.m_targetCreature = null;
            ai.m_targetStatic = null;
            ai.m_lastFlee = 0f;
            if (ai.IsAlerted()) ai.SetAlerted(false);
        }

        /// <summary>Holds a monster in place and makes it target the Haugbui; true while it does.</summary>
        private static bool Pin(Character monster, Vector3 at, TombController tomb)
        {
            if (monster == null || tomb.Creature == null) return false;
            monster.transform.position = at;
            Rigidbody body = monster.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = at;
                body.linearVelocity = Vector3.zero;
            }
            if (monster.GetComponent<MonsterAI>() is MonsterAI ai)
            {
                ai.m_targetCreature = tomb.Creature.Character;
                ai.m_attackPlayerObjects = false;
            }
            return CombatGateway.IsTargeting(monster, tomb.Creature.Character);
        }

        private static void Hit(TombController tomb, float damage, Character attacker)
        {
            if (tomb.Creature == null) return;
            var hit = new HitData();
            hit.m_damage.m_damage = damage;
            hit.m_point = tomb.Creature.transform.position;
            if (attacker != null) hit.SetAttacker(attacker);
            tomb.Creature.Character.Damage(hit);
        }

        private IEnumerator MeasureTaken(TombController tomb, float damage, Action<float> lost)
        {
            yield return EnsureOut(tomb);
            Heal(tomb);
            yield return null;
            float before = tomb.Creature.Character.GetHealth();
            Hit(tomb, damage, null);
            yield return SelfTestContext.WaitFrames(2);
            lost(tomb.Creature != null ? before - tomb.Creature.Character.GetHealth() : float.NaN);
            Heal(tomb);
        }

        private static void Heal(TombController tomb)
        {
            if (tomb.Creature != null) tomb.Creature.Character.SetHealth(tomb.Creature.Character.GetMaxHealth());
        }

        /// <summary>Back to Idle after a flight caused by the test's own hits.</summary>
        private static IEnumerator WaitCalm(TombController tomb) =>
            SelfTestContext.WaitFor(() => tomb.Machine.State != HaugbuiState.Fleeing && tomb.Machine.State != HaugbuiState.Combat,
                (float)ThreatTracker.MemorySeconds + 4f);

        /// <summary>A Haugbui killed by a test fight is brought back (cooldown and dusk skipped on the fake clock).</summary>
        private IEnumerator EnsureOut(TombController tomb)
        {
            if (tomb.Creature != null && tomb.Machine.IsOut) yield break;
            if (tomb.Machine.State == HaugbuiState.Dead)
            {
                _t.Fail("Haugbui alive before the next check", (tomb == _a ? "A" : "B") + " died in the previous check; brought back");
                _time.Offset += TombController.CooldownSeconds + 5;
                _time.ToNextDusk();
            }
            yield return SetTime(FakeTime.Night);
            yield return SelfTestContext.WaitFor(() => tomb.Machine.State == HaugbuiState.Idle, 6f);
            Heal(tomb);
        }

        private static void DismissWarnings()
        {
            for (int i = 0; i < 5 && UnifiedPopup.IsVisible() && CombatWarning.Pending == null; i++) UnifiedPopup.Pop();
        }

        private static bool Ratio(float value, float reference, float expected) =>
            reference > 0f && Mathf.Abs(value / reference - expected) <= 0.03f * expected;

        private static float DistanceToTomb(TombController tomb) =>
            tomb.Creature != null ? Vector3.Distance(tomb.Creature.transform.position, tomb.transform.position) : float.MaxValue;

        private int Delivered() => CountWood(_delivery != null ? _delivery.Container : null);

        private int WoodTotal() =>
            Delivered() + CountWood(_source) + _a.Carried.CountPrefab("Wood") + _b.Carried.CountPrefab("Wood")
            + CountWood(_a.GetComponent<Container>()) + CountWood(_b.GetComponent<Container>())
            + OverflowWood(_a) + OverflowWood(_b);

        private string WoodBreakdown() =>
            $"total {WoodTotal()} (delivery {Delivered()}, source {CountWood(_source)}, " +
            $"carried {_a.Carried.CountPrefab("Wood")}/{_b.Carried.CountPrefab("Wood")}, " +
            $"tomb {CountWood(_a.GetComponent<Container>())}/{CountWood(_b.GetComponent<Container>())}, " +
            $"overflow {OverflowWood(_a)}/{OverflowWood(_b)}, ground {GroundWood()})";

        private static int CountWood(Container container) =>
            container == null ? 0 : ItemBridge.CountPrefab(container.GetInventory(), "Wood");

        private static int OverflowWood(TombController tomb) =>
            tomb == null ? 0 : tomb.Overflow.Where(s => s.Prefab == "Wood").Sum(s => s.Amount);

        private static int GroundWood() =>
            ItemDrop.s_instances.Where(d => d != null && ItemBridge.PrefabName(d.m_itemData) == "Wood")
                .Sum(d => d.m_itemData.m_stack);

        private IEnumerator SetTime(float fraction)
        {
            _time.Fraction = fraction;
            _a.TickNow();
            _b.TickNow();
            yield return SelfTestContext.WaitFrames(2);
        }

        private Vector3 Ground(Vector3 offset) => SelfTestContext.OnGround(_arena + offset);
    }
}
#endif
