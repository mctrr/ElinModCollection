namespace Wetness;

internal static class WetnessExt
{
    /// <summary>
    /// 取某个角色的湿润度。
    /// 首次访问时把空槽位初始化为默认值 —— 旧存档里这些索引是 0，
    /// 若不初始化会被直接判成「脱水症」。
    /// </summary>
    public static Stats wetness(this Chara c)
    {
        // 第一次取用时补上源表行（Tip 与颜色都依赖它），见 StatsWetness.EnsureSource。
        StatsWetness.EnsureSource();

        if (c._cints[WetnessDef.SlotInit] == 0)
        {
            MigrateFromOldSlots(c);
            c._cints[WetnessDef.SlotValue] = WetnessDef.Initial;
            c._cints[WetnessDef.SlotAccum] = 0;
            c._cints[WetnessDef.SlotInit] = 1;
        }
        return StatsWetness.Instance.Set(c._cints, WetnessDef.SlotValue, c);
    }

    /// <summary>
    /// 擦掉旧槽位（22-25）里残留的数据 —— v0.11.0 及更早的存档才会带上。
    ///
    /// 那几个数是「当时的湿润度 / 初始化标记」，留着不会被任何人读，
    /// 但哪天原版把 22 分给新状态，一个残留的 60 就会被当成那个状态的值读出来 ——
    /// 这正是搬槽位本来要躲开的事，所以顺手清干净。
    ///
    /// 只在「旧标记亮着、新标记还没亮」这一次真正迁移时动手；
    /// 擦完把旧标记也清掉，从此再不碰 22-25。
    /// </summary>
    private static void MigrateFromOldSlots(Chara c)
    {
        const int OldInit = 24;
        if (c._cints[OldInit] == 0)
        {
            return;
        }
        for (int i = 22; i <= 25; i++)
        {
            c._cints[i] = 0;
        }
    }

    /// <summary>是否对这名角色追踪湿润度。目前只覆盖玩家阵营。</summary>
    public static bool TracksWetness(this Chara c)
    {
        return c != null && c.IsPCFaction;
    }
}
