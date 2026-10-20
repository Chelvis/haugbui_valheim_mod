namespace Haugbui.Core.Combat
{
    /// <summary>What the game says about one character, read once per scan (HB-141).</summary>
    public struct TargetFacts
    {
        public bool Dead;
        public bool IsPlayer;
        public bool Tamed;
        public bool IsHaugbui;

        /// <summary>
        /// It is about to attack or attacking a player, a Haugbui or a player-built piece, and not fleeing: monster AI
        /// with such a target, or it hurt a Haugbui recently. Creatures without monster AI never attack.
        /// </summary>
        public bool Aggressor;

        public bool Boss;

        /// <summary>The game lets a tamed creature hit it (BaseAI.IsEnemy); false for Dvergr that are not angry.</summary>
        public bool EnemyOfTamed;

        public bool Loaded;
        public float DistanceToTomb;
    }

    public enum TargetVerdict
    {
        Valid,
        Dead,
        Player,
        Tamed,
        Haugbui,
        NotEnemy,
        NotLoaded,
        OutOfRadius,
        NotAttacking
    }

    /// <summary>
    /// HB-141 (decision 2026-10-16): outside a raid only creatures attacking a player, a Haugbui or the base are
    /// targets; during a raid that reaches the operational radius every creature that is not tamed is. Bosses follow the
    /// same rule (no exclusion).
    /// </summary>
    public static class TargetPolicy
    {
        public static TargetVerdict Judge(in TargetFacts facts, float radius, bool raid)
        {
            if (facts.Dead) return TargetVerdict.Dead;
            if (facts.IsPlayer) return TargetVerdict.Player;
            if (facts.IsHaugbui) return TargetVerdict.Haugbui;
            if (facts.Tamed) return TargetVerdict.Tamed;
            if (!facts.EnemyOfTamed) return TargetVerdict.NotEnemy;
            if (!facts.Loaded) return TargetVerdict.NotLoaded;
            if (!(facts.DistanceToTomb <= radius)) return TargetVerdict.OutOfRadius;
            if (!raid && !facts.Aggressor) return TargetVerdict.NotAttacking;
            return TargetVerdict.Valid;
        }

        public static bool IsValid(in TargetFacts facts, float radius, bool raid) => Judge(facts, radius, raid) == TargetVerdict.Valid;

        /// <summary>A raid reaches the tomb's operational radius when the two areas overlap (horizontal distance).</summary>
        public static bool RaidReaches(float distanceXZ, float eventRange, float radius) => distanceXZ < eventRange + radius;
    }
}
