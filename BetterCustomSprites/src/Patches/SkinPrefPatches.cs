using System;
using HarmonyLib;

namespace ACS.Patches;

/// <summary>
/// 皮肤级 .pref 支持（决策 2026-09-07：@N 抢读 = 密度只认文件名；作用对象 NPC+PC 全部）。
///
/// ① SpriteData.LoadPref postfix —— 剥 scaleTex：
///    ACS 路径读入的 .pref 一律清掉 scaleTex（ints[25]=0 → ScaleTex 恒 1f），密度只认文件名 @N
///    （缺省 100）。vanilla 所有懒加载点（Chara.Pref、Core refocus、Load）都汇入 LoadPref，
///    剥完的 pref 留存在 data.pref 上 → LoadPrefAndFixDensity 的密度比较恒等，
///    永不按 .pref scaleTex 重切。.pref 其余键（shadow/x/y/z/pivot/hatY 等）保留，
///    经 vanilla Chara.Pref → 皮肤级阴影/位移/头像随皮肤走。动态底图皮（无 _acs_）不受影响。
///
/// ② Chara.Pref getter postfix —— 原皮链兜底：
///    绑了自定义皮肤但皮肤 data 无 .pref 时，vanilla 返回 refs.prefs.replacer1（空白默认），
///    把原角色行 pref（usePref 大影子等，原皮链 = sourceCard.replacer.data?.pref ?? sourceCard.pref）
///    整个跳过。改为：皮肤无 .pref → 回落原皮链结果（仍无则维持 replacer1，与 vanilla 一致）。
/// </summary>
[HarmonyPatch]
internal static class SkinPrefPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpriteData), nameof(SpriteData.LoadPref))]
    internal static void StripAcsScaleTexOnLoad(SpriteData __instance)
    {
        try {
            if (__instance.path.IsEmpty()
                || __instance.path.IndexOf("_acs_", StringComparison.Ordinal) < 0) {
                return;
            }

            var pref = __instance.pref;
            if (pref is null) {
                return;
            }

            if (pref.ints is null) {
                pref.ints = new int[26];
                return;
            }

            if (pref.ints.Length <= 25) {
                var grown = new int[26];
                Array.Copy(pref.ints, grown, pref.ints.Length);
                pref.ints = grown;
            }

            // scaleTex 键（nightly 23.343.1 ints[25]）：剥掉 → ScaleTex=1f → 永不按 .pref 密度重切
            pref.ints[25] = 0;
        }
        catch (Exception) {
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Pref), MethodType.Getter)]
    internal static void FallbackToSourceRowPref(Chara __instance, ref SourcePref __result)
    {
        try {
            // 只处理「绑了自定义皮 + 皮肤 data 无 .pref」：vanilla 该分支返回 replacer1 空白默认。
            if (__result is null
                || __instance.spriteReplacer is null
                || __instance.spriteReplacer.data?.pref is not null) {
                return;
            }

            var sc = __instance.sourceCard;
            if (sc is null) {
                return;
            }

            // 原皮链（= vanilla 无自定义皮时的 Chara.Pref 兜底）：
            // sourceCard.replacer.data?.pref ?? sourceCard.pref —— usePref 大影子灌在这里。
            SourcePref? rowPref = sc.replacer?.data?.pref ?? sc.pref;
            if (rowPref is not null) {
                __result = rowPref;
            }
        }
        catch (Exception) {
        }
    }
}
