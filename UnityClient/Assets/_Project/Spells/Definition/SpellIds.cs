// §26 SpellId 규칙: S01~S13 → 1~13, H01~H06 → 101~106

namespace SpellboundVR.Spells
{
    public static class SpellIds
    {
        public const int None = 0;

        public const int S01_Smoke = 1;
        public const int S02_PoisonField = 2;
        public const int S03_Fireball = 3;
        public const int S04_ArrowRain = 4;
        public const int S05_Tornado = 5;
        public const int S06_Lightning = 6;
        public const int S07_Gatling = 7;
        public const int S08_Shield = 8;
        public const int S09_TowerBuff = 9;
        public const int S10_Focus = 10;
        public const int S11_AxeWarrior = 11;
        public const int S12_Mage = 12;
        public const int S13_Turret = 13;

        public const int H01_Freeze = 101;
        public const int H02_Laser = 102;
        public const int H03_Drain = 103;
        public const int H04_ReflectBarrier = 104;
        public const int H05_Rage = 105;
        public const int H06_SummonTower = 106;

        public static bool IsHighPowerId(int spellId) => spellId > 100;

        /// <summary>3 → "S03", 102 → "H02"</summary>
        public static string Code(int spellId)
        {
            if (spellId <= 0) return "---";
            if (spellId > 100) return "H" + (spellId - 100).ToString("00");
            return "S" + spellId.ToString("00");
        }
    }
}
