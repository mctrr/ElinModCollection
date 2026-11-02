using System;
using System.Collections.Generic;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// PCC 上 ACS/静态 skin 的贴图呈现：只做「作者声明值 → 世界倍率 / 几何位置」的换算。
/// 不读像素、不按画面内容修正摆放——帧底留白之类的素材问题由作者改图，BCS 不代为对齐。
/// </summary>
internal static class AcsSpritePresenter
{
    /// <summary>
    /// PC 专属的额外垂直偏移（世界单位），默认 0 = <b>与 NPC 完全一致</b>。
    /// BCS 不再对 PC 做任何自动抬升/补偿，落点就是原版 <c>CardActor.OnRender</c> 写好的位置；
    /// 只有在素材的 <c>.pref</c> 也调不好站姿时，才用这个值手动加减（正数上抬、负数下沉）。
    /// </summary>
    internal static float PcYAdjust;

    /// <summary>
    /// 将 SpriteData.scale 映射为 PCC 叠加用的世界倍率。
    /// 只看 scale 本身，不掺 frame 推断，也不做跨 clip 归一。
    /// 两套约定（判据是声明值本身，不是猜）：
    /// - 原版字段默认值 = 50（`public int scale = 50`；`.ini` 不存在、或 ini 里没有 scale 键时
    ///   LoadAnimationIni 也落回 50）→ 50 必须映射成 1.0，否则所有未声明缩放的皮肤都缩一半。
    /// - 作者显式声明时按 100 = 1.0（配套导出脚本写的就是 `scale=100`；BCS 给 ACS 固定写 100）。
    /// </summary>
    internal static float ResolveWorldScale(SpriteData data)
    {
        if (data is null || data.scale <= 0) {
            return 1f;
        }

        // 旧判据是 `data.frame > 1 || data.scale >= 100`。那个 `frame > 1` 会让
        // 「ini 写了 frame 但没写 scale」的皮肤（LoadAnimationIni 把 scale 落回默认 50、frame=N）
        // 被当成声明值 100 用 → 直接缩成一半。
        return data.scale >= 100 ? data.scale / 100f : data.scale / 50f;
    }

    /// <summary>仅作者 scale → 世界倍率；不跨 clip 压身高。</summary>
    internal static float ComputeLocalScaleMultiplier(Chara? owner, SpriteData data)
    {
        _ = owner;
        return ResolveWorldScale(data);
    }
    /// <summary>
    /// PC 叠 ACS / 静态整图时，PC 与 NPC 走的是两套原版定位数据，同一张图会固定差一个 Y：
    /// <list type="bullet">
    /// <item>NPC：<c>CardRenderer.GetPref()</c> → <c>Chara.Pref</c>（皮肤 pref），
    /// <c>renderer.data</c> → 种族 <c>sourceCard.renderData</c>（<c>BindNpcCustomSkin</c> 用的就是这支）；</item>
    /// <item>PC：<c>GetPref()</c> → <c>refs.prefs.pcc</c>，<c>renderer.data</c> → <c>refs.renderers.pcc</c>。</item>
    /// </list>
    /// 两套相减就是 PC 需要补的 Y。数值全部取自原版自身，没有硬编码偏移。
    /// </summary>
    internal static float ResolveNpcAlignmentDy(Chara owner, RenderData? pccData, int dir)
    {
        try {
            var racePref = owner.Pref;
            var pccPref = EClass.core?.refs?.prefs?.pcc;
            if (pccData is null || racePref is null || pccPref is null) {
                return 0f;
            }

            float npcY = racePref.y + ResolveCharaAnchorY(owner, dir);
            float pcY = pccPref.y + OffsetY(pccData, dir);
            float dy = npcY - pcY;
            LogAlignment(owner, dy, npcY, pcY, pccData);
            return dy;
        }
        catch (Exception) {
            return 0f;
        }
    }

    /// <summary>原版通用角色 <c>chara</c> RenderData 的资源路径（与 <c>RenderRow.pathRenderData</c> 同源）。</summary>
    private const string CharaBaseRenderDataPath = CorePath.RenderData + "chara";
    /// <summary>
    /// 原版「普通角色」落点用的 RenderData 的 offset.y —— NPC 带同一张皮肤时用的就是它。
    /// <para>
    /// 普通角色的 sprite pivot 是 (0,0)（<c>RenderRow.GetSprite</c>），所以
    /// <c>chara</c> 的 offset 是「脚底锚点」；而皮肤 / ACS 整图是中心 pivot
    /// （<c>SpriteData.LoadSprites</c> 固定 (0.5,0.5)）。原版 <c>RenderRow.SetRenderData()</c>
    /// 的 <c>@</c> 分支为此把 offset 补上 mesh 高的一半：实测
    /// <c>chara</c>=(0.00,0.30) → 换出来的 <c>custom_chara</c>=(0.64,0.94)，y 正好 +0.64 = 1.28/2。
    /// 这里照同一套算法取通用角色那一支，数值全部来自原版资源。
    /// </para>
    /// PC 的种族行指向 <c>pcc</c>（PCC 合成图专用锚点，offset.y=0.4），拿它自己比自己永远是 0，
    /// 所以 PC 必须改用这一支；非 PC 角色的种族行本就跟 NPC 同源，直接用。
    /// </summary>
    private static float ResolveCharaAnchorY(Chara owner, int dir)
    {
        var race = owner.source?.renderData;
        if (race is not null && race is not RenderDataPcc) {
            return OffsetY(race, dir);
        }

        var baseData = ResourceCache.Load<RenderData>(CharaBaseRenderDataPath);
        if (baseData is null) {
            return 0f;
        }

        var pass = baseData.pass;
        float center = pass is not null ? pass.pmesh.size.y * 0.5f : 0f;
        return OffsetY(baseData, dir) + center;
    }

    /// <summary>
    /// 原版 <c>RenderRow.SetRenderData()</c> 的 <c>@</c> 分支的等价物 —— 把「脚底锚点」形态的
    /// 种族 RenderData 换成中心锚点版本。换贴图的角色必须有这一跳，否则会沉底半个身高：
    /// <list type="bullet">
    /// <item>原版普通角色 sprite 是底左 pivot（<c>RenderRow.GetSprite</c> 用 <c>Vector2.zero</c>），
    /// 所以 <c>chara</c> 的 offset 是「脚底」；</item>
    /// <item>皮肤 / ACS 整图是中心 pivot（<c>SpriteData.LoadSprites</c>），整张图会往下掉半个身高
    /// （实测 128px @ PPU 100 = 1.28 世界高 → 0.64）。</item>
    /// </list>
    /// 原版只给 <c>@xxx</c> 行做补偿（<c>offset += pass.pmesh.size * 0.5</c>，随后 <c>pass = null</c>）：
    /// mod 角色行的 render data 写 <c>@chara</c> → 实测 offset.y 0.94 = 0.30 + 0.64，正常；
    /// 原版角色行写的是 <c>chara</c> → offset.y 0.30，没有这一跳，于是沉底。
    /// 这里按同一套算法补上，数值全部取自该 RenderData 自己的 pass mesh，无硬编码。
    /// <para>
    /// 已经是 <c>@</c> 形态（<c>pass == null</c>，如 <c>custom_chara</c> / <c>chara_custom_*</c>）
    /// 或 PC 的 <see cref="RenderDataPcc"/> 一律原样返回。
    /// </para>
    /// </summary>
    internal static RenderData? ResolveCenterAnchoredRenderData(RenderData? race)
    {
        if (race is null || race is RenderDataPcc || race.pass is null || race.pass.pmesh is null) {
            return race;
        }

        if (CenterAnchored.TryGetValue(race, out var cached) && cached is not null) {
            return cached;
        }

        var pass = race.pass;
        var clone = UnityEngine.Object.Instantiate(race);
        clone.offset.x += pass.pmesh.size.x * 0.5f;
        clone.offset.y += pass.pmesh.size.y * 0.5f;
        clone.pass = null;
        clone.name = race.name;   // 诊断日志里 data=chara 保持一致，便于逐角色对比

        CenterAnchored[race] = clone;
        return clone;
    }

    /// <summary>原种族 RenderData → 已补偿副本。按资产缓存，避免每次进屏都新建一个 ScriptableObject。</summary>
    private static readonly Dictionary<RenderData, RenderData> CenterAnchored = new Dictionary<RenderData, RenderData>();

    /// <summary>切图 / 新游戏：渲染器整体重建，补偿副本作废。</summary>
    internal static void ClearCenterAnchoredCache()
    {
        CenterAnchored.Clear();
        LyingActors.Clear();
    }

    /// <summary>被本类旋转成「躺姿」的 actor（InstanceID）——只有这些需要还原 localRotation。</summary>
    private static readonly HashSet<int> LyingActors = new HashSet<int>();

    /// <summary>
    /// 躺姿相对<b>站立基准</b>的纵向偏移（世界单位）。负数 = 下沉，<c>-0.49</c> 即整图下移 0.49 格。
    ///
    /// 标定依据：相机 orthographicSize 2.7、屏高 1080 → 1080 / 2 / 2.7 = <b>200 px 每个世界单位</b>，
    /// 所以 1920×1080 实机截图上「抬 10 像素」正好是 0.05 格。0.54 下沉时人还压在地板下，
    /// 抬到 0.49 就是「半身躺在地面上」。
    ///
    /// 这是<b>整张中心 pivot 立绘</b>的落点，PC 与 NPC 共用。别再往它上面叠
    /// subDead / subDeadPCC 的 offset.y（NPC 0.82 / 主角 0.06，互不相同，是给多部件 mesh 的）。
    /// </summary>
    private const float LyingDropY = -0.49f;

    /// <summary>
    /// 睡觉 / 死亡的「横躺」姿态。
    ///
    /// 原版让角色躺下只有一条路：<c>CardRenderer.Draw</c> 在 actor 渲染前把
    /// <c>SubPassData.Current</c> 换成 <c>subDeadPCC</c>（主角）或 <c>subDead</c>（NPC）——
    /// 实测两者都是 <b>Z 轴旋转 270°</b>，另带一份 offset（主角 (-0.3, 0.06)、NPC (0.2, 0.82)）。
    /// 那份 offset 是给 PCC 多部件 mesh 调的；ACS 是整张中心 pivot 立绘，照搬的结果就是
    /// 「横躺 + 往左挪 0.3 格 + 离地悬浮」，也就是玩家看到的浮在左边空中。
    ///
    /// 这里按<b>整图</b>重算落点。原版那两份 offset 是给 PCC 多部件 mesh 调的，整张中心 pivot
    /// 立绘照搬只会得到「横躺 + 左挪 + 悬空」，所以<b>只取 rotation</b>（这才是「躺」），位置全部重算：
    /// <list type="number">
    /// <item>横向回到站立基准，不再跟 offset.x（主角 -0.3 会让整张图偏到左边一格）；</item>
    /// <item>纵向按 <see cref="LyingDropY"/> 这条整图标定值下沉，PC / NPC 共用同一个落点；</item>
    /// <item>NPC 原版从不写 localRotation，躺姿的旋转由这里补上。</item>
    /// </list>
    ///
    /// <b>只能在 OnRender 之后调</b>：每帧原版都会重写 position，从它算基准才不会累积漂移。
    /// 别挂到 RefreshSprite / NextDir 上 —— 那两条路径不重算 position，会重复减 offset。
    /// </summary>
    internal static void ApplyDeadOrSleepPose(Chara? owner, CardActor? actor)
    {
        if (owner is null || actor?.sr is null) {
            return;
        }

        var tr = actor.transform;
        if (tr is null) {
            return;
        }

        int id = actor.GetInstanceID();
        if (!owner.IsDeadOrSleeping) {
            // 只还原被我们转过的 actor；原版对非 PCC 从不写 localRotation。
            if (LyingActors.Remove(id)) {
                tr.localRotation = Quaternion.identity;
            }
            return;
        }

        var pass = owner.IsPCC ? EClass.setting?.pass?.subDeadPCC : EClass.setting?.pass?.subDead;
        if (pass is null || actor.sr.sprite is null) {
            return;
        }

        // 原版已经把这帧的 SubPass offset 加进 position，先减回来得到站立基准
        Vector3 stand = tr.position;
        var cur = SubPassData.Current;
        if (cur is not null && cur.enable) {
            stand -= cur.offset;
        }

        tr.localRotation = pass.rotation;
        LyingActors.Add(id);

        // 位置只由站立基准 + 标定常数决定：横向回本格，纵向半身入地。
        // 不叠加 pass.offset —— 那份数据是为 PCC 多部件 mesh 调的，对整图立绘无意义。
        tr.position = new Vector3(stand.x, stand.y + LyingDropY, stand.z);
    }

    /// <summary>镜像 <c>CardActor.OnRender</c> 对 offset / offsetBack 的选择。</summary>
    private static float OffsetY(RenderData data, int dir)
    {
        if (data.useOffsetBack) {
            bool back = data.symmetry ? dir % 2 == 1 : dir == 1 || dir == 2;
            if (back) {
                return data.offsetBack.y;
            }
        }
        return data.offset.y;
    }

    private static readonly Dictionary<int, float> LoggedDy = new Dictionary<int, float>();

    /// <summary>诊断（仅 DiagnosticLog）：每个角色只在差值变化时打一行，实机即可看到两套原始数值。</summary>
    private static void LogAlignment(Chara owner, float dy, float npcY, float pcY, RenderData pccData)
    {
        if (!AcsMod.DiagnosticLog) {
            return;
        }

        int uid = owner.uid;
        if (LoggedDy.TryGetValue(uid, out float last) && last == dy) {
            return;
        }
        LoggedDy[uid] = dy;

        var raceData = owner.source?.renderData;
        var baseData = ResourceCache.Load<RenderData>(CharaBaseRenderDataPath);
        var basePass = baseData?.pass;
        AcsMod.Log($"[align] dy={dy:0.####} npcY={npcY:0.####} pcY={pcY:0.####} | "
            + $"npcPrefY={owner.Pref?.y:0.####} race={raceData?.name} raceOffY={raceData?.offset.y:0.####} | "
            + $"chara={baseData?.name} charaOffY={baseData?.offset.y:0.####} meshHalfY={(basePass is not null ? basePass.pmesh.size.y * 0.5f : 0f):0.####} | "
            + $"pccPrefY={EClass.core?.refs?.prefs?.pcc?.y:0.####} pcc={pccData.name} pccOffY={pccData.offset.y:0.####}");
    }
    private static readonly Dictionary<string, string> ProbeLast = new Dictionary<string, string>();

    /// <summary>
    /// 诊断（仅 DiagnosticLog）：PC 与 NPC 都打一行同样的字段，用来对比同一张 ACS 图
    /// 在两条路径上的<b>真实渲染姿态</b>（根节点位置/缩放、sr 子节点偏移、sprite pivot/世界高、
    /// SubPass 偏移）。每个角色只在数值变化时打一行。
    /// </summary>
    internal static void ProbePose(string who, Chara owner, CardActor actor, float paramY)
    {
        if (!AcsMod.DiagnosticLog || owner is null || actor is null || actor.sr is null) {
            return;
        }

        try {
            var tr = actor.transform;
            var srTr = actor.sr.transform;
            var sp = actor.sr.sprite;
            var cr = owner.renderer as CardRenderer;
            var data = cr?.data;
            var pref = cr?.GetPref();
            var raceData = owner.sourceCard?.renderData;
            var srcData = owner.source?.renderData;

            string key = who + "|" + owner.uid;
            string val = $"{tr.localScale.y:0.####}|{srTr.localPosition.y:0.####}"
                + $"|{(sp is null ? -1f : sp.pivot.y):0.###}|{(sp is null ? 0f : sp.rect.height):0.#}"
                + $"|{(sp is null ? 0f : sp.bounds.size.y):0.####}|{SubPassData.Current.offset.y:0.####}"
                + $"|{pref?.y:0.####}|{data?.offset.y:0.####}|{data?.name}|{raceData?.offset.y:0.####}|{raceData?.name}"
                + $"|{srcData?.offset.y:0.####}|{srcData?.name}";
            if (ProbeLast.TryGetValue(key, out string? last) && last == val) {
                return;
            }
            ProbeLast[key] = val;

            AcsMod.Log($"[pose] {who} uid={owner.uid} type={actor.GetType().Name} pY={paramY:0.####}"
                + $" rootY={tr.position.y:0.####} dY={tr.position.y - paramY:0.####} rootScaleY={tr.localScale.y:0.####}"
                + $" srLocalY={srTr.localPosition.y:0.####} srScaleY={srTr.localScale.y:0.####}"
                + $" prefY={pref?.y:0.####} data={data?.name} dataOffY={data?.offset.y:0.####}"
                + $" raceData={raceData?.name} raceOffY={raceData?.offset.y:0.####}"
                + $" srcData={srcData?.name} srcOffY={srcData?.offset.y:0.####}"
                + $" sameRacePcc={(ReferenceEquals(raceData, data) ? 1 : 0)} subY={SubPassData.Current.offset.y:0.####}"
                + $" pivotY={(sp is null ? -1f : sp.pivot.y):0.###} rectH={(sp is null ? 0f : sp.rect.height):0.#}"
                + $" boundsY={(sp is null ? 0f : sp.bounds.size.y):0.####} ppu={(sp is null ? 0f : sp.pixelsPerUnit):0.#}"
                + $" skin={owner.c_idSpriteReplacer}");
        }
        catch (Exception) {
            // 诊断本身不许影响渲染
        }
    }
}
