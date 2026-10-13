namespace Haugbui.Core.Creature
{
    /// <summary>Mirror of ItemDrop.ItemData.ItemType (Valheim 1.0.17); the values are the game's.</summary>
    public enum GameItemType
    {
        None = 0,
        Material = 1,
        Consumable = 2,
        OneHandedWeapon = 3,
        Bow = 4,
        Shield = 5,
        Helmet = 6,
        Chest = 7,
        Ammo = 9,
        Customization = 10,
        Legs = 11,
        Hands = 12,
        Trophy = 13,
        TwoHandedWeapon = 14,
        Torch = 15,
        Misc = 16,
        Shoulder = 17,
        Utility = 18,
        Tool = 19,
        Attach_Atgeir = 20,
        Fish = 21,
        TwoHandedWeaponLeft = 22,
        AmmoNonEquipable = 23,
        Trinket = 24
    }

    /// <summary>HB-060/061: the Haugbui moves resources, never gear.</summary>
    public static class ItemRules
    {
        public static bool IsTransportable(GameItemType type, bool equipable)
        {
            if (equipable) return false;
            switch (type)
            {
                case GameItemType.Material:
                case GameItemType.Consumable:
                case GameItemType.Trophy:
                case GameItemType.Misc:
                case GameItemType.Fish:
                case GameItemType.AmmoNonEquipable:
                    return true;
                default:
                    return false;
            }
        }
    }
}
