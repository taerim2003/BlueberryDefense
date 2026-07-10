public static class LightningStorm
{
    public static float ActiveUntil;
    public const float BaseProcChance = 0.6f;
    public static float ProcChance = BaseProcChance;
    public static float ProcDamage = 15f;
    public static bool RecursiveProcEnabled;

    // 체인 라이트닝 (힘 연계, path1): 첫 낙뢰 피격 시 주변 적에게 전이
    public static bool ChainEnabled;
    public const float ChainRadius = 4f;
    public const int ChainCount = 3;
}
