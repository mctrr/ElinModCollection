using ACS.API;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// NPC（<see cref="RenderFramePatch"/>）与 PC（<see cref="PccRenderPatch"/>）两条渲染路径
/// 镜像重复的“每帧推进动画”状态机，合并到 <see cref="ClipPlayback.State"/> 这一份实现里。
/// 以后修动画 bug（同帧去重 / sig·clip 变化重播 / timer 累进 / 帧钳制）只需改这里一处，
/// 不用在两端各改一遍。
/// 历史 bug 根因的中文注释随逻辑从两个原文件（NpcFrameState / FrameState）原样保留。
/// </summary>
internal static class ClipPlayback
{
    /// <summary>
    /// 单角色逐帧播放状态（替代 RenderFramePatch.NpcFrameState 与 PccRenderPatch.FrameState）。
    /// 键为 actor.GetInstanceID()，由两端各自的字典按 actor 持有，清理入口（ClearRuntimeCaches）不变。
    /// 语义与旧两套一致，字段一一对应：
    /// 旧 NpcFrameState.LastFrame 与 FrameState.LastAdvanceFrame 都记录“上次推进的 Time.frameCount”，
    /// 统一为这里的 <see cref="LastAdvanceFrame"/>。
    /// </summary>
    internal sealed class State
    {
        /// <summary>上次已推进的 Time.frameCount；同帧内第二次调用不再推进（一帧只推一次）。
        /// 初始 -1 确保首帧必进；Time.frameCount 在渲染帧内恒 &gt;= 1，与旧实现行为一致。</summary>
        public int LastAdvanceFrame = -1;

        /// <summary>上次使用的 clip 对象（用引用相等判断是否“换 clip”）。</summary>
        public SpriteData? LastClip;

        /// <summary>上次一次性动作签名；null 表示非 one-shot 的普通 idle/move/combat。</summary>
        public string? LastSig;

        /// <summary>当前 clip 内累计计时。</summary>
        public float Timer;

        /// <summary>当前帧序号。</summary>
        public int Index;

        /// <summary>
        /// 判定“该从头重播”并就地更新状态：命中（a）新一轮一次性动作——sig 非空且与
        /// <see cref="LastSig"/> 不同；或（b）clip 对象引用变化——时，更新 LastSig/LastClip，
        /// 并把本状态 <see cref="Timer"/> / <see cref="Index"/> 清零，返回 true。
        /// 注意：本方法不动 <see cref="LastAdvanceFrame"/>，也不清调用方 actor 的
        /// spriteIndex/spriteTimer——NPC 的 GetCurrentAnimatedData 需要顺带把原版字段归零，
        /// 由各调用点按需处理。语义与旧两端代码一致。
        /// </summary>
        public bool RestartIfChanged(Chara chara, SpriteData data)
        {
            string? sig = AcsStateResolver.GetOneShotSignature(chara);
            bool newAction = sig != null && sig != LastSig;
            if (newAction) {
                // 新一轮一次性动作（攻击/施法/受伤/复活…）：即使 clip 与上一帧是同一个
                // SpriteData 对象（如 magic/melee 回退共用 combat 图），也必须从头播——
                // 否则继承战斗姿态的旧帧序号，一两帧就被判“播完”清掉（视觉上加速/闪完）。
                LastSig = sig;
            }

            if (newAction || !ReferenceEquals(LastClip, data)) {
                // 切换 clip（idle↔move↔combat↔melee…）或“新一轮一次性动作”时从第 0 帧重播。
                // sig 为 null（非 one-shot 的普通 idle/move/combat）时，靠 clip 引用变化重启。
                LastClip = data;
                Timer = 0f;
                Index = 0;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 每帧推进一步：同帧去重（<see cref="LastAdvanceFrame"/> == Time.frameCount 则整段跳过）
        /// → sig/clip 变化走 <see cref="RestartIfChanged"/> 重播，否则 timer += delta 累进、
        /// 越 time 调 AdvanceClipIndex 换帧；末尾做帧数钳制。返回推进后的 index。
        /// 推进后需要写回 actor 的 spriteTimer/spriteIndex 的调用点读本状态字段即可。
        /// </summary>
        public int StepFrame(Chara chara, SpriteData data)
        {
            int fc = Time.frameCount;
            if (LastAdvanceFrame != fc) {
                LastAdvanceFrame = fc;
                if (!RestartIfChanged(chara, data)) {
                    // 动作与 clip 都没变：正常计时推进。用独立副本而不直接依赖
                    // actor.spriteTimer/spriteIndex：换肤角色 vanillaTouched=true 时原版与
                    // Postfix 同帧各推一次会双倍速，这里仅记录我们这一路的推进，
                    // 配合调用点的 allowAdvance 避免叠加。
                    Timer += Core.delta;
                    if (Timer >= data.time && data.time > 0f) {
                        Timer -= data.time;
                        Index = AcsStateResolver.AdvanceClipIndex(chara, data.frame, Index);
                    }
                }
            }

            // 防御：帧数变少时钳制（例如 move 回 idle）
            if (data.frame > 0 && Index >= data.frame) {
                Index = 0;
            }

            return Index;
        }
    }
}
