using System;
using HarmonyLib;

namespace ACS;

/// <summary>
/// 探测 23.359.0 才有的 SpriteData API。
///
/// BCS 2.6.0 直接引用 <c>CardActor.spriteData</c>、<c>SpriteReplacer.dictOverrides</c>
/// 与 <c>Chara.GetSpriteData</c>。旧本体上这些符号不存在（<c>spriteTimer</c> /
/// <c>spriteIndex</c> 则还是 private），JIT 第一次执行到就抛 <c>MissingFieldException</c>。
/// 抛点正好落在读档 / <c>OnEnterScreen</c> 路径上，玩家看到的是「进不去存档，
/// 去掉这个模组才能进」。
///
/// 因此所有新 API 的访问都必须先过这道门，并且真正的字段访问要放在单独的方法里：
/// JIT 按方法懒编译，旧本体永远不会编译到那个方法，也就不会解析到缺失的字段。
/// 只在同一方法里提前 return 是不够的 —— 方法体的字段引用在 JIT 时就解析了。
/// </summary>
internal static class ApiProbe
{
    /// <summary>本体是否提供 23.359.0 的 SpriteData API（含字段可见性）。</summary>
    internal static readonly bool HasSpriteDataApi = Probe();

    private static bool Probe()
    {
        try {
            return IsPublicField(typeof(CardActor), "spriteData")
                && IsPublicField(typeof(CardActor), "spriteTimer")
                && IsPublicField(typeof(CardActor), "spriteIndex")
                && AccessTools.Field(typeof(SpriteReplacer), "dictOverrides") is not null
                && AccessTools.Method(typeof(Chara), "GetSpriteData") is not null;
        }
        catch (Exception) {
            // 探测本身失败按「没有」处理：少功能好过崩溃。
            return false;
        }
    }

    /// <summary>
    /// 必须同时判断 public：旧本体的 <c>spriteTimer</c> / <c>spriteIndex</c> 字段是存在的，
    /// 只是 private，直接访问会抛 <c>FieldAccessException</c>。
    /// </summary>
    private static bool IsPublicField(Type type, string name)
    {
        var field = AccessTools.Field(type, name);
        return field is not null && field.IsPublic;
    }
}
