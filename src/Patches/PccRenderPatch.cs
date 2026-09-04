using System;
using System.Collections.Generic;
using ACS.API;
using HarmonyLib;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// 为主角（PCC 系统）提供动态贴图支持。
/// 在 CharaActorPCC.OnRender 结束后用 ACS 帧覆盖 sr.sprite；
/// 隐藏 PCC 武器 tile overlay，但保留/重绘真实手持物，使其叠在 ACS 前面。
/// </summary>
[HarmonyPatch]
internal class PccRenderPatch
{
    private static readonly AccessTools.FieldRef<CardActor, float> SpriteTimerRef =
        AccessTools.FieldRefAccess<CardActor, float>("spriteTimer");

    private static readonly AccessTools.FieldRef<CardActor, int> SpriteIndexRef =
        AccessTools.FieldRefAccess<CardActor, int>("spriteIndex");

    // 临时把 temp 设为 -1，跳过原版 OnRender 的主/副手绘制（含战斗武器 tile）
    private static int _savedTempLeft;
    private static int _savedTempRight;
    private static bool _tempsSaved;

    // 按 actor 分状态推进帧，避免多 PCC 共享 _sharedTimer 串帧
    // 逐帧推进状态机（原 FrameState）已抽到共享的 ClipPlayback.State（见 ClipPlayback.cs），
    // 这里只留“actor.InstanceID → 状态”的字典，键与清理入口 ClearRuntimeCaches 不变。
    private static readonly Dictionary<int, ClipPlayback.State> FrameByActor = new Dictionary<int, ClipPlayback.State>();

    // 按 actor 实例分别记左右朝向；禁止全局共享（NPC 会污染 PC 的 W/S）
    private static readonly Dictionary<int, bool> FlipByActor = new Dictionary<int, bool>();

    private static Vector3 _org;

    /// <summary>控制台重载：清帧推进与朝向粘滞，避免旧 clip 索引残留。</summary>
    internal static void ClearRuntimeCaches()
    {
        FrameByActor.Clear();
        FlipByActor.Clear();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CharaActorPCC), nameof(CharaActorPCC.OnRender))]
    internal static void OnPccRenderPrefix(CharaActorPCC __instance)
    {
        _tempsSaved = false;
        if (!ShouldUseAcsOverlay(__instance) || __instance.pcc?.data is null) {
            return;
        }
        // temp=-1：原版 switch 直接 break，不画武器 tile / 装备手持。
        // 工具类手持由 Postfix DrawHeldInFront 在非战斗时补画。
        _savedTempLeft = __instance.pcc.data.tempLeft;
        _savedTempRight = __instance.pcc.data.tempRight;
        __instance.pcc.data.tempLeft = -1;
        __instance.pcc.data.tempRight = -1;
        _tempsSaved = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharaActorPCC), nameof(CharaActorPCC.OnRender))]
    internal static void OnPccRender(CharaActorPCC __instance, RenderParam p)
    {
        if (_tempsSaved && __instance.pcc?.data != null) {
            __instance.pcc.data.tempLeft = _savedTempLeft;
            __instance.pcc.data.tempRight = _savedTempRight;
            _tempsSaved = false;
        }

        if (!TryGetAcs(out var owner, out var data, __instance, p.snow)) {
            return;
        }

        // 记录手持绘制原点（与原版 OnRender 一致），供 ACS 后重绘手持物
        // 变身/形态切换瞬间 renderer.data 可能为 null（原版正在重建），空防护。
        _org.x = p.x;
        _org.y = p.y;
        _org.z = p.z + (owner.renderer?.data?.offset.z ?? 0f);

        // 静态单帧 skin 不推进动画计时
        if (data.frame > 1) {
            AdvanceFrame(__instance, data, owner);
        }
        else {
            SpriteTimerRef(__instance) = 0f;
            SpriteIndexRef(__instance) = 0;
            // 单帧 revive / greet 也要清一次性标记，否则会粘死
            AcsStateResolver.StopOneShotIfLastFrame(owner, data.frame, 0);
        }
        ApplyAcsSprite(__instance, data, p);
        // ACS 身体是 SpriteRenderer，可能盖住 mesh 手持物；在 ACS 之后再画一次手持
        DrawHeldInFront(__instance, p, owner);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharaActorPCC), nameof(CharaActorPCC.NextDir))]
    internal static void OnNextDir(CharaActorPCC __instance)
    {
        if (!TryGetAcs(out _, out var data, __instance, snow: false)) {
            return;
        }
        ApplyAcsSprite(__instance, data, null);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharaActorPCC), nameof(CharaActorPCC.RefreshSprite))]
    internal static void OnRefreshSprite(CharaActorPCC __instance)
    {
        if (!TryGetAcs(out _, out var data, __instance, snow: false)) {
            return;
        }
        ApplyAcsSprite(__instance, data, null);
    }

    private static bool ShouldUseAcsOverlay(CharaActorPCC __instance)
    {
        if (__instance.pcc?.data is null || __instance.pcc.data.isUnique) {
            return false;
        }
        var owner = __instance.owner;
        if (owner is null || owner.mimicry != null) {
            return false;
        }
        // ACS 动态 clip 或静态 Package Skin 均可叠在 PCC 上
        return AcsSuffixes.Resolve(owner) is not null
            || PcSkinPatch.GetStaticSkinData(owner.spriteReplacer) is not null;
    }

    private static bool TryGetAcs(out Chara owner, out SpriteData data, CharaActorPCC actor, bool snow)
    {
        owner = null!;
        data = null!;
        if (actor is null) {
            return false;
        }
        var o = actor.owner;
        if (o is null || o.mimicry != null) {
            return false;
        }
        if (actor.pcc?.data?.isUnique == true) {
            return false;
        }

        // 读档/冷启动：spriteReplacer 为空或尚无可用 skin 时重绑
        if (!o.c_idSpriteReplacer.IsEmpty() && o.renderer is CharaRenderer cr
            && (o.spriteReplacer is null
                || (AcsSuffixes.Resolve(o) is null
                    && PcSkinPatch.GetStaticSkinData(o.spriteReplacer) is null))) {
            PcSkinPatch.TryApplyPcSkin(cr);
        }

        // 优先动态 ACS（多帧）；否则回退静态 Package Skin 底图
        var suffixes = AcsSuffixes.Resolve(o);
        if (suffixes is not null) {
            var clip = o.GetAcsClip(snow, suffixes);
            if (clip is not null) {
                owner = o;
                data = clip;
                return true;
            }
        }

        var staticData = PcSkinPatch.GetStaticSkinData(o.spriteReplacer, o);
        if (staticData is null) {
            // 切勿 localScale=one：会把原版 PCC 的 source.size*SubPass 缩放打成 1
            return false;
        }

        owner = o;
        data = staticData;
        return true;
    }

    private static void AdvanceFrame(CharaActorPCC __instance, SpriteData data, Chara owner)
    {
        int actorKey = __instance.GetInstanceID();
        if (!FrameByActor.TryGetValue(actorKey, out var st) || st is null) {
            st = new ClipPlayback.State();
            FrameByActor[actorKey] = st;
        }

        // 同帧去重 + sig/clip 变化重播 + timer 累进 + 帧钳制与 NPC 端 AdvanceAndApply
        // 共用同一套 ClipPlayback.State.StepFrame，避免两条路径各自维护导致行为漂移。
        int index = st.StepFrame(owner, data);
        SpriteTimerRef(__instance) = st.Timer;
        SpriteIndexRef(__instance) = index;
    }

    /// <summary>
    /// 同步 angle 后按左右半球翻转。Elin 角色 shader 读 _Rect UV，不能只靠 sr.flipX。
    /// 仅 A/D（左右）更新朝向；W/S（上下）保持上次左右朝向。
    /// </summary>
    private static void ApplyAcsSprite(CharaActorPCC __instance, SpriteData data, RenderParam? p)
    {
        try {
            if (__instance.sr is null || __instance.mpb is null) {
                return;
            }

            // ACS 横条：显示前校正 frame 并确保 sprites 按帧切开
            // （避免 frame=1 时切出整条 1920 宽图，move 看起来比 idle 大很多）
            if (!data.path.IsEmpty()
                && data.path.IndexOf("_acs_", StringComparison.Ordinal) >= 0) {
                SkinListPatch.EnsureAcsSprites(data);
            }

            var sprites = data.GetSprites();
            if (sprites is null || sprites.Length == 0) {
                return;
            }

            // GetSprites 可能又走了 Load→LoadAnimationIni；再校正一次切片
            if (!data.path.IsEmpty()
                && data.path.IndexOf("_acs_", StringComparison.Ordinal) >= 0
                && (data.sprites is null
                    || data.sprites.Length != data.frame
                    || (data.frame > 1 && data.sprites.Length == 1))) {
                SkinListPatch.EnsureAcsSprites(data);
                sprites = data.sprites ?? data.GetSprites();
                if (sprites is null || sprites.Length == 0) {
                    return;
                }
            }

            int idx = SpriteIndexRef(__instance);
            if (idx < 0 || idx >= sprites.Length) {
                idx = 0;
            }
            var sprite = sprites[idx];
            if (sprite is null || sprite.texture is null) {
                return;
            }

            // 功能C：直接用原版中心 pivot Sprite（与 NPC / SpriteData.LoadSprites 一致）。
            // 底轴 GetBottomPivotSprite 在去掉 Y hack 后会脚底浮空；对齐 NPC 几何优先。

            // 每帧用 owner.angle 刷新 provider（WASD 改的是 angle）
            if (__instance.provider is not null && __instance.owner is not null) {
                __instance.provider.angle = __instance.owner.angle;
                __instance.provider.SetDir();
            }

            bool flip = ResolveHorizontalFlip(__instance);

            __instance.sr.sprite = sprite;
            __instance.sr.flipX = flip;
            // 功能3：显示前再确保 Point（静态 skin 可能不经 EnsureAcsSprites）
            if (sprite.texture.filterMode != FilterMode.Point) {
                sprite.texture.filterMode = FilterMode.Point;
            }
            __instance.mpb.SetTexture("_MainTex", sprite.texture);

            if (__instance.transform is not null) {
                // 原版 PCC OnRender: localScale = source.size * SubPassData.scale
                // ACS 覆盖后不能硬塞 Vector3.one：会丢掉场景缩放，且忽略 SpriteData.scale。
                // 两套约定：
                // - ACS ApplyClipMeta 写 scale=100 → 1.0 世界倍率（/100）
                // - 原版静态 skin 默认 scale=50 → 1.0（NPC 路径按 /50；若误用 /100 会缩成一半）
                // 注：曾试过乘 source.size，PC 变锐但体型过大（PCC 部件 size≠ACS 全身图），已回滚。
                float m = AcsSpritePresenter.ComputeLocalScaleMultiplier(__instance.owner, data);
                Vector3 passScale = SubPassData.Current.scale;
                // SubPass 未就绪时退回 1，避免 0 缩放
                if (passScale.x == 0f && passScale.y == 0f && passScale.z == 0f) {
                    passScale = Vector3.one;
                }
                Vector3 localScale = new Vector3(
                    passScale.x * m,
                    passScale.y * m,
                    passScale.z * m);
                __instance.transform.localScale = localScale;

                // 功能E3：中心 pivot 叠在 PCC 脚底锚点 → clip 级稳定抬升（半高 − 全帧最小 pad）。
                // 禁止按动画帧改 Y（E2 逐帧 pad 会抖）；lift 已量化到 0.01，加在原版已 snap 的 base 上。
                // 固定下移：动态 ACS 3px；静态单帧再上 1px（2px），因 lift 按各自 pad 算，固定量需分开。
                // 必须沿 transform.up（local Y→世界）加，不能写死 world Y：
                // 睡觉/死亡 subDeadPCC 会旋转，站立的“半高”在躺姿下变成水平位移；
                // 只加 world Y → 悬浮；完全不加 → 中心落在锚点，身子偏到邻格。
                float liftY = AcsSpritePresenter.ResolveCenterPivotLiftY(data, localScale.y);
                float yNudge = data.frame > 1 ? -0.03f : -0.02f;
                Vector3 pos = __instance.transform.position;
                pos += __instance.transform.up * (liftY + yNudge);
                __instance.transform.position = pos;
            }

            Texture2D tex = sprite.texture;
            Rect tr = sprite.textureRect;
            float x0 = tr.x / (float)tex.width;
            float x1 = tr.xMax / (float)tex.width;
            float y0 = tr.yMin / (float)tex.height;
            float y1 = tr.yMax / (float)tex.height;
            // 自定义 shader 用 _Rect 采样；左右翻转必须交换 x
            Vector4 rect = flip
                ? new Vector4(x1, y0, x0, y1)
                : new Vector4(x0, y0, x1, y1);
            __instance.mpb.SetVector("_Rect", rect);
            __instance.mpb.SetFloat("_PixelHeight", sprite.rect.height);
            __instance.sr.SetPropertyBlock(__instance.mpb);
        }
        catch (NullReferenceException) {
        }
    }

    /// <summary>
    /// 左右朝向（贴图默认朝左：flip=false 左，true 右）：
    /// PC 键盘：只认 EInput.axis.x（勿 ConvertAxis）。A/D 更新；纯 W/S 粘滞。
    /// PC 点地：无键时用「当前一步」的屏幕水平分量（等距 screenX∝mapX+mapZ），纯屏竖步粘滞。
    /// PC 施法：mcombat 窗口内朝向目标屏幕水平方向（原版 LookAt 改 angle，PC ACS 不读 angle）。
    /// PC 绝不用 angle（等距 W≈NE 会误改朝向）。
    /// NPC：angle 8 扇区；纯 N/S 粘滞。
    /// 必须按 actor 分状态，禁止全局 lastFlip。
    /// </summary>
    private static bool ResolveHorizontalFlip(CharaActorPCC actor)
    {
        int key = actor.GetInstanceID();
        FlipByActor.TryGetValue(key, out bool lastFlip);

        if (actor.owner is { IsPC: true } owner) {
            Vector2 raw = EInput.axis;
            // 1) A/D：键盘优先，与点地无关
            if (raw.sqrMagnitude > 0.0001f && Mathf.Abs(raw.x) > 0.01f) {
                lastFlip = raw.x > 0f; // +x = D/右 → flip
                FlipByActor[key] = lastFlip;
                return lastFlip;
            }

            // 2) 纯 W/S：粘滞，不走点地逻辑
            if (raw.sqrMagnitude > 0.0001f) {
                return lastFlip;
            }

            // 3) 施法窗口：朝向目标的屏幕水平方向（原版 LookAt 改了 angle，PC ACS 不读 angle）
            if (AcsStateResolver.TryGetMagicFaceRight(owner, out bool magicRight)) {
                lastFlip = magicRight;
                FlipByActor[key] = lastFlip;
                return lastFlip;
            }

            // 4) 无键：点地/寻路当前一步转头（迷宫拐弯随 movePoint 更新）
            if (TryFlipFromClickStep(owner, ref lastFlip)) {
                FlipByActor[key] = lastFlip;
            }

            return lastFlip;
        }

        float angle;
        if (actor.provider is not null) {
            angle = actor.provider.angle;
        }
        else if (actor.owner is not null) {
            angle = actor.owner.angle;
        }
        else {
            return lastFlip;
        }

        // 与 GetDirIdx 一致：((angle+22.5) 归一) / 45 → 0..7
        // 0=E,1=NE,2=N,3=NW,4=W,5=SW,6=S,7=SE
        angle = (angle % 360f + 360f) % 360f;
        float sector = (angle + 22.5f) % 360f;
        int dirIdx = (int)(sector / 45f);
        if (dirIdx < 0) {
            dirIdx = 0;
        }
        else if (dirIdx > 7) {
            dirIdx = 7;
        }

        // 纯北/南：保持上次左右
        if (dirIdx == 2 || dirIdx == 6) {
            return lastFlip;
        }

        // 0=E,1=NE,7=SE → 朝右 flip；3=NW,4=W,5=SW → 朝左
        lastFlip = dirIdx is 0 or 1 or 7;
        FlipByActor[key] = lastFlip;
        return lastFlip;
    }

    /// <summary>
    /// 点地朝向：看「当前这一步」的屏幕水平分量，不看最终终点、不用 angle。
    /// 等距 Point.Position：screenX ∝ (mapX+mapZ)，故 map 纯 ±z（屏上右上/左下）也有水平分量。
    /// 有屏水平分量则更新；纯屏竖步（map dx=-dz）返回 false（调用方保持 lastFlip）。
    ///
    /// 原版点地链路（IL 已核）：
    /// - 邻格/按住：AM_Adv → GoalManualMove，步进向量在 Player.nextMove（及 static dest）
    /// - 远点：PressedActionMove → AI_Goto，走 owner.path.nodes[nodeIndex]（递减，非 Count-1）
    /// - CharaRenderer.movePoint 在 UpdatePosition 起步时被 Set(pos)，与 pos 相等，不能当下一步
    /// </summary>
    private static bool TryFlipFromClickStep(Chara owner, ref bool lastFlip)
    {
        try {
            // 1) GoalManualMove / 箭头 / 按住点地：nextMove 即本步 (map dx, map dz→y)
            Player? player = EClass.player;
            if (player is not null) {
                Vector2 nm = player.nextMove;
                if (Mathf.Abs(nm.x) > 0.01f || Mathf.Abs(nm.y) > 0.01f) {
                    return TryApplyScreenHorizontalFlip(nm.x, nm.y, ref lastFlip);
                }
            }

            // 2) GoalManualMove.dest = GetFirstStep(pos+nextMove)；nextMove 被清零的帧仍可用
            if (owner.ai is GoalManualMove && owner.pos is not null) {
                Point dest = GoalManualMove.dest;
                if (dest is not null && dest.IsValid && !dest.Equals(owner.pos)) {
                    return TryApplyScreenHorizontalFlip(
                        dest.x - owner.pos.x,
                        dest.z - owner.pos.z,
                        ref lastFlip);
                }
            }

            // 3) AI_Goto 远点：path.nodes[nodeIndex]（与 TryGoTo 一致；Count-1 只在起步碰巧相同）
            PathProgress path = owner.path;
            if (path is not null
                && path.HasPath
                && path.nodes is not null
                && path.nodes.Count > 0
                && owner.pos is not null
                && !path.IsDestinationReached(owner.pos)) {
                int idx = path.nodeIndex;
                if (idx < 0 || idx >= path.nodes.Count) {
                    idx = path.nodes.Count - 1;
                }

                Algorithms.PathFinderNode node = path.nodes[idx];
                // 与 AI_Goto 相同：当前格等于节点时退一格
                if (node.X == owner.pos.x && node.Z == owner.pos.z && idx > 0) {
                    idx--;
                    node = path.nodes[idx];
                }

                if (node.X == owner.pos.x && node.Z == owner.pos.z) {
                    return false;
                }

                return TryApplyScreenHorizontalFlip(
                    node.X - owner.pos.x,
                    node.Z - owner.pos.z,
                    ref lastFlip);
            }

            return false;
        }
        catch {
            return false;
        }
    }

    /// <summary>
    /// 地图步进 (dx,dz) → 等距屏幕水平符号。
    /// Point.Position：screenX = (x+z)*tileAlign.x，故水平分量符号 = sign(dx+dz)。
    /// 右上(+z)/右下(+x) → 朝右；左下(-z)/左上(-x) → 朝左；纯屏竖(dx=-dz) → 粘滞。
    /// </summary>
    private static bool TryApplyScreenHorizontalFlip(float mapDx, float mapDz, ref bool lastFlip)
    {
        float screenH = mapDx + mapDz;
        if (Mathf.Abs(screenH) <= 0.01f) {
            return false;
        }

        lastFlip = screenH > 0f;
        return true;
    }

    /// <summary>
    /// ACS 覆盖后只重绘「工具类」手持物；装备武器（含战斗时）一律不画，
    /// 避免盖住/干扰 _acs_combat 动态贴图。
    /// ACS 全身图叠在 PCC 脚底锚点上，原版 heldPos 会落在脚边。
    /// clip .ini 的 heldX/heldY 是 100 ppu 像素偏移，叠在原版 heldPos 上。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CharaRenderer), nameof(CharaRenderer.DrawHeld))]
    internal static void OnDrawHeldPrefix(CharaRenderer __instance)
    {
        try {
            var owner = __instance.owner;
            if (owner is null || owner.held is null) {
                return;
            }

            var suffixes = AcsSuffixes.Resolve(owner);
            if (suffixes is null) {
                return;
            }

            bool snow = EClass.core.IsGameStarted && EClass._zone is not null && EClass._zone.IsSnowCovered;
            var clip = owner.GetAcsClip(snow, suffixes);
            if (!AcsClipIni.TryGetHeldOffset(clip, out Vector2 off)) {
                return;
            }

            float x = owner.flipX ? -off.x : off.x;
            RenderObject.currentParam.x += x;
            RenderObject.currentParam.y += off.y;
        }
        catch (Exception) {
        }
    }

    private static void DrawHeldInFront(CharaActorPCC actor, RenderParam p, Chara owner)
    {
        try {
            if (actor.pcc?.data is null || actor.pcc.data.isUnique) {
                return;
            }
            if (owner.IsDeadOrSleeping) {
                return;
            }
            // 战斗中完全不画手持/武器，交给 combat ACS
            if (AcsStateResolver.IsCombatVisual(owner)) {
                return;
            }
            // 拿着非工具 held 时不画
            if (owner.held != null && !(owner.held.trait.ShowAsTool && !HotItemHeld.disableTool)) {
                return;
            }

            Cell cell = owner.Cell;
            if (!(owner.Cell.isFloating || !cell.sourceSurface.tileType.IsDeepWater || cell.IsIceTile)) {
                return;
            }

            // 仅工具：热键栏工具 / ShowAsTool 的 held，不画 slotMainHand 装备武器
            Thing? thing = null;
            if (owner.IsPC && EMono.player.currentHotItem.RenderThing != null) {
                var rt = EMono.player.currentHotItem.RenderThing;
                if (rt.trait.ShowAsTool && !HotItemHeld.disableTool) {
                    thing = rt;
                }
            }
            if (thing is null) {
                return;
            }

            int num = actor.currentDir;
            int frame = actor.provider is not null ? actor.provider.currentFrame : 0;
            if (frame < 0 || frame >= EMono.setting.render.animeWalk.Length) {
                frame = 0;
            }

            bool flag2 = num == 0 || num == 1;
            if (thing.trait.InvertHeldSprite) {
                flag2 = !flag2;
            }
            Vector3[] mainHandPos = EMono.setting.render.mainHandPos;
            Vector3[] mainHand = EMono.setting.render.animeWalk[frame].mainHand;
            SourcePref pref = thing.Pref;
            thing.dir = flag2 ? 0 : 1;
            thing.SetRenderParam(p);
            p.x = _org.x + mainHandPos[num].x + mainHand[num].x + (flag2 ? 0.01f : -0.01f) * pref.equipX;
            p.y = _org.y + mainHandPos[num].y + mainHand[num].y + 0.01f * pref.equipY;
            p.z = _org.z - thing.renderer.data.offset.z + mainHandPos[num].z + mainHand[num].z - 0.02f;
            if (thing.renderer.hasActor) {
                thing.renderer.RefreshSprite();
                if (!flag2) {
                    p.x -= thing.renderer.data._offset.x * 2f;
                }
            }
            p.v.x = p.x;
            p.v.y = p.y;
            p.v.z = p.z;
            thing.renderer.Draw(p, ref p.v, drawShadow: false);

            p.x = _org.x;
            p.y = _org.y;
            p.z = _org.z - owner.renderer.data.offset.z;
        }
        catch (Exception) {
        }
    }
}
