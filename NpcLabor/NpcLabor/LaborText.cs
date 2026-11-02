using System;
using System.Collections.Generic;

namespace NpcLabor;

/// <summary>
/// Lightweight player-facing strings. Language routing: JP -> CN -> EN, RU
/// is supported too (its table is empty and falls back to EN per key).
/// Any UI language without a table of its own reads English, never Chinese.
/// Keep lean tone; no spreadsheet dumps.
/// </summary>
internal static partial class LaborText
{
    static readonly Dictionary<string, string> Cn = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["term.dungeonExplore"] = "地牢探索",
        ["term.regionDispatch"] = "地区派遣",
        ["term.dispatch"] = "派遣",
        ["term.townWork"] = "店铺帮工",
        ["term.coCraft"] = "共同制造",
        ["term.assist"] = "协助",
        ["term.process"] = "加工",

        ["unlock.region"] = "{0}需据点{1}级（当前{2}级）",
        ["unlock.dungeon"] = "{0}需据点{1}级（当前{2}级）",

        ["reward.money"] = "金币×{0}",
        ["reward.plat"] = "白金币×{0}",
        ["reward.furnitureTicket"] = "家具兑换券",
        ["reward.leftover"] = "打工奖励 {0}",
        ["reward.skillDiscount"] = "技能再熟练一点，报酬会更好…",
        ["reward.investQuota"] = "这家店这个月已经帮忙升过了，下个月再来吧。",
        ["reward.investTownCap"] = "店铺投资已追上城镇发展度，需先提升城镇发展度。。。",
        ["reward.investRaised"] = "{0}店铺更受欢迎了",

        ["town.settle.success"] = "完成",
        ["town.settle.recall"] = "召回",
        ["town.settle.fail"] = "失败",
        ["town.settle.line"] = "{0} 的「{1}」{2}。",
        ["town.you"] = "你",

        ["town.ui.self"] = "1  自己做",
        ["town.ui.companion"] = "2  交给同伴",
        ["town.ui.cancel"] = "取消",
        ["town.ui.selfSub"] = "约 {0} 小时 · 耗体力",
        ["town.ui.companionSub"] = "约 {0} 小时 · 选 1 人",
        ["town.ui.skillWarn"] = "这活儿还得再练练手，报酬会更好…",
        ["town.ui.noInvestWarn"] = "手艺还压不过这店铺，帮不上提升投资…",
        ["town.ui.stop"] = "停下（无主奖）",
        ["town.ui.resume"] = "继续帮工",
        ["town.ui.recall"] = "召回工人（无主奖）",
        ["town.ui.close"] = "关闭",
        ["town.leave.confirm"] = "你正在店铺帮工。现在离开的话，帮工仍会继续计时，确定要离开吗？",
        ["town.leave.confirmSelf"] = "你正在亲自店铺帮工。离开前请选择：",
        ["town.leave.abort"] = "1  退出打工（任务失败）",
        ["town.leave.swap"] = "2  换 NPC 来继续",
        ["town.leave.cancel"] = "取消",
        ["town.leave.header"] = "亲自帮工 · 离开",
        ["town.leave.noCandidates"] = "没有空闲同伴可接替",
        ["town.leave.swapOk"] = "{0} 接替了「{1}」。",
        ["town.leave.swapFail"] = "换人失败",
        ["town.leave.abortOk"] = "你退出了「{0}」。",

        ["dispatch.settle.success"] = "成功",
        ["dispatch.settle.fail"] = "失败",
        ["dispatch.settle.recall"] = "召回",
        ["dispatch.settle.end"] = "结束",
        ["dispatch.settle.line"] = "{0} 的{1}（{2}）{3}。",
        ["dispatch.fail.hint.dungeon"] = "若成功或许还能带回首领战利品与声望。",
        ["dispatch.fail.hint.region"] = "若成功或许还能带回更完整的收获。",

        // ---- common ----
        ["common.cancel"] = "取消",

        // ---- co-craft ----
        ["co.mode.off"] = "关闭",
        ["co.mode.auto"] = "自动",
        ["co.mode.autoName"] = "自动·{0}",
        ["co.mode.autoUnknown"] = "自动(?)",
        ["co.mode.you"] = "(你)",
        ["co.tag.party"] = "队",
        ["co.tag.home"] = "居",
        ["co.prefix.hobby"] = "爱好",
        ["co.prefix.work"] = "工作",
        ["co.menu.noAssist"] = "不协助",
        ["co.menu.noOne"] = "无人可协助",
        ["co.menu.skillShort"] = "技",
        ["co.msg.started"] = "{0} 正在和你一起制造。",
        ["co.msg.assistantFallback"] = "助手",
        ["co.msg.gainedExp"] = "{0} 在{1}中熟练了一些。",
        ["co.msg.ended"] = "{0}结束了。",
        ["co.msg.autoSet"] = "改为自动选择助手。",
        ["co.msg.off"] = "协助已关闭。",
        ["co.msg.pinned"] = "由 {0} 协助。",
        ["co.msg.pinFailed"] = "指定的助手现在不可用，已关闭协助。",

        // ---- process ----
        ["proc.mode.self"] = "自己",
        ["proc.mode.selfYou"] = "自己（你）",
        ["proc.mode.auto"] = "自动",
        ["proc.mode.autoNone"] = "自动(无)",
        ["proc.menu.selfProcess"] = "亲自加工",
        ["proc.menu.pickBest"] = "选最合适的人",
        ["proc.menu.noOne"] = "无人可用",
        ["proc.menu.skillShort"] = "技",
        ["proc.msg.selfMode"] = "由你自己加工。",
        ["proc.msg.autoSet"] = "自动：当前 {0}。放满材料后开始。",
        ["proc.msg.autoNone"] = "自动：附近暂时没有合适的人。",
        ["proc.msg.pinned"] = "交给 {0}。放满材料后开始。",
        ["proc.msg.pinFailed"] = "指定的人现在不可用，已改回自己操作。",
        ["proc.msg.busy"] = "这台机器已经在加工了。",
        ["proc.msg.materialIncomplete"] = "材料不完整。",
        ["proc.msg.noWorker"] = "没有可用的加工人员，改由你自己操作，或重新选择。",
        ["proc.msg.notEnoughMaterial"] = "材料数量不够。",
        ["proc.msg.changed"] = "材料在开始时发生了变化。",
        ["proc.msg.cannotTake"] = "无法取出材料。",
        ["proc.msg.cannotOperate"] = "{0} 不会操作这台机器。",
        ["proc.msg.start"] = "{0} 开始在{1}上加工（{2} 次）。",
        ["proc.msg.residentFallback"] = "居民",
        ["proc.msg.machineFallback"] = "加工机",
        ["proc.msg.doneSkilled"] = "{0} 完成了{1}，手更熟了。",
        ["proc.msg.done"] = "{0} 完成了{1}。",
        ["proc.msg.interrupted"] = "{0} 的{1}中断了。",
        ["proc.msg.stopped"] = "{0} 停止了{1}。",
        ["proc.msg.noFuel"] = "燃料不够，{0}停下了。",
        ["proc.msg.noMaterial"] = "材料用完了，{0}停下了。",
        ["proc.msg.noMachine"] = "机器不见了，{0}停下了。",
        ["proc.msg.stuck"] = "路被堵住了，{0}停下了。",
        ["proc.msg.exhausted"] = "{0}太累了，停下了。",
        ["proc.msg.ended"] = "{0}的{1}结束了。",
        ["proc.msg.zoneContinue"] = "{0}留在原地图继续{1}。",
        ["proc.msg.zoneResume"] = "{0}继续加工（剩余 {1} 次）。",
        ["proc.msg.leftoverGround"] = "你背包装不下，剩下的材料堆在{0}旁（不会自动拾取）。",

        // ---- process jobs list (multi-task) ----
        ["proc.ui.jobsTitle"] = "加工任务",
        ["proc.ui.jobsNone"] = "暂无加工任务",
        ["proc.ui.jobsIdle"] = "待派工",
        ["proc.ui.jobsSuspended"] = "留守中",
        ["proc.ui.jobsReturning"] = "退还材料中",
        ["proc.ui.jobsProgress"] = "{0}/{1}",
        ["proc.ui.jobsEtaHours"] = "余 {0} 小时",
        ["proc.ui.jobsCancelDone"] = "已取消「{0}」的加工。",
        ["proc.ui.jobsWorkerBusy"] = "{0} 正在加工别的机器。",

        // ---- craft UI ----
        ["craft.effLine"] = "{0} / 有效 {1}",
        ["craft.effLineSkill"] = "{0} {1} / 有效 {2}",

        // ---- dispatch ----
        ["dis.error.invalidTarget"] = "无效目标",
        ["dis.error.notUnlocked"] = "{0}未解锁",
        ["dis.error.tooManyTarget"] = "同一目标最多 {0} 人",
        ["dis.error.tooManyDungeon"] = "同一地牢最多 {0} 人",
        ["dis.error.cannotSend"] = "{0} 无法派出",
        ["dis.error.noOne"] = "没有可派出的角色",
        ["dis.error.regionBusy"] = "该地区已有{0}进行中",
        ["dis.error.dungeonBusy"] = "该地牢已有{0}进行中",
        ["dis.error.needBase"] = "需要据点",
        ["dis.error.regionTile"] = "附近没有可进入的该地区格子",
        ["dis.error.dungeonNo"] = "该地牢不可{0}",
        ["dis.start.region"] = "{0} 出发{1}：{2}（{3} 人 · 探索{4}周）",
        ["dis.start.dungeon"] = "{0} 出发{1}：{2}（{3} 人 · 约{4}天）",
        ["dis.error.startFail"] = "出发失败: {0}",
        ["dis.loot.none"] = "暂无收获",
        ["dis.parcel.harvest"] = "派遣收获",
        ["dis.parcel.partial"] = "派遣部分收获",
        ["dis.parcel.recall"] = "派遣召回",
        ["dis.parcel.loot"] = "派遣战利品",
        ["dis.parcel.package"] = "派遣包裹",
        ["dis.loot.item"] = "物品",
        ["dis.loot.noMission"] = "无任务",
        ["dis.loot.noPlan"] = "暂无计划收获",
        ["dis.loot.moreKinds"] = "…共{0}种",
        ["dis.loot.totalLine"] = "{0} (共{1}件 x{2})",
        ["dis.loot.etcItems"] = "{0} 等{1}项",
        ["dis.loot.planFail"] = "计划读取失败: {0}",
        ["dis.loot.special"] = "特殊",
        ["dis.debug.nonRegion"] = "非地区任务（成功时现算） 当前追踪: ",
        ["dis.debug.harvestDump"] = "派遣成功计划收获 x{0}: ",
        ["dis.debug.output"] = "已输出 {0} 个任务计划收获",
        ["dis.debug.noMission"] = "当前没有派遣任务",
        ["dis.debug.forceDone"] = "已强制完成 {0} 个派遣（成功收获已入邮箱）",
        ["dis.debug.notFound"] = "未找到派遣任务 {0}",
        ["dis.debug.forceDoneOne"] = "已强制完成派遣 #{0} {1}",
        ["dis.exp.explore"] = "探索",
        ["dis.exp.lockpick"] = "开锁",
        ["dis.exp.gather"] = "采集",
        ["dis.exp.dig"] = "挖掘",
        ["dis.exp.lumber"] = "伐木",
        ["dis.exp.tunnel"] = "挖洞",
        ["dis.exp.zero"] = "{0}+0",
        ["dis.exp.gain"] = "{0}+{1}(lv{2} 差{3})",
        ["dis.compass.e"] = "东",
        ["dis.compass.w"] = "西",
        ["dis.compass.s"] = "南",
        ["dis.compass.n"] = "北",
        ["dis.compass.near"] = "附近",
        ["dis.yield.rich"] = "丰厚",
        ["dis.yield.medium"] = "中等",
        ["dis.yield.normal"] = "普通",
        ["dis.yield.meager"] = "微薄",
        ["dis.stat.none"] = "未选择",
        ["dis.stat.line"] = "战{0} 探{1} 锁{2} 采{3}",
        ["dis.stat.lineChance"] = "战{0} 探{1} 锁{2} 采{3} {4}%",
        ["dis.region.name.plain"] = "平原",
        ["dis.region.name.forest"] = "森林",
        ["dis.region.name.beach"] = "沙滩",
        ["dis.region.name.mountain"] = "山脉",
        ["dis.region.name.area"] = "地区",
        ["dis.dist"] = "{0} 距{1}",
        ["dis.distDanger"] = "{0} 距{1} 危{2}",
        ["dis.kind.random"] = "随机",
        ["dis.kind.fixed"] = "固定",
        ["dis.kind.randomDungeon"] = "随机地牢",
        ["dis.kind.fixedDungeon"] = "固定地牢",
        ["dis.preview.region"] = "{0}\n{1}  {2}\n{3}人 · {4}\n探索{5}周\n探{6} 开锁{7} 采集{8}",
        ["dis.preview.dungeon"] = "{0}\n{1}  危险{2}  {3}\n{4}人 · {5}\n约{6}天 · 成功率{7}%\n战力{8}  探{9} 开锁{10} 采集{11}",
        ["dis.info.dungeon"] = "{0}（{1}） {2} 危险{3} · 约{5}天",
        ["dis.info.region"] = "{0}（地区） {1} · 探索{2}周",
        ["dis.msg.baseOnly"] = "只能在据点使用{0}。",
        ["dis.msg.busyTalk"] = "{0} 正在探索中，不便交谈。",
        ["dis.msg.busyInteract"] = "{0} 正在探索中，无法互动。",
        ["dis.msg.isIn"] = "{0}在{1}那里。",
        ["dis.msg.empty"] = "当前没有{0}/{1}",
        ["dis.ui.activeHeader"] = "— 进行中任务 —",
        ["dis.ui.randomHeader"] = "— {0}·随机 —",
        ["dis.ui.fixedHeader"] = "— {0}·固定 —",
        ["dis.ui.noTargets"] = "附近没有已知目标",
        ["dis.ui.recallPartial"] = "召回（部分奖励）",
        ["dis.ui.recallFail"] = "召回失败",
        ["dis.ui.progress"] = "{0}% 剩{1}天",
        ["dis.ui.taskHeader"] = "任务 · {0}",
        ["dis.ui.weeks"] = "探索{0}周",
        ["dis.ui.chooseWeeks"] = "{0} · 选择探索周数",
        ["dis.ui.noLastTeam"] = "没有可用的上次队伍",
        ["dis.ui.noAutoPick"] = "没有可自动选择的成员",
        ["dis.ui.noCandidates"] = "没有可派出的居民或队友",
        ["dis.ui.confirmSel"] = "确认（{0}/{1}）",
        ["dis.ui.lastTeam"] = "上次队伍",
        ["dis.ui.maxHarvest"] = "最大收获",
        ["dis.ui.maxCombat"] = "战斗最大收获",
        ["dis.ui.partyTag"] = "队伍",
        ["dis.ui.restoreLast"] = "恢复该目标上次队伍",
        ["dis.ui.autoPickGather"] = "按探索/采集优先选满",
        ["dis.ui.autoPickPower"] = "按战力优先选满",
        ["dis.ui.pickHeader"] = "{0} · 选择人员（可多选，最多{1}）",
        ["dis.ui.pickHeaderWeeks"] = "{0} · 探索{1}周 · 选人（最多{2}）",
        ["dis.ui.tooMany"] = "同一地城最多 {0} 人",
        ["dis.ui.pickFirst"] = "请先选择人员",
        ["dis.ui.invalidChoice"] = "选择无效",
        ["dis.ui.confirmSend"] = "确认派出",
        ["dis.ui.busySuffix"] = "（进行中）",
        ["dis.ui.inProgress"] = "进行中",
        ["dis.q.ended"] = "（已结束）",
        ["dis.q.members"] = "成员: {0}",
        ["dis.q.position"] = "位置: {0}",
        ["dis.q.progress"] = "进度 {0}%",
        ["dis.q.left"] = "剩{0}天",
        ["dis.q.harvest"] = "收获: {0}",
        ["dis.q.regionPos"] = "{0} ·{1}周",
        ["dis.q.target"] = "目标",
        ["dis.q.entrance"] = "入口",
        ["dis.q.area"] = "地区",
        ["dis.q.plan"] = "计划收获: {0}",

        // ---- town labor ----
        ["town.error.invalidJob"] = "无效岗位",
        ["town.error.noTownWork"] = "据点没有{0}",
        ["town.error.invalidPlace"] = "无效地点",
        ["town.error.busy"] = "你正忙着",
        ["town.error.busyCoCraft"] = "正在共同制造中",
        ["town.error.clientBusy"] = "该委托人已有{0}进行中",
        ["town.error.maxConcurrent"] = "本城同时最多 {0} 个{1}",
        ["town.error.jobGone"] = "委托人岗位已失效",
        ["town.error.noSp"] = "体力不足，先休息一下再打工。",
        ["town.error.workerBusy"] = "工人忙碌中",
        ["town.error.cannotAssign"] = "{0} 不可派工",
        ["town.msg.youStart"] = "你前往 {0} 处做「{1}」（约 {2} 小时）。",
        ["town.msg.workerStart"] = "{0} 去 {1} 做「{2}」（约 {3} 小时）。",
        ["town.msg.youTired"] = "你太累了，停下了「{0}」。",
        ["town.msg.workStart"] = "你开始在 {0} 处做「{1}」。",
        ["town.msg.paused"] = "{0}那边的帮工暂停了，走近店主用中键可以继续。",
        ["town.msg.endedShort"] = "该{0}已结束",
        ["town.msg.stopFail"] = "停下失败",
        ["town.msg.recallFail"] = "召回失败",
        ["town.msg.jobInvalid"] = "岗位无效",
        ["town.msg.clientGone"] = "委托人不在",
        ["town.msg.noCandidates"] = "没有空闲的居民或队员",
        ["town.msg.invalidChoice"] = "人选无效",
        ["town.msg.town"] = "店铺",
        ["town.msg.townName"] = "城镇",
        ["town.msg.worker"] = "工人",
        ["town.msg.companion"] = "同伴",
        ["town.msg.client"] = "委托人",
        ["town.progress.arriving"] = "前往工作地点…",
        ["town.progress.line"] = "{0}%  剩余{1}小时",
        ["town.ui.confirmStart"] = "确认开工",
        ["town.ui.sendConfirm"] = "派 {0} 去做「{1}」",
        ["town.ui.hours"] = "约 {0} 小时",
        ["town.ui.choosePick"] = "{0} · 选 1 人（约 {1} 时）",
        ["town.ui.skillFallback"] = "技能",
        ["town.ui.partyTag"] = "队伍",
        ["town.ui.busyTag"] = "{0}中",
        ["town.ui.youTag"] = "· 你",
        ["town.ui.needHelp"] = "店里忙，需要帮忙。",
        ["town.ui.inProgress"] = "进行中",
        ["town.ui.header"] = "{0} · {1} · {2}",
        ["town.reward.hint"] = "报酬：约金币×{0}",
        ["town.reward.line"] = "报酬 {0}",
        ["town.reward.moneyOnly"] = "报酬：金币×{0}",
        ["town.busy.invalid"] = "无效角色",
        ["town.busy.town"] = "正在{0}中",
        ["town.busy.dungeon"] = "正在{0}中",
        ["town.busy.process"] = "正在{0}中",
        ["town.busy.coCraft"] = "正在协助制造中",
        ["town.busy.craft"] = "正在据点生产中",

        ["craft.note.missing"] = "缺材料：{0}",
        ["craft.note.notHome"] = "要在自己的据点里才会动手",
        ["craft.note.awayWorker"] = "居民不在据点",
        ["craft.note.busy"] = "居民正在忙别的活",
        ["craft.note.stocked"] = "库存已够（{0}）",
        ["craft.note.noBench"] = "据点里没有{0}，先放一个再做",
        ["craft.note.failed"] = "制作失败",
        ["craft.note.working"] = "正在工作台上干活",
        ["craft.note.walking"] = "正在走向工作台",
        ["craft.note.interrupted"] = "制作被打断，材料已还回",
        ["craft.note.workerGone"] = "居民不在了",
        ["craft.msg.missing"] = "「{0}」的常驻生产缺材料：{1}",
        ["craft.msg.waiting"] = "据点生产：{0} 个任务停着（{1}）",
        ["craft.msg.running"] = "据点生产：{0} 个任务进行中（{1}）",
        ["craft.msg.cancelled"] = "已取消「{0}」的常驻生产",
        ["craft.state.none"] = "没有指派任何制作任务。",

        ["craft.ui.assign"] = "指派居民制作",
        ["craft.ui.pickWorker"] = "让谁来做",
        ["craft.ui.pickRecipe"] = "做什么",
        ["craft.ui.pickMode"] = "做到什么程度",
        ["craft.ui.mode.count"] = "制作指定数量",
        ["craft.ui.mode.keep"] = "保持库存数量",
        ["craft.ui.mode.endless"] = "无限制作",
        ["craft.ui.pickTarget"] = "数量",
        ["craft.ui.noStation"] = "无需工作台",
        ["craft.ui.noStationAvailable"] = "据点里没有可用的工作台，先放一个制作工具",
        ["craft.ui.cancelOrder"] = "取消常驻生产：{0}",
        ["craft.ui.noWorker"] = "据点里没有可指派的居民",
        ["craft.ui.page"] = "第 {0}/{1} 页",
        ["craft.ui.next"] = "下一页",
        ["craft.ui.prev"] = "上一页",
        ["craft.ui.materials"] = "材料：{0}",
        ["craft.ui.materialOk"] = "材料充足",
        ["craft.ui.skill"] = "技能 {0}（需要 {1}）",
        ["craft.ui.cancel"] = "取消这个任务",
        ["town.talk.busy"] = "{0} 正在{1}中，不便交谈。",
        ["town.talk.noInteract"] = "{0} 正在{1}中，无法互动。",

        // ---- job catalog ----
        ["job.inn.title"] = "旅店杂役",
        ["job.inn.skill"] = "搬运/杂务",
        ["job.inn.r1"] = "最近城里人多，客房和厅堂都转不过来。我自己忙着招呼客人，请替我把客房收拾干净，顺便把楼下的桌面和脏布也处理一下。",
        ["job.inn.r2"] = "冒险者扎堆住店，夜里脚步声没停过。我脱不开身，请派个手脚利落的人来帮忙铺床、倒水、把走廊收拾整齐。",
        ["job.inn.r3"] = "明天有一拨常客要来，今天周转特别紧。平时杂务都是我自己做，现在实在顾不上，请替我把店里的杂活顶一阵。",
        ["job.inn.r4"] = "店里杯盘和行李堆着，前台又在排队。我腾不出手，请找人帮我把客房和公用间收拾到能再接客的程度。",
        ["job.kitchen.title"] = "厨房帮厨",
        ["job.kitchen.skill"] = "料理",
        ["job.kitchen.r1"] = "出餐高峰到了，案板上的菜还没切完。平时备菜洗碗都是我自己做，现在脱不开身，请替我在厨房搭把手。",
        ["job.kitchen.r2"] = "灶火正旺，锅碗已经堆起来了。我要盯着主菜，请派个人来洗锅、递盘、把出餐口也照看一下。",
        ["job.kitchen.r3"] = "订单比平时多，厨房转不过弯。不需要你掌勺，只要帮我把杂活顶住，别让出餐断档就行。",
        ["job.kitchen.r4"] = "临时缺帮手，汤底和配菜都压着。请找个仔细点的人来备菜洗碗，忙完这一阵就好。",
        ["job.general.title"] = "杂货店员",
        ["job.general.skill"] = "交涉",
        ["job.general.r1"] = "货架有些乱，门口还排着问价的人。我要去清点进货，请替我看一会儿柜台，把货理齐、把常客招呼好。",
        ["job.general.r2"] = "刚到的货还没上架，店里又走不开人。平时理货都是我自己做，现在脱不开身，请派个人来搭把手。",
        ["job.general.r3"] = "最近问价的人特别多，一个人顾不过来。请找个口齿清楚的人帮我看店，拿不准的货别乱报价，喊我就行。",
        ["job.general.r4"] = "店里杂务堆着，绳子蜡烛和瓶罐都要重摆。我忙着记账，请替我把货架整理好，顺带应付一下柜台。",
        ["job.smith.title"] = "铁匠助手",
        ["job.smith.skill"] = "锻造",
        ["job.smith.r1"] = "炉火正旺，订单还压着。我脱不开身，请替我拉风箱、递钳子，把打完的铁屑边角也一并收好。",
        ["job.smith.r2"] = "今天锻打排得满，一个人转不过来。不需要你掌锤，只要帮我递工具、清台面，听锤声做事就行。",
        ["job.smith.r3"] = "半成品和碎铁皮堆在砧台上。平时收拾都是我自己做，现在忙着赶活，请派个助手来搭把手。",
        ["job.smith.r4"] = "临时缺帮手，风箱和边角料都顾不上。请找个沉得住气的人来当助手，烫手的东西别乱摸。",
        ["job.food.title"] = "食品店员",
        ["job.food.skill"] = "料理/交易",
        ["job.food.r1"] = "早市货刚卸下来，台秤旁还忙着。我脱不开身，请替我把货摆整齐，称重时招呼客人。",
        ["job.food.r2"] = "买的人比平时多，一个人看不过来柜台。请派个人来帮忙摆摊、称重，卖相差的先放到后面。",
        ["job.food.r3"] = "新鲜货要趁早卖，筐里果菜还没码好。平时都是我自己做，现在腾不出手，请找人搭把手。",
        ["job.food.r4"] = "柜台忙，进货也压着。请替我把食品摆上前，笑着招呼来买的人，忙过这阵就行。",
        ["job.book.title"] = "书店店员",
        ["job.book.skill"] = "阅读",
        ["job.book.r1"] = "我缺了整理书架的人手。平时都是自己分类编号，但我现在脱不开身，请替我把新到的书按架归位，别让客人找不到。",
        ["job.book.r2"] = "你是经验丰富的旅行者吗？店里最近打听旧书和地图的人特别多，请帮我招呼柜台，顺便把散落的册子收齐。",
        ["job.book.r3"] = "新到的一批书还没拆完，柜台又排着人。我顾不上两头，请替我把书架理顺，问价的先记下来。",
        ["job.book.r4"] = "有人把专题书和闲书混在一起了。我正忙着进货，请找个细心点的人来重新排架，别把封面弄脏。",
        ["job.scholar.title"] = "学者助手",
        ["job.scholar.skill"] = "记忆",
        ["job.scholar.r1"] = "我缺了研究所需要的抄写人手。平时都是自己整理，但我现在脱不开身，请替我誊清几份记录，把案上的文书归好。",
        ["job.scholar.r2"] = "公会这边事务堆着，名册和回执都要过一遍。我不在的时候，请帮我把文件按类分开，别弄丢印章。",
        ["job.scholar.r3"] = "最近来办手续的人特别多，案头转不过来。请派个稳当的人来帮忙登记、传话，把排队的人也安顿好。",
        ["job.scholar.r4"] = "我手头有一份急件要核对。平时都是自己做，但今天实在顾不上，请替我把附表誊清并按编号收好。",

        // Board titles: shop identity, not generic clerk labels.
        ["job.shop.fish.need"] = "鱼店需要人手",
        ["job.shop.meat.need"] = "肉店需要人手",
        ["job.shop.fruit.need"] = "果蔬店需要人手",
        ["job.shop.bread.need"] = "面包店需要人手",
        ["job.shop.milk.need"] = "奶品店需要人手",
        ["job.shop.booze.need"] = "酒铺需要人手",
        ["job.shop.food.need"] = "食品店需要人手",
        ["job.shop.inn.need"] = "旅店需要人手",
        ["job.shop.kitchen.need"] = "厨房需要人手",
        ["job.shop.book.need"] = "书店需要人手",
        ["job.shop.scholar.need"] = "文书处需要人手",
        ["job.shop.smith.need"] = "铁匠铺需要人手",
        ["job.shop.general.need"] = "杂货店需要人手",
        ["job.shop.junk.need"] = "废品店需要人手",
        ["job.shop.souvenir.need"] = "纪念品店需要人手",
        ["job.shop.generic.need"] = "{0}需要人手",

        // ModConfigGUI tab (the base game integrated the ModConfigGUI mod in 0.23.350).
        // Row labels double as the slider captions, so keep them short.
        ["cfg.title"] = "NPC 帮工 · 配置",
        ["cfg.hint"] = "改动立即生效，并写回 labor_config.json。",
        ["cfg.sec.features"] = "功能开关",
        ["cfg.feat.enabled"] = "总开关",
        ["cfg.feat.dispatch"] = "派遣",
        ["cfg.feat.townLabor"] = "店铺帮工",
        ["cfg.feat.craft"] = "据点生产",
        ["cfg.sec.town"] = "店铺帮工",
        ["cfg.town.minWage"] = "每小时最低工资",
        ["cfg.town.expPerHour"] = "每小时经验",
        ["cfg.town.ticket"] = "家具券概率（%）",
        ["cfg.town.hoursMin"] = "最短工时",
        ["cfg.town.hoursMax"] = "最长工时",
        ["cfg.town.headroom"] = "城镇投资余量",
        ["cfg.town.platBase"] = "白金币基数",
        ["cfg.town.platPerTier"] = "每投资档白金币",
        ["cfg.town.tierSize"] = "投资档规模",
        ["cfg.town.affinity"] = "好感基数",
        ["cfg.sec.dispatch"] = "派遣",
        ["cfg.dis.lockpickPerChest"] = "每箱开锁次数",
        ["cfg.dis.lockpickMaxChests"] = "开锁箱数上限",
        ["cfg.dis.bossMult"] = "首领奖励倍率",
        ["cfg.dis.specialtyMult"] = "地区特产倍率",
        ["cfg.dis.expExploreSuccess"] = "探索成功经验",
        ["cfg.dis.expExploreFailure"] = "探索失败经验",
        ["cfg.dis.expExploreRecall"] = "探索召回经验",
        ["cfg.dis.expLockpickSuccess"] = "开锁成功经验",
        ["cfg.dis.expLockpickFailure"] = "开锁失败经验",
        ["cfg.dis.expLockpickRecall"] = "开锁召回经验",
        ["cfg.dis.expGatherSuccess"] = "采集成功经验",
        ["cfg.dis.expGatherFailure"] = "采集失败经验",
        ["cfg.dis.expGatherRecall"] = "采集召回经验",
        ["cfg.dis.fameBase"] = "声望基数",
        ["cfg.dis.famePerDanger"] = "每级危险声望",
        ["cfg.sec.process"] = "加工",
        ["cfg.proc.speed"] = "加工速度倍率",
        ["cfg.sec.unlock"] = "解锁门槛",
        ["cfg.unlock.region"] = "地区派遣需据点等级",
        ["cfg.unlock.dungeon"] = "地牢探索需据点等级",
        ["cfg.disabled"] = "该功能已在配置里关闭",
        ["cfg.sec.debug"] = "调试",
        ["cfg.dbg.pdispHint"] = "常驻派遣按整月结算，测试时可以在这里直接推进月份或立刻结算。",
        ["cfg.dbg.pdispMonth"] = "常驻派遣：推进 1 个月",
        ["cfg.dbg.pdispMonthDone"] = "已为 {0} 个常驻派遣推进 1 个月。",
        ["cfg.dbg.pdispFinish"] = "常驻派遣：立即结束并结算",
        ["cfg.dbg.pdispFinishDone"] = "已结算 {0} 个常驻派遣，报酬寄往家园邮箱。",

        // ---- 常驻派遣 (permanent dispatch, 2026-10-28) ----
        ["term.pdispatch"] = "常驻派遣",
        ["pdisp.name.fishing"] = "捕鱼",
        ["pdisp.name.coach"] = "教练",
        ["pdisp.name.patrol"] = "治安巡逻",
        ["pdisp.name.lookout"] = "望风",
        ["pdisp.name.winery"] = "葡萄酒庄帮工",
        ["pdisp.name.hunter"] = "猎人",
        ["pdisp.name.market"] = "市集帮工",
        ["pdisp.name.broker"] = "中间人",
        ["pdisp.name.farm"] = "稻田帮工",
        ["pdisp.name.farm_dry"] = "田间帮工",
        ["pdisp.name.altar"] = "祭坛侍奉",
        ["pdisp.name.painter"] = "街头画师",
        ["pdisp.name.tutor"] = "魔法老师",
        ["pdisp.name.concert"] = "宫廷演奏",
        ["pdisp.name.gather"] = "山货采集",
        ["pdisp.name.inn"] = "旅馆帮厨",
        ["pdisp.name.church"] = "教堂杂役",
        ["pdisp.name.porter"] = "搬运工",
        ["pdisp.name.children"] = "照顾孩子们",
        ["pdisp.name.forge"] = "枪匠助手",

        // ---- 委托正文：告示板详情里由"当地负责人"口吻说出的一段话 ----
        ["pdisp.desc.pd_fishing"] = "港口最近缺人手。船一靠岸就要人分鱼、过秤，忙起来一整天都歇不下。你要是会撒网，就来码头找管事吧。",
        ["pdisp.desc.pd_coach_fighter"] = "公会里那批新人连剑都握不稳。我这边实在腾不出手，谁能替我带一带他们？",
        ["pdisp.desc.pd_patrol"] = "这镇子白天还算安分，夜里就不好说了。多一个人在街上走动，店家才敢把门关上。",
        ["pdisp.desc.pd_lookout"] = "巡逻队最近查得紧，巷口得有人盯着。看到不对劲就递个话，别让人察觉。",
        ["pdisp.desc.pd_winery"] = "收成这几天最忙。温泉那边客人还等着，我两头跑不过来。",
        ["pdisp.desc.pd_hunter"] = "山里的野兽最近闹到镇子边上了。得有人上去清一清，免得伤了人和牲口。",
        ["pdisp.desc.pd_market"] = "卸货、支棚、收摊，一天里最忙的就是这几趟。缺个跑腿的。",
        ["pdisp.desc.pd_broker"] = "商队来得多了，两边的话传不过来。价钱要谈，面子也得留，不是谁都能干。",
        ["pdisp.desc.pd_farm"] = "水田该放水了，一个人守着几块田跑不过来。插秧、看水、割稻，哪一样都缺手。",
        ["pdisp.desc.pd_farm_yowyn"] = "地里的菜一茬接一茬，收完这茬那茬又该翻了。帮我翻翻土、浇浇水，能省不少事。",
        ["pdisp.desc.pd_altar_foxtown"] = "这处小祭坛一直没人正经管。你只要天天去，神自会看见。",
        ["pdisp.desc.pd_painter"] = "鲁米艾斯特的街上总有人想画像。缺个会拿笔的，支起架子就能开工。",
        ["pdisp.desc.pd_tutor_mage"] = "学徒比往年多了三成，教书的人手没跟上。",
        ["pdisp.desc.pd_concert"] = "宫里的乐师告了长假，外宾要来，曲子不能停。得找个顶得上的。",
        ["pdisp.desc.pd_gather"] = "山货过了这阵子就没了。可这两年认路的人少了，总有人采回不能吃的东西。",
        ["pdisp.desc.pd_inn"] = "天不亮就得和面备料，一个人实在转不开。厨房里缺双麻利的手。",
        ["pdisp.desc.pd_shrine_nefu"] = "来往的旅人多，祭坛前的灯一天不能断。总得有人照应。",
        ["pdisp.desc.pd_church"] = "神职人员忙不过来，杂事算不上体面，可总得有人做。愿意的话，从早晨的钟声开始。",
        ["pdisp.desc.pd_porter"] = "商队不等天亮，货到了就得卸。有力气的来，日结。",
        ["pdisp.desc.pd_children"] = "我守了她们很久。现在轮到你了。做不好，我会知道。",
        ["pdisp.desc.pd_forge"] = "枪管要校，铆钉要打，一道工序比一道细。学徒顶不上，得找个熟手。",
        ["pdisp.desc.pd_altar_aquli"] = "这地方的声音我听得懂，别人听不懂。愿意学着听，就留下。",

        ["pdisp.reward.townInfluence"] = "城镇影响力",
        ["pdisp.ui.header"] = "{0} · {1}",
        ["pdisp.ui.activeRow"] = "{0}（{1}，已干 {2} 个整月）",
        ["pdisp.ui.activeSub"] = "{0} · 已结算 {1} 个整月",
        ["pdisp.ui.recall"] = "召回结算",
        ["pdisp.ui.close"] = "关闭",
        ["pdisp.ui.noTask"] = "这里没有常驻派遣。",
        ["pdisp.ui.noCandidate"] = "没有能干「{0}」的人。",
        ["pdisp.ui.gate"] = "{0}>{1}",
        ["pdisp.ui.guildGate"] = "{0}阶段>{1}",
        ["pdisp.ui.inProgress"] = "进行中",
        ["pdisp.ui.served"] = "已结算 {0} 个整月",
        ["pdisp.ui.nextSettle"] = "距离下次结算 {0}",
        ["pdisp.reward.develop"] = "城镇发展度",
        ["pdisp.reward.influence"] = "当地影响力",
        ["pdisp.reward.indulgence"] = "免罪符（稀有）",
        ["pdisp.reward.faith"] = "信仰加成",
        ["pdisp.reward.godweapon"] = "神明偏好的武器（奇跡以上）",
        ["pdisp.reward.blessed"] = "祝福的{0}",
        ["pdisp.guild.fighter"] = "战士公会",
        ["pdisp.guild.thief"] = "盗贼公会",
        ["pdisp.guild.mage"] = "法师公会",
        ["pdisp.guild.merchant"] = "商会",
        ["pdisp.err.invalid"] = "无效的派遣。",
        ["pdisp.err.noZone"] = "找不到「{0}」的派遣地点。",
        ["pdisp.err.busy"] = "{0} 已经在忙别的事了。",
        ["pdisp.err.defBusy"] = "「{0}」已经有人在做了。",
        ["pdisp.err.gate"] = "{0} 的技能还干不了「{1}」。",
        ["pdisp.err.guildRank"] = "「{0}」需要你在对应工会的等级升到 2。",
        ["pdisp.err.recallFail"] = "召回失败。",
        ["pdisp.msg.start"] = "{0} 开始在{2}做{1}了。",
        ["pdisp.msg.settle"] = "「{1}」结束了：{0} 共干了 {2} 个整月，报酬{3}",
        ["pdisp.msg.mailed"] = "已打包寄到家园邮箱。",
        ["pdisp.msg.recalled"] = "（提前召回）",
        ["pdisp.msg.settleNone"] = "「{1}」结束了：{0} 还没干满一个月，没有可结算的报酬{2}",
        ["pdisp.msg.noPay"] = "（无）",
    };

    static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["term.dungeonExplore"] = "Dungeon Explore",
        ["term.regionDispatch"] = "Region Outing",
        ["term.dispatch"] = "Dispatch",
        ["term.townWork"] = "Shop Labor",
        ["term.coCraft"] = "Co-Craft",
        ["term.assist"] = "Assist",
        ["term.process"] = "Process",

        ["unlock.region"] = "{0} needs base Lv{1} (now {2})",
        ["unlock.dungeon"] = "{0} needs base Lv{1} (now {2})",

        ["reward.money"] = "Gold x{0}",
        ["reward.plat"] = "Platinum x{0}",
        ["reward.furnitureTicket"] = "Furniture ticket",
        ["reward.leftover"] = "Work bonus {0}",
        ["reward.skillDiscount"] = "A bit more practice would earn better pay...",
        ["reward.investQuota"] = "This shop already got a hand this month — come back next month...",
        ["reward.investTownCap"] = "The shop has caught up with the town's development — raise that first...",
        ["reward.investRaised"] = "{0}'s shop is more popular now...",

        ["town.settle.success"] = "done",
        ["town.settle.recall"] = "recalled",
        ["town.settle.fail"] = "failed",
        ["town.settle.line"] = "{0}'s \"{1}\" {2}.",
        ["town.you"] = "You",

        ["town.ui.self"] = "1  Do it yourself",
        ["town.ui.companion"] = "2  Send a companion",
        ["town.ui.cancel"] = "Cancel",
        ["town.ui.selfSub"] = "~{0}h · costs stamina",
        ["town.ui.companionSub"] = "~{0}h · pick 1",
        ["town.ui.skillWarn"] = "Needs a steadier hand for full pay...",
        ["town.ui.noInvestWarn"] = "Skill is below this shop's level - no invest gain...",
        ["town.ui.stop"] = "Stop (no main prize)",
        ["town.ui.resume"] = "Resume the shift",
        ["town.ui.recall"] = "Recall worker (no main prize)",
        ["town.ui.close"] = "Close",
        ["town.leave.confirm"] = "Shop labor is still running. Leaving now keeps the job ticking — leave anyway?",
        ["town.leave.confirmSelf"] = "You are doing shop labor yourself. Choose before leaving:",
        ["town.leave.abort"] = "1  Quit job (fail)",
        ["town.leave.swap"] = "2  Hand off to an NPC",
        ["town.leave.cancel"] = "Cancel",
        ["town.leave.header"] = "Self shop labor · leave",
        ["town.leave.noCandidates"] = "No free companion to take over",
        ["town.leave.swapOk"] = "{0} takes over \"{1}\".",
        ["town.leave.swapFail"] = "Hand-off failed",
        ["town.leave.abortOk"] = "You quit \"{0}\".",

        ["dispatch.settle.success"] = "success",
        ["dispatch.settle.fail"] = "failed",
        ["dispatch.settle.recall"] = "recalled",
        ["dispatch.settle.end"] = "ended",
        ["dispatch.settle.line"] = "{0}'s {1} ({2}) {3}.",
        ["dispatch.fail.hint.dungeon"] = "Success might have brought boss spoils and fame.",
        ["dispatch.fail.hint.region"] = "Success might have brought a fuller haul.",

        // ---- common ----
        ["common.cancel"] = "Cancel",

        // ---- co-craft ----
        ["co.mode.off"] = "Off",
        ["co.mode.auto"] = "Auto",
        ["co.mode.autoName"] = "Auto · {0}",
        ["co.mode.autoUnknown"] = "Auto(?)",
        ["co.mode.you"] = "(you)",
        ["co.tag.party"] = "P",
        ["co.tag.home"] = "H",
        ["co.prefix.hobby"] = "Hobby",
        ["co.prefix.work"] = "Work",
        ["co.menu.noAssist"] = "No assist",
        ["co.menu.noOne"] = "No one available",
        ["co.menu.skillShort"] = "sk",
        ["co.msg.started"] = "{0} is crafting with you.",
        ["co.msg.assistantFallback"] = "assistant",
        ["co.msg.gainedExp"] = "{0} got more skilled at {1}.",
        ["co.msg.ended"] = "{0} ended.",
        ["co.msg.autoSet"] = "Auto-assist enabled.",
        ["co.msg.off"] = "Assist disabled.",
        ["co.msg.pinned"] = "{0} will assist.",
        ["co.msg.pinFailed"] = "Chosen assistant unavailable; assist disabled.",

        // ---- process ----
        ["proc.mode.self"] = "Self",
        ["proc.mode.selfYou"] = "Yourself",
        ["proc.mode.auto"] = "Auto",
        ["proc.mode.autoNone"] = "Auto (none)",
        ["proc.menu.selfProcess"] = "Process yourself",
        ["proc.menu.pickBest"] = "Pick the best fit",
        ["proc.menu.noOne"] = "No one available",
        ["proc.menu.skillShort"] = "sk",
        ["proc.msg.selfMode"] = "You will process it yourself.",
        ["proc.msg.autoSet"] = "Auto: {0} now. Fill the grid to start.",
        ["proc.msg.autoNone"] = "Auto: no one suitable nearby right now.",
        ["proc.msg.pinned"] = "{0} will process it. Fill the grid to start.",
        ["proc.msg.pinFailed"] = "Chosen worker unavailable; switched back to yourself.",
        ["proc.msg.busy"] = "This machine is already processing.",
        ["proc.msg.materialIncomplete"] = "Ingredients are incomplete.",
        ["proc.msg.noWorker"] = "No worker available; process yourself or pick again.",
        ["proc.msg.notEnoughMaterial"] = "Not enough ingredients.",
        ["proc.msg.changed"] = "Ingredients changed when starting.",
        ["proc.msg.cannotTake"] = "Could not take the ingredients.",
        ["proc.msg.cannotOperate"] = "{0} doesn't know this machine.",
        ["proc.msg.start"] = "{0} starts processing on {1} ({2} uses).",
        ["proc.msg.residentFallback"] = "resident",
        ["proc.msg.machineFallback"] = "processor",
        ["proc.msg.doneSkilled"] = "{0} finished {1} and is more skilled now.",
        ["proc.msg.done"] = "{0} finished {1}.",
        ["proc.msg.interrupted"] = "{0}'s {1} was interrupted.",
        ["proc.msg.stopped"] = "{0} stopped {1}.",
        ["proc.msg.noFuel"] = "Out of fuel; {0} stopped.",
        ["proc.msg.noMaterial"] = "Materials ran out; {0} stopped.",
        ["proc.msg.noMachine"] = "The machine is gone; {0} stopped.",
        ["proc.msg.stuck"] = "The path is blocked; {0} stopped.",
        ["proc.msg.exhausted"] = "{0} is exhausted and stopped.",
        ["proc.msg.ended"] = "{0}'s {1} is done.",
        ["proc.msg.zoneContinue"] = "{0} stays on the old map and keeps working.",
        ["proc.msg.zoneResume"] = "{0} resumes processing ({1} left).",
        ["proc.msg.leftoverGround"] = "Your pack cannot hold it; the leftovers are stacked by {0} (not auto-picked).",

        // ---- process jobs list (multi-task) ----
        ["proc.ui.jobsTitle"] = "Processing jobs",
        ["proc.ui.jobsNone"] = "No processing jobs",
        ["proc.ui.jobsIdle"] = "Unassigned",
        ["proc.ui.jobsSuspended"] = "Away",
        ["proc.ui.jobsReturning"] = "Returning materials",
        ["proc.ui.jobsProgress"] = "{0}/{1}",
        ["proc.ui.jobsEtaHours"] = "{0}h left",
        ["proc.ui.jobsCancelDone"] = "Cancelled processing on {0}.",
        ["proc.ui.jobsWorkerBusy"] = "{0} is working another machine.",

        // ---- craft UI ----
        ["craft.effLine"] = "{0} / Eff {1}",
        ["craft.effLineSkill"] = "{0} {1} / Eff {2}",

        // ---- dispatch ----
        ["dis.error.invalidTarget"] = "Invalid target",
        ["dis.error.notUnlocked"] = "{0} not unlocked",
        ["dis.error.tooManyTarget"] = "At most {0} per target",
        ["dis.error.tooManyDungeon"] = "At most {0} per dungeon",
        ["dis.error.cannotSend"] = "{0} can't be sent",
        ["dis.error.noOne"] = "No one to send",
        ["dis.error.regionBusy"] = "A {0} is already in progress there",
        ["dis.error.dungeonBusy"] = "A {0} is already in progress there",
        ["dis.error.needBase"] = "A base is required",
        ["dis.error.regionTile"] = "No enterable tile of that region nearby",
        ["dis.error.dungeonNo"] = "Can't {0} this dungeon",
        ["dis.start.region"] = "{0} set out on {1}: {2} ({3} people · explore {4}w)",
        ["dis.start.dungeon"] = "{0} set out on {1}: {2} ({3} people · ~{4} days)",
        ["dis.error.startFail"] = "Failed to start: {0}",
        ["dis.loot.none"] = "No haul yet",
        ["dis.parcel.harvest"] = "Dispatch harvest",
        ["dis.parcel.partial"] = "Dispatch partial harvest",
        ["dis.parcel.recall"] = "Dispatch recalled",
        ["dis.parcel.loot"] = "Dispatch spoils",
        ["dis.parcel.package"] = "Dispatch parcel",
        ["dis.loot.item"] = "item",
        ["dis.loot.noMission"] = "No mission",
        ["dis.loot.noPlan"] = "No planned haul",
        ["dis.loot.moreKinds"] = "...{0} kinds in all",
        ["dis.loot.totalLine"] = "{0} ({1} items x{2})",
        ["dis.loot.etcItems"] = "{0} ({1} items in all)",
        ["dis.loot.planFail"] = "Plan read failed: {0}",
        ["dis.loot.special"] = "special",
        ["dis.debug.nonRegion"] = "Non-region mission (computed on success) · tracking: ",
        ["dis.debug.harvestDump"] = "Dispatch success plan haul x{0}: ",
        ["dis.debug.output"] = "Printed {0} mission plan hauls",
        ["dis.debug.noMission"] = "No dispatch missions right now",
        ["dis.debug.forceDone"] = "Force-completed {0} dispatch(es) (success haul mailed)",
        ["dis.debug.notFound"] = "Dispatch mission {0} not found",
        ["dis.debug.forceDoneOne"] = "Force-completed dispatch #{0} {1}",
        ["dis.exp.explore"] = "explore",
        ["dis.exp.lockpick"] = "lockpick",
        ["dis.exp.gather"] = "gather",
        ["dis.exp.dig"] = "digging",
        ["dis.exp.lumber"] = "lumberjack",
        ["dis.exp.tunnel"] = "tunneling",
        ["dis.exp.zero"] = "{0}+0",
        ["dis.exp.gain"] = "{0}+{1} (lv{2} to {3})",
        ["dis.compass.e"] = "East",
        ["dis.compass.w"] = "West",
        ["dis.compass.s"] = "South",
        ["dis.compass.n"] = "North",
        ["dis.compass.near"] = "nearby",
        ["dis.yield.rich"] = "rich",
        ["dis.yield.medium"] = "medium",
        ["dis.yield.normal"] = "normal",
        ["dis.yield.meager"] = "meager",
        ["dis.stat.none"] = "none selected",
        ["dis.stat.line"] = "Power {0}  Explore {1}  Lockpick {2}  Gather {3}",
        ["dis.stat.lineChance"] = "Power {0}  Explore {1}  Lockpick {2}  Gather {3}  {4}%",
        ["dis.region.name.plain"] = "Plains",
        ["dis.region.name.forest"] = "Forest",
        ["dis.region.name.beach"] = "Beach",
        ["dis.region.name.mountain"] = "Mountain",
        ["dis.region.name.area"] = "Area",
        ["dis.dist"] = "{0} {1}d",
        ["dis.distDanger"] = "{0} {1}d danger{2}",
        ["dis.kind.random"] = "random",
        ["dis.kind.fixed"] = "fixed",
        ["dis.kind.randomDungeon"] = "random dungeon",
        ["dis.kind.fixedDungeon"] = "fixed dungeon",
        ["dis.preview.region"] = "{0}\n{1}  {2}\n{3} people · {4}\nExplore {5} weeks\nExplore {6}  Lockpick {7}  Gather {8}",
        ["dis.preview.dungeon"] = "{0}\n{1}  Danger {2}  {3}\n{4} people · {5}\n~{6} days · Success {7}%\nPower {8}  Explore {9}  Lockpick {10}  Gather {11}",
        ["dis.info.dungeon"] = "{0} ({1}) {2} Danger {3} · ~{5}d",
        ["dis.info.region"] = "{0} (region) {1} · explore {2} weeks",
        ["dis.msg.baseOnly"] = "{0} can only be used at a base.",
        ["dis.msg.busyTalk"] = "{0} is exploring and can't talk.",
        ["dis.msg.busyInteract"] = "{0} is exploring and can't be interacted with.",
        ["dis.msg.isIn"] = "{0} is in {1}.",
        ["dis.msg.empty"] = "No {0}/{1} right now",
        ["dis.ui.activeHeader"] = "— Active missions —",
        ["dis.ui.randomHeader"] = "— {0} · Random —",
        ["dis.ui.fixedHeader"] = "— {0} · Fixed —",
        ["dis.ui.noTargets"] = "No known targets nearby",
        ["dis.ui.recallPartial"] = "Recall (partial rewards)",
        ["dis.ui.recallFail"] = "Recall failed",
        ["dis.ui.progress"] = "{0}%  {1}d left",
        ["dis.ui.taskHeader"] = "Mission · {0}",
        ["dis.ui.weeks"] = "Explore {0} weeks",
        ["dis.ui.chooseWeeks"] = "{0} · Choose explore weeks",
        ["dis.ui.noLastTeam"] = "No previous team",
        ["dis.ui.noAutoPick"] = "No members to auto-pick",
        ["dis.ui.noCandidates"] = "No residents or allies to send",
        ["dis.ui.confirmSel"] = "Confirm ({0}/{1})",
        ["dis.ui.lastTeam"] = "Last team",
        ["dis.ui.maxHarvest"] = "Max haul",
        ["dis.ui.maxCombat"] = "Max combat haul",
        ["dis.ui.partyTag"] = "Party",
        ["dis.ui.restoreLast"] = "Restore last team",
        ["dis.ui.autoPickGather"] = "Auto-pick by explore/gather",
        ["dis.ui.autoPickPower"] = "Auto-pick by combat power",
        ["dis.ui.pickHeader"] = "{0} · Pick members (multi, max {1})",
        ["dis.ui.pickHeaderWeeks"] = "{0} · explore {1} weeks · pick (max {2})",
        ["dis.ui.tooMany"] = "At most {0} per dungeon",
        ["dis.ui.pickFirst"] = "Pick members first",
        ["dis.ui.invalidChoice"] = "Invalid choice",
        ["dis.ui.confirmSend"] = "Confirm dispatch",
        ["dis.ui.busySuffix"] = " (in progress)",
        ["dis.ui.inProgress"] = "In progress",
        ["dis.q.ended"] = " (ended)",
        ["dis.q.members"] = "Members: {0}",
        ["dis.q.position"] = "Position: {0}",
        ["dis.q.progress"] = "Progress {0}%",
        ["dis.q.left"] = "{0}d left",
        ["dis.q.harvest"] = "Haul: {0}",
        ["dis.q.regionPos"] = "{0} · {1} weeks",
        ["dis.q.target"] = "target",
        ["dis.q.entrance"] = "entrance",
        ["dis.q.area"] = "area",
        ["dis.q.plan"] = "Planned haul: {0}",

        // ---- town labor ----
        ["town.error.invalidJob"] = "Invalid job",
        ["town.error.noTownWork"] = "No {0} at this base",
        ["town.error.invalidPlace"] = "Invalid location",
        ["town.error.busy"] = "You're busy",
        ["town.error.busyCoCraft"] = "Currently co-crafting",
        ["town.error.clientBusy"] = "This client already has {0} in progress",
        ["town.error.maxConcurrent"] = "At most {0} {1} in town at once",
        ["town.error.jobGone"] = "The client's post is gone",
        ["town.error.noSp"] = "Too tired; rest before working.",
        ["town.error.workerBusy"] = "Worker busy",
        ["town.error.cannotAssign"] = "{0} can't be assigned",
        ["town.msg.youStart"] = "You head to {0} for \"{1}\" (~{2}h).",
        ["town.msg.workerStart"] = "{0} heads to {1} for \"{2}\" (~{3}h).",
        ["town.msg.youTired"] = "Exhausted; stopped \"{0}\".",
        ["town.msg.workStart"] = "You start \"{1}\" at {0}.",
        ["town.msg.paused"] = "The shift at {0} is paused. Walk up and use the middle-click menu to resume.",
        ["town.msg.endedShort"] = "That {0} ended",
        ["town.msg.stopFail"] = "Couldn't stop",
        ["town.msg.recallFail"] = "Recall failed",
        ["town.msg.jobInvalid"] = "Invalid job",
        ["town.msg.clientGone"] = "Client is away",
        ["town.msg.noCandidates"] = "No free residents or allies",
        ["town.msg.invalidChoice"] = "Invalid choice",
        ["town.msg.town"] = "shop",
        ["town.msg.townName"] = "town",
        ["town.msg.worker"] = "worker",
        ["town.msg.companion"] = "companion",
        ["town.msg.client"] = "client",
        ["town.progress.arriving"] = "Heading to work...",
        ["town.progress.line"] = "{0}%  {1}h left",
        ["town.ui.confirmStart"] = "Confirm start",
        ["town.ui.sendConfirm"] = "Send {0} to \"{1}\"",
        ["town.ui.hours"] = "~{0}h",
        ["town.ui.choosePick"] = "{0} · pick 1 (~{1}h)",
        ["town.ui.skillFallback"] = "skill",
        ["town.ui.partyTag"] = "Party",
        ["town.ui.busyTag"] = "{0} in progress",
        ["town.ui.youTag"] = "· you",
        ["town.ui.needHelp"] = "The shop is busy and needs help.",
        ["town.ui.inProgress"] = "In progress",
        ["town.ui.header"] = "{0} · {1} · {2}",
        ["town.reward.hint"] = "Pay: about gold x{0}",
        ["town.reward.line"] = "Reward {0}",
        ["town.reward.moneyOnly"] = "Pay: gold x{0}",
        ["town.busy.invalid"] = "Invalid character",
        ["town.busy.town"] = "{0} in progress",
        ["town.busy.dungeon"] = "{0} in progress",
        ["town.busy.process"] = "{0} in progress",
        ["town.busy.coCraft"] = "Currently assisting craft",
        ["town.busy.craft"] = "Currently working at base production",

        ["craft.note.missing"] = "needs {0}",
        ["craft.note.notHome"] = "only works at your own base",
        ["craft.note.awayWorker"] = "resident is not at the base",
        ["craft.note.busy"] = "resident is busy elsewhere",
        ["craft.note.stocked"] = "stocked ({0})",
        ["craft.note.noBench"] = "no {0} at the base - place one first",
        ["craft.note.failed"] = "craft failed",
        ["craft.note.working"] = "working at the bench",
        ["craft.note.walking"] = "walking to the bench",
        ["craft.note.interrupted"] = "craft interrupted, ingredients returned",
        ["craft.note.workerGone"] = "resident is gone",
        ["craft.msg.missing"] = "{0}'s standing order is short of {1}",
        ["craft.msg.waiting"] = "Base production: {0} order(s) stalled ({1})",
        ["craft.msg.running"] = "Base production: {0} order(s) running ({1})",
        ["craft.msg.cancelled"] = "Cancelled {0}'s standing order",
        ["craft.state.none"] = "No production assignments.",

        ["craft.ui.assign"] = "Assign a resident",
        ["craft.ui.pickWorker"] = "Who makes it",
        ["craft.ui.pickRecipe"] = "What to make",
        ["craft.ui.pickMode"] = "How much",
        ["craft.ui.mode.count"] = "Make a set number",
        ["craft.ui.mode.keep"] = "Keep the stock topped up",
        ["craft.ui.mode.endless"] = "Make without stopping",
        ["craft.ui.pickTarget"] = "Amount",
        ["craft.ui.noStation"] = "No station needed",
        ["craft.ui.noStationAvailable"] = "No usable station at this base - place a crafting tool first",
        ["craft.ui.cancelOrder"] = "Cancel standing order: {0}",
        ["craft.ui.noWorker"] = "No resident at this base can be assigned",
        ["craft.ui.page"] = "Page {0}/{1}",
        ["craft.ui.next"] = "Next page",
        ["craft.ui.prev"] = "Previous page",
        ["craft.ui.materials"] = "Needs: {0}",
        ["craft.ui.materialOk"] = "Materials ready",
        ["craft.ui.skill"] = "Skill {0} (wants {1})",
        ["craft.ui.cancel"] = "Cancel this assignment",
        ["town.talk.busy"] = "{0} is busy with {1} and can't talk.",
        ["town.talk.noInteract"] = "{0} is busy with {1} and can't be interacted with.",

        // ---- job catalog ----
        ["job.inn.title"] = "Inn Chore",
        ["job.inn.skill"] = "Carrying/Chores",
        ["job.inn.r1"] = "The town is crowded lately; rooms and the hall are overrun. I'm busy with guests — please tidy the rooms and take care of the tables and linens downstairs.",
        ["job.inn.r2"] = "Adventurers are packing the inn, footsteps all night. I can't get away — send someone quick to make beds, pour water, and tidy the halls.",
        ["job.inn.r3"] = "Regulars arrive tomorrow and today is tight. I usually handle the chores myself but can't right now — please cover the shop's odd jobs for a while.",
        ["job.inn.r4"] = "Dishes and luggage are piled up and the front desk is queued. I'm swamped — have someone tidy the rooms and common areas until we can host again.",
        ["job.kitchen.title"] = "Kitchen Help",
        ["job.kitchen.skill"] = "Cooking",
        ["job.kitchen.r1"] = "The dinner rush is here and prep isn't done. I normally prep and wash myself but can't step away — lend a hand in the kitchen.",
        ["job.kitchen.r2"] = "The stove is blazing and dishes are piling up. I'm on the mains — send someone to wash pots, pass plates, and watch the pickup window.",
        ["job.kitchen.r3"] = "Orders are up and the kitchen can't keep pace. No cooking needed — just hold the line on chores so plates keep going out.",
        ["job.kitchen.r4"] = "Short-handed suddenly, with stocks and sides backed up. Have a careful person prep and wash; we just need to get through this stretch.",
        ["job.general.title"] = "General Clerk",
        ["job.general.skill"] = "Bartering",
        ["job.general.r1"] = "The shelves are messy and customers are queuing at the door. I need to check stock — watch the counter, straighten goods, and greet regulars.",
        ["job.general.r2"] = "New stock hasn't been shelved and no one can leave the floor. I usually do it myself but can't now — send someone to help.",
        ["job.general.r3"] = "Too many customers for one person lately. Find someone clear-spoken to mind the shop — if unsure of a price, call me.",
        ["job.general.r4"] = "Chores are piling up — ropes, candles, jars all need rearranging. I'm on the books; straighten the shelves and handle the counter.",
        ["job.smith.title"] = "Smith's Apprentice",
        ["job.smith.skill"] = "Smithing",
        ["job.smith.r1"] = "The forge is hot and orders are backed up. I can't step away — work the bellows, hand me tongs, and gather the filings.",
        ["job.smith.r2"] = "Forging is booked solid today. No hammering needed — pass tools, clear the bench, and move with the rhythm.",
        ["job.smith.r3"] = "Pieces and scrap cover the anvil bench. I usually clean up myself but I'm rushing orders — send an assistant.",
        ["job.smith.r4"] = "Short an extra hand; the bellows and scrap are slipping. Find someone steady — and warn them the metal is hot.",
        ["job.food.title"] = "Food Clerk",
        ["job.food.skill"] = "Cooking/Trading",
        ["job.food.r1"] = "The morning stock just came in and the scale is busy. I can't step away — arrange the goods and serve customers at the scale.",
        ["job.food.r2"] = "More buyers than usual; one person can't cover the counter. Send someone to set out goods and weigh, stowing the ugly ones behind.",
        ["job.food.r3"] = "Fresh goods sell fast and the baskets aren't sorted. I usually do it myself but can't now — have someone help.",
        ["job.food.r4"] = "The counter is busy and deliveries are backed up. Put the food out front, greet buyers warmly, and ride out the rush.",
        ["job.book.title"] = "Bookstore Clerk",
        ["job.book.skill"] = "Reading",
        ["job.book.r1"] = "I'm short hands for shelving. I normally sort and number myself but can't now — put the new books on their shelves so customers can find them.",
        ["job.book.r2"] = "Are you a seasoned traveler? Lately many ask after old books and maps — mind the counter and gather the stray volumes.",
        ["job.book.r3"] = "New books are unpacked halfway and the counter is queued. I can't cover both — straighten the shelves and jot down price queries.",
        ["job.book.r4"] = "Someone mixed the specialist books with the leisure ones. I'm busy restocking — have a careful person re-shelve without soiling the covers.",
        ["job.scholar.title"] = "Scholar's Aide",
        ["job.scholar.skill"] = "Memory",
        ["job.scholar.r1"] = "I need a copyist for my research. I usually organize it myself but can't now — transcribe a few records and file the documents.",
        ["job.scholar.r2"] = "Guild work is piling up — rosters and receipts need review. While I'm away, sort the files by type and don't lose the seal.",
        ["job.scholar.r3"] = "Processions are endless lately and the desk is overwhelmed. Send a steady person to register, relay messages, and manage the queue.",
        ["job.scholar.r4"] = "I have an urgent document to verify. I'd do it myself but can't today — copy the appendix cleanly and file it by number.",

        // Board titles: shop identity, not generic clerk labels.
        ["job.shop.fish.need"] = "Fish shop needs help",
        ["job.shop.meat.need"] = "Butcher needs help",
        ["job.shop.fruit.need"] = "Fruit shop needs help",
        ["job.shop.bread.need"] = "Bakery needs help",
        ["job.shop.milk.need"] = "Dairy shop needs help",
        ["job.shop.booze.need"] = "Liquor shop needs help",
        ["job.shop.food.need"] = "Food shop needs help",
        ["job.shop.inn.need"] = "Inn needs help",
        ["job.shop.kitchen.need"] = "Kitchen needs help",
        ["job.shop.book.need"] = "Bookstore needs help",
        ["job.shop.scholar.need"] = "Desk needs help",
        ["job.shop.smith.need"] = "Smithy needs help",
        ["job.shop.general.need"] = "General store needs help",
        ["job.shop.junk.need"] = "Junk shop needs help",
        ["job.shop.souvenir.need"] = "Souvenir shop needs help",
        ["job.shop.generic.need"] = "{0} needs help",

        // ModConfigGUI tab. AddToggle runs its caption through ToTitleCase, so these are
        // written in title case already.
        ["cfg.title"] = "NPC Labor - Settings",
        ["cfg.hint"] = "Changes apply immediately and are saved to labor_config.json.",
        ["cfg.sec.features"] = "Features",
        ["cfg.feat.enabled"] = "Master Switch",
        ["cfg.feat.dispatch"] = "Dispatch",
        ["cfg.feat.townLabor"] = "Town Labor",
        ["cfg.feat.craft"] = "Base Production",
        ["cfg.sec.town"] = "Town Labor",
        ["cfg.town.minWage"] = "Min Wage Per Hour",
        ["cfg.town.expPerHour"] = "Exp Per Hour",
        ["cfg.town.ticket"] = "Furniture Ticket Chance (%)",
        ["cfg.town.hoursMin"] = "Min Hours",
        ["cfg.town.hoursMax"] = "Max Hours",
        ["cfg.town.headroom"] = "Town Invest Headroom",
        ["cfg.town.platBase"] = "Platinum Base",
        ["cfg.town.platPerTier"] = "Platinum Per Invest Tier",
        ["cfg.town.tierSize"] = "Invest Tier Size",
        ["cfg.town.affinity"] = "Affinity Base",
        ["cfg.sec.dispatch"] = "Dispatch",
        ["cfg.dis.lockpickPerChest"] = "Lockpicks Per Chest",
        ["cfg.dis.lockpickMaxChests"] = "Lockpick Max Chests",
        ["cfg.dis.bossMult"] = "Boss Reward Multiplier",
        ["cfg.dis.specialtyMult"] = "Region Specialty Multiplier",
        ["cfg.dis.expExploreSuccess"] = "Explore Success Exp",
        ["cfg.dis.expExploreFailure"] = "Explore Failure Exp",
        ["cfg.dis.expExploreRecall"] = "Explore Recall Exp",
        ["cfg.dis.expLockpickSuccess"] = "Lockpick Success Exp",
        ["cfg.dis.expLockpickFailure"] = "Lockpick Failure Exp",
        ["cfg.dis.expLockpickRecall"] = "Lockpick Recall Exp",
        ["cfg.dis.expGatherSuccess"] = "Gather Success Exp",
        ["cfg.dis.expGatherFailure"] = "Gather Failure Exp",
        ["cfg.dis.expGatherRecall"] = "Gather Recall Exp",
        ["cfg.dis.fameBase"] = "Fame Base",
        ["cfg.dis.famePerDanger"] = "Fame Per Danger",
        ["cfg.sec.process"] = "Process",
        ["cfg.proc.speed"] = "Process Speed Multiplier",
        ["cfg.sec.unlock"] = "Unlock Thresholds",
        ["cfg.unlock.region"] = "Region Outing Base Lv",
        ["cfg.unlock.dungeon"] = "Dungeon Explore Base Lv",
        ["cfg.disabled"] = "This feature is turned off in the mod settings",
        ["cfg.sec.debug"] = "Debug",
        ["cfg.dbg.pdispHint"] = "Permanent posts settle per whole month; use these in testing to fast-forward or settle at once.",
        ["cfg.dbg.pdispMonth"] = "Permanent post: advance 1 month",
        ["cfg.dbg.pdispMonthDone"] = "Advanced {0} permanent post(s) by 1 month.",
        ["cfg.dbg.pdispFinish"] = "Permanent post: finish & settle now",
        ["cfg.dbg.pdispFinishDone"] = "Settled {0} permanent post(s); rewards mailed home.",

        // ---- Permanent dispatch (2026-10-28) ----
        ["term.pdispatch"] = "Permanent posts",
        ["pdisp.name.fishing"] = "Fishing",
        ["pdisp.name.coach"] = "Coach",
        ["pdisp.name.patrol"] = "Watch patrol",
        ["pdisp.name.lookout"] = "Lookout",
        ["pdisp.name.winery"] = "Winery hand",
        ["pdisp.name.hunter"] = "Hunter",
        ["pdisp.name.market"] = "Market hand",
        ["pdisp.name.broker"] = "Broker",
        ["pdisp.name.farm"] = "Rice paddy hand",
        ["pdisp.name.farm_dry"] = "Field hand",
        ["pdisp.name.altar"] = "Shrine attendant",
        ["pdisp.name.painter"] = "Street painter",
        ["pdisp.name.tutor"] = "Magic tutor",
        ["pdisp.name.concert"] = "Court concert",
        ["pdisp.name.gather"] = "Forage",
        ["pdisp.name.inn"] = "Inn kitchen hand",
        ["pdisp.name.church"] = "Church hand",
        ["pdisp.name.porter"] = "Porter",
        ["pdisp.name.children"] = "Caretaker",
        ["pdisp.name.forge"] = "Gunsmith's hand",

        // ---- commission text (board detail), in the voice of the local official ----
        ["pdisp.desc.pd_fishing"] = "The port is short-handed. A boat comes in and someone has to sort the catch and weigh it — that is the whole day gone. If you can handle a net, ask for the foreman down at the harbour.",
        ["pdisp.desc.pd_coach_fighter"] = "The new lot in the guild can barely hold a sword. I cannot spare the time myself — is there anyone who could drill them for me?",
        ["pdisp.desc.pd_patrol"] = "This town behaves itself in daylight. Nights are another matter. One more pair of boots on the street is what lets the shopkeepers bolt their doors.",
        ["pdisp.desc.pd_lookout"] = "The watch has been poking around lately, so someone has to keep an eye on the alley mouth. Give a shout if anything looks wrong — and do not get noticed doing it.",
        ["pdisp.desc.pd_winery"] = "Harvest week is the worst of it. The guests at the springs are still waiting on me and I cannot be in two places.",
        ["pdisp.desc.pd_hunter"] = "The beasts have started coming right up to the edge of town. Someone has to go up there and thin them out before they hurt a person or the livestock.",
        ["pdisp.desc.pd_market"] = "Unloading, raising the stalls, packing them away — those are the three worst stretches of the day. I need a pair of legs.",
        ["pdisp.desc.pd_broker"] = "More caravans than we have people to carry word between them. Settle the price and still leave the man his face — not everyone can do it.",
        ["pdisp.desc.pd_farm"] = "The paddies are due for water and one pair of legs cannot keep up with the plots. Planting, watching the water, cutting — every stage is short of hands.",
        ["pdisp.desc.pd_farm_yowyn"] = "The beds come in one after another — clear one and the next wants turning. Help me turn the soil and water them and it saves me a week.",
        ["pdisp.desc.pd_altar_foxtown"] = "Nobody has looked after this little altar properly in years. Go every day and the god will notice.",
        ["pdisp.desc.pd_painter"] = "People on the streets of Lumiest keep asking to have their likeness drawn. We need someone who can hold a brush — set up a stand and start.",
        ["pdisp.desc.pd_tutor_mage"] = "Three apprentices more than last year for every one teacher. We have not kept up.",
        ["pdisp.desc.pd_concert"] = "The court musician has taken a long leave and guests are coming. The music cannot stop. We need someone who can stand in.",
        ["pdisp.desc.pd_gather"] = "The hill harvest is gone in a few weeks. Fewer people know the paths now, and someone always brings back something that should not be eaten.",
        ["pdisp.desc.pd_inn"] = "We are kneading and laying out the day's food before light, and one pair of hands cannot turn fast enough. The kitchen needs someone quick.",
        ["pdisp.desc.pd_shrine_nefu"] = "Travellers come through all day, so the lamp at the shrine cannot go out even once. Someone has to see to it.",
        ["pdisp.desc.pd_church"] = "The clergy cannot keep up, and the odd jobs are no dignified work — but they have to be done. If you are willing, start with the morning bell.",
        ["pdisp.desc.pd_porter"] = "The caravan does not wait for sunrise — the load comes off the moment it lands. Come if your back is up to it. Paid by the day.",
        ["pdisp.desc.pd_children"] = "I have watched over them a long time. Now it is your turn. If you do it badly, I will know.",
        ["pdisp.desc.pd_forge"] = "Barrels to true, rivets to drive — every step finer than the last. An apprentice will not do. I need a practised hand.",
        ["pdisp.desc.pd_altar_aquli"] = "I hear what this place is saying. Nobody else does. Stay, if you are willing to learn it.",

        ["pdisp.reward.townInfluence"] = "town influence",
        ["pdisp.ui.header"] = "{0} · {1}",
        ["pdisp.ui.activeRow"] = "{0} ({1}, {2} month(s))",
        ["pdisp.ui.activeSub"] = "{0} · {1} whole month(s) settled",
        ["pdisp.ui.recall"] = "Recall & settle",
        ["pdisp.ui.close"] = "Close",
        ["pdisp.ui.noTask"] = "No permanent posts here.",
        ["pdisp.ui.noCandidate"] = "Nobody can take \"{0}\".",
        ["pdisp.ui.gate"] = "{0}>{1}",
        ["pdisp.ui.guildGate"] = "{0} rank>{1}",
        ["pdisp.ui.inProgress"] = "Active",
        ["pdisp.ui.served"] = "{0} whole month(s) settled",
        ["pdisp.ui.nextSettle"] = "next settlement in {0}",
        ["pdisp.reward.develop"] = "town development",
        ["pdisp.reward.influence"] = "local influence",
        ["pdisp.reward.indulgence"] = "indulgence (rare)",
        ["pdisp.reward.faith"] = "faith bonus",
        ["pdisp.reward.godweapon"] = "a weapon of the god's liking (miracle grade)",
        ["pdisp.reward.blessed"] = "blessed {0}",
        ["pdisp.guild.fighter"] = "Fighters Guild",
        ["pdisp.guild.thief"] = "Thieves Guild",
        ["pdisp.guild.mage"] = "Mages Guild",
        ["pdisp.guild.merchant"] = "Merchants Guild",
        ["pdisp.err.invalid"] = "Invalid post.",
        ["pdisp.err.noZone"] = "Cannot find the location for \"{0}\".",
        ["pdisp.err.busy"] = "{0} is already busy.",
        ["pdisp.err.defBusy"] = "Someone already holds \"{0}\".",
        ["pdisp.err.gate"] = "{0}'s skill is not enough for \"{1}\".",
        ["pdisp.err.guildRank"] = "\"{0}\" requires your rank 2 in the matching guild.",
        ["pdisp.err.recallFail"] = "Recall failed.",
        ["pdisp.msg.start"] = "{0} starts working as {1} in {2}.",
        ["pdisp.msg.settle"] = "\"{1}\" ended: {0} served {2} whole month(s). Rewards {3}",
        ["pdisp.msg.mailed"] = "mailed to your home mailbox.",
        ["pdisp.msg.recalled"] = " (recalled early)",
        ["pdisp.msg.settleNone"] = "\"{1}\" ended: {0} served under one month. Nothing to settle{2}",
        ["pdisp.msg.noPay"] = " (none)",
    };


    internal static string T(string key)
    {
        Dictionary<string, string> primary = PickTable();
        if (primary.TryGetValue(key, out string? s) && !string.IsNullOrEmpty(s))
        {
            return s;
        }

        if (primary != En && En.TryGetValue(key, out s) && !string.IsNullOrEmpty(s))
        {
            return s;
        }

        if (Cn.TryGetValue(key, out s) && !string.IsNullOrEmpty(s))
        {
            return s;
        }

        return key;
    }

    internal static string T(string key, params object[] args)
    {
        string fmt = T(key);
        if (args == null || args.Length == 0)
        {
            return fmt;
        }

        try
        {
            return string.Format(fmt, args);
        }
        catch
        {
            return fmt;
        }
    }

    // Language code the current table was picked for. Changing the in-game
    // language requires a restart (Lang.Init runs at boot), so caching the
    // picked table is safe and keeps T() off the reflection hot path.
    static string? _pickedLang;
    static Dictionary<string, string>? _picked;

    static Dictionary<string, string> PickTable()
    {
        // Trust Lang.langCode alone: Lang.Init assigns it straight from the
        // Lang/ directory name ("EN", "JP", "CN", community packs like "RUS").
        // The isEN/isJP bools are derived from the same string, and flag names
        // like isCN/isZH do not exist in the game - probing them was dead work.
        string code = (ProbeLangCode() ?? string.Empty).Trim().ToLowerInvariant();
        if (_pickedLang == code && _picked != null)
        {
            return _picked;
        }

        Dictionary<string, string> table;
        if (code == "jp" || code == "ja" || code.StartsWith("jp") || code.StartsWith("ja")
            || code.Contains("japan"))
        {
            table = Ja;
        }
        else if (code == "cn" || code == "zh" || code == "hans"
            || code.StartsWith("cn") || code.StartsWith("zh")
            || code.Contains("chinese") || code.Contains("hans"))
        {
            // Explicit CN/ZH so a Chinese UI is never swept into the EN default.
            table = Cn;
        }
        else if (code == "rus" || code == "ru" || code.Contains("russian"))
        {
            // Community Russian pack (Lang/ directory code "RUS"). Russian is
            // a supported language with its own table; keys T() cannot find
            // there fall back to En, so RU players read English for now.
            table = Ru;
        }
        else if (code == "en" || code.StartsWith("en") || code.Contains("english"))
        {
            table = En;
        }
        else
        {
            // Any other UI language the mod does not ship (DE/FR/KO/...):
            // English too. Never default to Chinese here - a non-CN UI that
            // is not JP must read EN, not CN.
            table = En;
        }

        _pickedLang = code;
        _picked = table;
        return table;
    }

    static string? ProbeLangCode()
    {
        // Try common static members on Lang.
        try
        {
            Type? langType = typeof(EClass).Assembly.GetType("Lang")
                ?? Type.GetType("Lang");
            if (langType != null)
            {
                foreach (string name in new[] { "langCode", "lang", "id", "current", "Current" })
                {
                    try
                    {
                        var f = langType.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        if (f != null)
                        {
                            object? v = f.GetValue(null);
                            string? s = v as string ?? v?.ToString();
                            if (!string.IsNullOrEmpty(s))
                            {
                                return s;
                            }
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        var prop = langType.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        if (prop != null)
                        {
                            object? v = prop.GetValue(null, null);
                            string? s = v as string ?? v?.ToString();
                            if (!string.IsNullOrEmpty(s))
                            {
                                return s;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }

        // Fallback: core config.lang if present.
        try
        {
            object? core = typeof(EClass).GetField("core", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null);
            if (core != null)
            {
                object? cfg = core.GetType().GetField("config")?.GetValue(core)
                    ?? core.GetType().GetProperty("config")?.GetValue(core, null);
                if (cfg != null)
                {
                    object? lang = cfg.GetType().GetField("lang")?.GetValue(cfg)
                        ?? cfg.GetType().GetProperty("lang")?.GetValue(cfg, null);
                    string? s = lang as string ?? lang?.ToString();
                    if (!string.IsNullOrEmpty(s))
                    {
                        return s;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }
}
