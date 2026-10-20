using System;
using System.Collections.Generic;
using Haugbui.Core.Combat;
using Haugbui.Core.Creature;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class TargetPolicyTests
    {
        private const float Radius = 50f;

        private static TargetFacts Creature(bool attacking) => new TargetFacts
        {
            Aggressor = attacking, EnemyOfTamed = true, Loaded = true, DistanceToTomb = 10f,
        };

        private static IEnumerable<TestCaseData> Matrix()
        {
            // name, facts, verdict outside a raid, verdict during a raid
            yield return new TestCaseData(Creature(true), TargetVerdict.Valid, TargetVerdict.Valid).SetName("greydwarf attacking");
            yield return new TestCaseData(Creature(false), TargetVerdict.NotAttacking, TargetVerdict.Valid).SetName("calm greydwarf");
            yield return new TestCaseData(Creature(false), TargetVerdict.NotAttacking, TargetVerdict.Valid).SetName("deer (cannot attack)");
            yield return new TestCaseData(Creature(true), TargetVerdict.Valid, TargetVerdict.Valid).SetName("wild boar attacking");
            TargetFacts boss = Creature(true);
            boss.Boss = true;
            yield return new TestCaseData(boss, TargetVerdict.Valid, TargetVerdict.Valid).SetName("boss attacking");
            TargetFacts calmBoss = Creature(false);
            calmBoss.Boss = true;
            yield return new TestCaseData(calmBoss, TargetVerdict.NotAttacking, TargetVerdict.Valid).SetName("calm boss");
            TargetFacts wolf = Creature(true);
            wolf.Tamed = true;
            yield return new TestCaseData(wolf, TargetVerdict.Tamed, TargetVerdict.Tamed).SetName("tamed wolf");
            TargetFacts player = Creature(true);
            player.IsPlayer = true;
            yield return new TestCaseData(player, TargetVerdict.Player, TargetVerdict.Player).SetName("player");
            TargetFacts haugbui = Creature(true);
            haugbui.IsHaugbui = true;
            haugbui.Tamed = true;
            yield return new TestCaseData(haugbui, TargetVerdict.Haugbui, TargetVerdict.Haugbui).SetName("another Haugbui");
            TargetFacts dvergr = Creature(false);
            dvergr.EnemyOfTamed = false;
            yield return new TestCaseData(dvergr, TargetVerdict.NotEnemy, TargetVerdict.NotEnemy).SetName("calm dvergr");
            yield return new TestCaseData(Creature(true), TargetVerdict.Valid, TargetVerdict.Valid).SetName("angry dvergr");
            TargetFacts dead = Creature(true);
            dead.Dead = true;
            yield return new TestCaseData(dead, TargetVerdict.Dead, TargetVerdict.Dead).SetName("dead");
            TargetFacts far = Creature(true);
            far.DistanceToTomb = Radius + 0.1f;
            yield return new TestCaseData(far, TargetVerdict.OutOfRadius, TargetVerdict.OutOfRadius).SetName("outside the radius");
            TargetFacts edge = Creature(true);
            edge.DistanceToTomb = Radius;
            yield return new TestCaseData(edge, TargetVerdict.Valid, TargetVerdict.Valid).SetName("on the radius");
            TargetFacts unloaded = Creature(true);
            unloaded.Loaded = false;
            yield return new TestCaseData(unloaded, TargetVerdict.NotLoaded, TargetVerdict.NotLoaded).SetName("unloaded zone");
            TargetFacts nan = Creature(true);
            nan.DistanceToTomb = float.NaN;
            yield return new TestCaseData(nan, TargetVerdict.OutOfRadius, TargetVerdict.OutOfRadius).SetName("unknown distance");
        }

        [TestCaseSource(nameof(Matrix))]
        public void Target_matrix(TargetFacts facts, TargetVerdict normal, TargetVerdict raid)
        {
            Assert.That(TargetPolicy.Judge(facts, Radius, raid: false), Is.EqualTo(normal), "outside a raid");
            Assert.That(TargetPolicy.Judge(facts, Radius, raid: true), Is.EqualTo(raid), "during a raid");
            Assert.That(TargetPolicy.IsValid(facts, Radius, false), Is.EqualTo(normal == TargetVerdict.Valid));
        }

        [TestCase(150f, 96f, 50f, false)]
        [TestCase(145f, 96f, 50f, true)]
        [TestCase(0f, 96f, 50f, true)]
        public void Raid_reaches_the_radius_when_the_areas_overlap(float distance, float range, float radius, bool expected)
        {
            Assert.That(TargetPolicy.RaidReaches(distance, range, radius), Is.EqualTo(expected));
        }
    }

    public class CombatMathTests
    {
        [TestCase(1.0f, 1.0f, 1.0f, 1.0f)]
        [TestCase(1.4f, 1.0f, 1.4f, 1f / 1.4f)]
        [TestCase(2.0f, 1.0f, 2.0f, 0.5f)]
        [TestCase(2.0f, 1.5f, 3.0f, 0.5f)]
        public void Dealt_is_multiplied_and_taken_divided(float level, float baseMultiplier, float dealt, float taken)
        {
            Assert.That(CombatMath.DealtFactor(level, baseMultiplier), Is.EqualTo(dealt).Within(1e-5));
            Assert.That(CombatMath.TakenFactor(level), Is.EqualTo(taken).Within(1e-5));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Invalid_multipliers_count_as_one(float bad)
        {
            Assert.That(CombatMath.DealtFactor(bad, bad), Is.EqualTo(1f));
            Assert.That(CombatMath.TakenFactor(bad), Is.EqualTo(1f));
        }

        [Test]
        public void Level_six_takes_half_and_deals_double()
        {
            float multiplier = Progression.ProgressionTable.Default.ForLevel(6).CombatMultiplier;
            Assert.That(10f * CombatMath.DealtFactor(multiplier, 1f), Is.EqualTo(20f).Within(1e-4));
            Assert.That(10f * CombatMath.TakenFactor(multiplier), Is.EqualTo(5f).Within(1e-4));
        }
    }

    public class CombatRulesTests
    {
        private static CombatInput In(HaugbuiState state, bool enabled = true, bool target = false, bool threatened = false,
            bool paused = false, bool returning = false) => new CombatInput
        {
            State = state, Enabled = enabled, HasTarget = target, Threatened = threatened, Paused = paused, ReturnPending = returning,
        };

        [TestCase(HaugbuiState.Idle)]
        [TestCase(HaugbuiState.Traveling)]
        public void A_target_interrupts_work_when_combat_is_on(HaugbuiState state)
        {
            Assert.That(CombatRules.Decide(In(state, target: true)), Is.EqualTo(CombatAction.Engage));
            Assert.That(CombatRules.Decide(In(state, enabled: false, target: true)), Is.EqualTo(CombatAction.None), "combat off never attacks");
        }

        [Test]
        public void Combat_off_flees_only_when_hurt()
        {
            Assert.That(CombatRules.Decide(In(HaugbuiState.Traveling, enabled: false, target: true, threatened: false)), Is.EqualTo(CombatAction.None),
                "being targeted is not being hurt");
            Assert.That(CombatRules.Decide(In(HaugbuiState.Traveling, enabled: false, threatened: true)), Is.EqualTo(CombatAction.Flee));
            Assert.That(CombatRules.Decide(In(HaugbuiState.Fleeing, enabled: false, threatened: true)), Is.EqualTo(CombatAction.None));
            Assert.That(CombatRules.Decide(In(HaugbuiState.Fleeing, enabled: false, threatened: false)), Is.EqualTo(CombatAction.StopFleeing));
        }

        [Test]
        public void Hurt_without_a_valid_target_flees_even_with_combat_on()
        {
            Assert.That(CombatRules.Decide(In(HaugbuiState.Idle, threatened: true)), Is.EqualTo(CombatAction.Flee));
            Assert.That(CombatRules.Decide(In(HaugbuiState.Fleeing, target: true, threatened: true)), Is.EqualTo(CombatAction.Engage));
        }

        [Test]
        public void A_fight_ends_at_once_without_target_toggle_longsword_or_when_going_home()
        {
            Assert.That(CombatRules.Decide(In(HaugbuiState.Combat, target: true)), Is.EqualTo(CombatAction.None));
            Assert.That(CombatRules.Decide(In(HaugbuiState.Combat, target: false)), Is.EqualTo(CombatAction.Disengage));
            Assert.That(CombatRules.Decide(In(HaugbuiState.Combat, enabled: false, target: true)), Is.EqualTo(CombatAction.Disengage));
            Assert.That(CombatRules.Decide(In(HaugbuiState.Combat, target: true, returning: true)), Is.EqualTo(CombatAction.Disengage));
        }

        [Test]
        public void No_new_fight_or_flight_once_a_return_is_pending()
        {
            Assert.That(CombatRules.Decide(In(HaugbuiState.Idle, target: true, threatened: true, returning: true)), Is.EqualTo(CombatAction.None));
            Assert.That(CombatRules.Decide(In(HaugbuiState.Fleeing, target: true, threatened: true, returning: true)), Is.EqualTo(CombatAction.None));
            foreach (HaugbuiState state in new[] { HaugbuiState.Returning, HaugbuiState.Entering, HaugbuiState.Emerging, HaugbuiState.Interacting })
                Assert.That(CombatRules.Decide(In(state, target: true, threatened: true)), Is.EqualTo(CombatAction.None), state.ToString());
        }

        [Test]
        public void Sleep_fast_mode_pauses_combat_and_flight()
        {
            Assert.That(CombatRules.Decide(In(HaugbuiState.Idle, target: true, threatened: true, paused: true)), Is.EqualTo(CombatAction.None));
            Assert.That(CombatRules.Decide(In(HaugbuiState.Combat, target: true, paused: true)), Is.EqualTo(CombatAction.Disengage));
            Assert.That(CombatRules.Decide(In(HaugbuiState.Fleeing, threatened: true, paused: true)), Is.EqualTo(CombatAction.StopFleeing));
        }

        [Test]
        public void Every_decision_is_a_legal_transition()
        {
            foreach (HaugbuiState state in Enum.GetValues(typeof(HaugbuiState)))
            foreach (bool enabled in new[] { false, true })
            foreach (bool target in new[] { false, true })
            foreach (bool threatened in new[] { false, true })
            foreach (bool paused in new[] { false, true })
            {
                CombatAction action = CombatRules.Decide(In(state, enabled, target, threatened, paused));
                HaugbuiState? to = action switch
                {
                    CombatAction.Engage => HaugbuiState.Combat,
                    CombatAction.Flee => HaugbuiState.Fleeing,
                    CombatAction.Disengage => HaugbuiState.Idle,
                    CombatAction.StopFleeing => HaugbuiState.Idle,
                    _ => null,
                };
                if (to != null)
                    Assert.That(HaugbuiStateMachine.CanTransition(state, to.Value), Is.True, $"{state} -> {to} ({action})");
            }
        }
    }

    public class ThreatTrackerTests
    {
        [Test]
        public void Never_threatened_without_damage()
        {
            var tracker = new ThreatTracker();
            Assert.That(tracker.Threatened(100, _ => AttackerStatus.TargetingIt), Is.False);
        }

        [Test]
        public void Damage_without_attacker_keeps_it_away_for_the_calm_time()
        {
            var tracker = new ThreatTracker();
            tracker.Damaged(100, null);
            Assert.That(tracker.Threatened(100 + ThreatTracker.CalmSeconds - 0.1, _ => AttackerStatus.Gone), Is.True);
            Assert.That(tracker.Threatened(100 + ThreatTracker.CalmSeconds + 0.1, _ => AttackerStatus.Gone), Is.False);
        }

        [Test]
        public void An_attacker_remains_while_targeting_it_or_for_a_while_after_its_last_hit()
        {
            var tracker = new ThreatTracker();
            var status = new Dictionary<string, AttackerStatus> { ["greydwarf"] = AttackerStatus.TargetingIt };
            tracker.Damaged(100, "greydwarf");
            Assert.That(tracker.Threatened(160, id => status[id]), Is.True, "still chasing it");

            status["greydwarf"] = AttackerStatus.Present;
            tracker.Damaged(200, "greydwarf");
            Assert.That(tracker.Threatened(200 + ThreatTracker.MemorySeconds - 1, id => status[id]), Is.True);
            Assert.That(tracker.Threatened(200 + ThreatTracker.MemorySeconds + 1, id => status[id]), Is.False);
            Assert.That(tracker.Attackers, Is.EqualTo(0), "forgotten");
        }

        [Test]
        public void A_dead_or_departed_attacker_ends_the_threat_after_the_calm_time()
        {
            var tracker = new ThreatTracker();
            tracker.Damaged(100, "skeleton");
            Assert.That(tracker.Threatened(101, _ => AttackerStatus.Gone), Is.True, "calm time");
            Assert.That(tracker.Attackers, Is.EqualTo(0));
            Assert.That(tracker.Threatened(100 + ThreatTracker.CalmSeconds + 0.1, _ => AttackerStatus.TargetingIt), Is.False);
        }

        [Test]
        public void Clear_forgets_everything()
        {
            var tracker = new ThreatTracker();
            tracker.Damaged(100, "a");
            tracker.Clear();
            Assert.That(tracker.Threatened(100, _ => AttackerStatus.TargetingIt), Is.False);
        }
    }
}
