using HarmonyLib;

namespace Wetness.Patches;

/// <summary>
/// 把 TraitWaterskin 挂到水囊上。
///
/// 为什么还要这一层：
/// 源表 trait 列那条路是 Card.ApplyTrait -> ClassCache.Create&lt;Trait&gt;("Trait"+名字, "Elin")。
/// 它先试 Type.GetType(名字 + ", Elin")，失败后回退到 LoadTypeFromGlobalNamespace，
/// 那个回退只遍历 ClassCache.assemblies 里已登记的程序集 ——
/// 而登记与否取决于游戏自己的插件枚举（Core.StartCore 里那一段），
/// 对 BepInEx 加载的 dll 不保证。这里补一次赋值，确定性最高。
///
/// 顺带一个好处：ApplyTrait 在读档建卡时也会跑（Card.cs 的 OnDeserialized 分支），
/// 所以存档里已有的旧水囊读回来也会被正确接管。
///
/// 源表里仍然保留 trait 列，那是给人看的声明；这里只是兜底。
/// </summary>
// 类级 [HarmonyPatch] 必需：缺了它整个类会被 HarmonyX 静默跳过（见 WetnessDrinkPatch 注释）。
[HarmonyPatch]
internal static class WetnessWaterskinPatch
{
    private static bool _reported;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Card), nameof(Card.ApplyTrait))]
    private static void ApplyTraitPostfix(Card __instance)
    {
        // sourceCard 的 id 覆盖所有卡片类型，用它可以安全地和角色卡区分开。
        if (__instance.sourceCard == null || __instance.sourceCard.id != WetnessDef.WaterskinId)
        {
            return;
        }

        // 声明式那条路成功的话，这里什么都不用做。
        if (__instance.trait is TraitWaterskin)
        {
            return;
        }

        if (!_reported)
        {
            _reported = true;
            Plugin.LogInfo($"[Waterskin] 源表 trait 未解析出 TraitWaterskin（实得 {__instance.trait?.GetType().Name ?? "null"}），改由补丁接管。");
        }

        __instance.trait = new TraitWaterskin();
        __instance.trait.SetOwner(__instance);
    }
}
