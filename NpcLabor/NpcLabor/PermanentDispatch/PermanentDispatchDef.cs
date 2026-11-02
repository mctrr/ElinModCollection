using System;
using System.Collections.Generic;

namespace NpcLabor.PermanentDispatch;

/// <summary>
/// One permanent dispatch task ("常驻派遣"): a town-anchored long posting a
/// companion can hold for whole months. Design settled 2026-10-28 — see
/// .workbuddy/memory/2026-10-28.md rounds 7-12.
///
/// Gates:
/// - Town tasks: the dispatched NPC's skill must EXCEED <see cref="GateLevel"/>.
/// - Guild tasks (<see cref="Guild"/> != ""): the PC's rank in that guild must
///   be >= 2 (vanilla promotes at contribution thresholds; rank caps at 6).
///
/// Monthly reward, paid per elapsed WHOLE month, delivered in ONE parcel when
/// the posting ends (recall / death / mod removed). Mid-month recall still pays
/// the whole months that passed.
/// </summary>
internal sealed class PermanentDispatchDef
{
    /// <summary>Stable id, persisted in missions.</summary>
    public string Id = "";

    /// <summary>Vanilla zone id the task is anchored to (e.g. "kapul").</summary>
    public string ZoneId = "";

    /// <summary>LaborText key for the task name ("pdisp.name.*").</summary>
    public string NameKey = "";

    /// <summary>SKILL id the NPC must exceed; 0 = no skill gate.</summary>
    public int GateSkill;

    /// <summary>NPC skill must be &gt; this value (战&gt;20 style wording).</summary>
    public int GateLevel;

    /// <summary>"fighter"/"thief"/"mage"/"merchant" for guild tasks, "" otherwise.
    /// Named GuildId so the vanilla Guild type stays addressable inside this class.</summary>
    public string GuildId = "";

    /// <summary>Base gold per settled month (the flat part of the pay).</summary>
    public int MoneyPerMonth;

    /// <summary>
    /// Gold per point of the relevant skill, added on top of
    /// <see cref="MoneyPerMonth"/>. Player formula 2026-11-02:
    /// village odd jobs 5/pt, town jobs 10/pt, guild posts 20/pt.
    /// </summary>
    public int MoneyPerSkill;

    /// <summary>
    /// Item count that scales with skill: <c>ItemScaleBase + skill/Divisor</c>,
    /// clamped to [1, <see cref="ItemScaleCap"/>]. Divisor 0 = not scaled (the
    /// fixed <see cref="ItemCount"/>/<see cref="ItemCountMax"/> path is used).
    /// </summary>
    public int ItemScaleBase = 1;

    public int ItemScaleDivisor;

    public int ItemScaleCap;

    /// <summary>Same scaling for <see cref="BlessedItemId"/>; 0 = use BlessedItemCount.</summary>
    public int BlessedScaleDivisor;

    public int BlessedScaleCap;

    /// <summary>
    /// Fixed zone points per month (patrol = 5). 0 = legacy skill formula.
    /// </summary>
    public int ZonePoints;

    /// <summary>Explicit item ids rolled per month (null entries tolerated at roll time).</summary>
    public string[] ItemIds = Array.Empty<string>();

    /// <summary>How many items are rolled per month from <see cref="ItemIds"/>.</summary>
    public int ItemCount;

    /// <summary>
    /// Upper bound for a monthly count range. When greater than
    /// <see cref="ItemCount"/> the month rolls a random count in
    /// [ItemCount, ItemCountMax]; 0 = fixed <see cref="ItemCount"/>.
    /// </summary>
    public int ItemCountMax;

    /// <summary>
    /// Optional rare drop, rolled once per settled month BEFORE the common pool:
    /// on a hit the month yields <see cref="RareItemCount"/> of this id instead of
    /// the usual <see cref="ItemIds"/> roll. Empty = no rare drop.
    /// </summary>
    public string RareItemId = "";

    /// <summary>Rare-drop chance in percent (1-100). 0 disables the roll.</summary>
    public int RareChancePercent;

    /// <summary>How many of <see cref="RareItemId"/> a successful roll yields.</summary>
    public int RareItemCount = 1;

    /// <summary>
    /// Item handed out every settled month in its blessed state (id + count).
    /// Empty = none. 诺耶尔's church pays one blessed water.
    /// </summary>
    public string BlessedItemId = "";

    public int BlessedItemCount = 1;

    /// <summary>
    /// Skill whose potential is trained once per settled month (vanilla trainer
    /// semantics, <c>ElementContainer.Train</c>). -1 = guild task: pick the NPC's
    /// highest skill among <see cref="GuildTrainables"/>; 0 = none.
    /// </summary>
    public int PotentialSkill;

    /// <summary>
    /// 祭坛委托专用：这座祭坛供奉的神（Religion id，例如 "machine"）。
    /// 空 = 按 PC 当前信仰的神算。见 <see cref="Special"/> == "faith"。
    /// </summary>
    public string GodId = "";

    /// <summary>
    /// Extra monthly effect: "guilpo:fighter|thief|mage|merchant" (RQX guild
    /// currency), "develop" (patrol -> town development), "influence" (local
    /// Zone.influence), "indulgence" (5% indulgence drop), "faith" (faith
    /// potential already handled via PotentialSkill; god change is a TODO).
    /// </summary>
    public string Special = "";

    // ---- resolved helpers -------------------------------------------------

    internal string Name => LaborText.T(NameKey);

    /// <summary>
    /// Flavor text for the board detail pane, written in the voice of the local
    /// official who hands the posting out. A def without text yields "" instead
    /// of the raw key (LaborText.T returns the key when it is missing).
    /// </summary>
    internal string Description
    {
        get
        {
            string key = "pdisp.desc." + Id;
            string s = LaborText.T(key);
            return string.IsNullOrEmpty(s) || s == key ? "" : s;
        }
    }

    internal bool IsGuildTask => GuildId.Length > 0;

    /// <summary>
    /// PC rank needed to take a guild posting. 2 = the same threshold vanilla
    /// already uses for guild-trainer pricing (CalcPlat.Train).
    /// </summary>
    internal const int GuildRankGate = 2;

    /// <summary>
    /// Which board lists this task. Guild tasks normally sit on the guild's OWN
    /// board inside the guild room (player spec 2026-10-30), but that board is
    /// added by the RandomQuest &amp; GuildQuest expansion pack — vanilla guild
    /// rooms have none. Without it the guild posting falls back to the host
    /// town's board (<see cref="ZoneId"/>), so the task stays reachable.
    /// </summary>
    internal bool BoardMatches(string? zoneId)
    {
        if (string.IsNullOrEmpty(zoneId))
        {
            return false;
        }

        if (IsGuildTask && GuildBoardsAvailable)
        {
            return zoneId!.IndexOf("guild_" + GuildId, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return string.Equals(zoneId, ZoneId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the guild quest boards exist, i.e. RQX is loaded. Keyed off its
    /// guild-currency row because that is the only thing we ever reference from
    /// that mod (no type dependency, no package dependency).
    /// </summary>
    internal static bool GuildBoardsAvailable
    {
        get
        {
            try
            {
                return EClass.sources?.things?.map != null
                       && EClass.sources.things.map.ContainsKey("MOD_byakko_RQX_guilpo_fighter");
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Short reward summary for the board row: items and zone effects only.
    /// Money is deliberately NOT advertised (player spec 2026-10-30: list what
    /// you get, not how much it pays); it only shows for money-only postings so
    /// the row is never blank. Potential gain stays a hidden surprise.
    /// </summary>
    internal string RewardText()
    {
        var bits = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < ItemIds.Length && bits.Count < 3; i++)
        {
            string name = ItemName(ItemIds[i]);
            if (!string.IsNullOrEmpty(name) && seen.Add(name))
            {
                bits.Add(name);
            }
        }

        if (!string.IsNullOrEmpty(BlessedItemId) && bits.Count < 3)
        {
            string blessed = ItemName(BlessedItemId);
            if (!string.IsNullOrEmpty(blessed))
            {
                bits.Add(LaborText.T("pdisp.reward.blessed", blessed));
            }
        }

        // 祭坛：每月一件该神偏好的武器（奇跡以上），见 Rewards.RollGodWeapon。
        if (Special == "faith")
        {
            bits.Add(LaborText.T("pdisp.reward.godweapon"));
        }

        switch (Special)
        {
            case "develop":
                bits.Add(LaborText.T("pdisp.reward.develop"));
                break;
            case "influence":
                bits.Add(LaborText.T("pdisp.reward.influence"));
                break;
            case "townInfluence":
                bits.Add(LaborText.T("pdisp.reward.townInfluence"));
                break;
            case "indulgence":
                bits.Add(LaborText.T("pdisp.reward.indulgence"));
                break;
            case "faith":
                bits.Add(LaborText.T("pdisp.reward.faith"));
                break;
        }

        if (bits.Count == 0 && MoneyPerMonth > 0)
        {
            bits.Add(MoneyText(MoneyPerMonth));
        }

        return bits.Count == 0 ? "" : string.Join("、", bits);
    }

    /// <summary>Localized thing name; empty when the source row is missing (RQX off).</summary>
    internal static string ItemName(string id)
    {
        try
        {
            if (EClass.sources?.things?.map != null
                && EClass.sources.things.map.TryGetValue(id, out SourceThing.Row? row)
                && row != null)
            {
                string n = row.GetName();
                return string.IsNullOrEmpty(n) ? "" : n;
            }
        }
        catch { }

        return "";
    }

    /// <summary>Gold amount in the game's own currency wording ("800奥伦").</summary>
    internal static string MoneyText(int amount)
    {
        try
        {
            return "qReward".lang(amount.ToString());
        }
        catch
        {
            return amount.ToString();
        }
    }

    /// <summary>Localized guild name for the rank gate line.</summary>
    internal string GuildName => LaborText.T("pdisp.guild." + (IsGuildTask ? GuildId : "fighter"));

    /// <summary>
    /// Compact gate line for a board row: "举重>10" / "盗贼公会阶段>2".
    /// </summary>
    internal string GateText()
    {
        if (IsGuildTask)
        {
            return LaborText.T("pdisp.ui.guildGate", GuildName, GuildRankGate);
        }

        return GateSkill > 0
            ? LaborText.T("pdisp.ui.gate", SkillName(GateSkill), GateLevel)
            : "";
    }

    /// <summary>Localized element name; the source row name is the EN column.</summary>
    internal static string SkillName(int id)
    {
        try
        {
            if (EClass.sources?.elements?.map != null
                && EClass.sources.elements.map.TryGetValue(id, out SourceElement.Row? row)
                && row != null)
            {
                string n = row.GetName();
                if (!string.IsNullOrEmpty(n))
                {
                    return n;
                }
            }
        }
        catch { }

        return id.ToString();
    }

    internal bool GateMet(Chara npc)
    {
        if (GateSkill <= 0)
        {
            return true;
        }

        try
        {
            return Math.Max(0, npc.Evalue(GateSkill)) > GateLevel;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Guild trainable pools, used for the "best trainable skill the NPC has"
    /// potential reward on guild tasks. Data lives here on purpose: the exact
    /// per-guild teach lists are a balance knob, not engine logic.
    /// </summary>
    internal static readonly Dictionary<string, int[]> GuildTrainables = new()
    {
        // Warrior guild trainer: melee / armor line.
        ["fighter"] = new[]
        {
            100, // martial
            132, // tactics
            130, // twohand
            123, // shield
            122, // armorHeavy
            101, // weaponSword
            102, // weaponAxe
            111, // weaponBlunt
            106, // weaponPolearm
            207, // weightlifting
        },
        // Thieves guild: sneaky line.
        ["thief"] = new[]
        {
            281, // stealing
            280, // lockpicking
            152, // stealth
            293, // disarmTrap
            150, // evasion
            120, // armorLight
        },
        // Mages guild: magic line.
        ["mage"] = new[]
        {
            304, // casting
            305, // magicDevice
            302, // controlmana
            301, // meditation
            303, // manaCapacity
            285, // reading
        },
        // Merchants guild: trade line.
        ["merchant"] = new[]
        {
            291, // negotiation
            292, // investing
            289, // appraising
        },
    };

    /// <summary>PC rank in the given guild (0 = not promoted yet), -1 unknown.</summary>
    internal static int PcGuildRank(string guild)
    {
        try
        {
            Guild? g = guild switch
            {
                "fighter" => Guild.Fighter,
                "thief" => Guild.Thief,
                "mage" => Guild.Mage,
                "merchant" => Guild.Merchant,
                _ => null,
            };
            if (g == null)
            {
                return -1;
            }

            return Math.Max(0, g.relation.rank);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>All task definitions. Money values are first-pass balance data.</summary>
    internal static readonly List<PermanentDispatchDef> All = new()
    {
        // ---- Port Kapul (fighters guild) ----------------------------------
        new PermanentDispatchDef
        {
            Id = "pd_fishing", ZoneId = "kapul", NameKey = "pdisp.name.fishing",
            GateSkill = 245, GateLevel = 10, // fishing > 10
            MoneyPerMonth = 300, MoneyPerSkill = 10,
            ItemIds = new[] { "fish" }, ItemScaleBase = 1, ItemScaleDivisor = 20, ItemScaleCap = 10,
            PotentialSkill = 245,
        },
        new PermanentDispatchDef
        {
            Id = "pd_coach_fighter", ZoneId = "kapul", NameKey = "pdisp.name.coach",
            GuildId = "fighter",
            MoneyPerMonth = 500, MoneyPerSkill = 20,
            ItemIds = new[] { "MOD_byakko_RQX_guilpo_fighter" },
            ItemScaleBase = 1, ItemScaleDivisor = 20, ItemScaleCap = 10,
            PotentialSkill = -1, Special = "guilpo:fighter",
        },

        // ---- Derphy (thieves guild) ---------------------------------------
        new PermanentDispatchDef
        {
            Id = "pd_patrol", ZoneId = "derphy", NameKey = "pdisp.name.patrol",
            GateSkill = 132, GateLevel = 20, // tactics > 20
            // Player correction 2026-11-01: patrols raise the town's INFLUENCE
            // (what the board charges for rerolls), not its development.
            // 2026-11-02: fixed +5 a month, and confiscated lockpicks as loot.
            MoneyPerMonth = 300, MoneyPerSkill = 10,
            ItemIds = new[] { "lockpick" }, ItemCount = 1, ItemCountMax = 3,
            PotentialSkill = 132, Special = "townInfluence", ZonePoints = 5,
        },
        new PermanentDispatchDef
        {
            Id = "pd_lookout", ZoneId = "derphy", NameKey = "pdisp.name.lookout",
            GuildId = "thief",
            MoneyPerMonth = 500, MoneyPerSkill = 20,
            ItemIds = new[] { "MOD_byakko_RQX_guilpo_thief" },
            ItemScaleBase = 1, ItemScaleDivisor = 20, ItemScaleCap = 10,
            PotentialSkill = -1, Special = "guilpo:thief",
        },

        // ---- Olvina (hot springs & wine) ----------------------------------
        new PermanentDispatchDef
        {
            Id = "pd_winery", ZoneId = "olvina", NameKey = "pdisp.name.winery",
            GateSkill = 286, GateLevel = 10, // farming > 10
            MoneyPerMonth = 300, MoneyPerSkill = 10,
            ItemIds = new[] { "48" }, ItemScaleBase = 1, ItemScaleDivisor = 5, ItemScaleCap = 20, // 48 = bottle of wine
            // 10% of the months the press gives the Lumiest speciality instead:
            // crimale2 = 鲁米艾斯特产『库莱姆酒』.
            RareItemId = "crimale2", RareChancePercent = 10, RareItemCount = 1,
            PotentialSkill = 286,
        },

        // ---- Willow --------------------------------------------------------
        new PermanentDispatchDef
        {
            Id = "pd_hunter", ZoneId = "lothria", NameKey = "pdisp.name.hunter",
            GateSkill = 133, GateLevel = 10, // marksman > 10
            MoneyPerMonth = 300, MoneyPerSkill = 10,
            // meat_marble is created by id, never generated from a corpse, so the
            // material stays meat instead of whatever the prey was made of.
            ItemIds = new[] { "meat_marble" }, ItemScaleBase = 1, ItemScaleDivisor = 10, ItemScaleCap = 20,
            PotentialSkill = 290,
        },

        // ---- Mysilia -------------------------------------------------------
        new PermanentDispatchDef
        {
            Id = "pd_market", ZoneId = "mysilia", NameKey = "pdisp.name.market",
            GateSkill = 291, GateLevel = 10, // negotiation > 10
            MoneyPerMonth = 300, MoneyPerSkill = 10,
            // 338 = 耐酸保护液 (acidproof liquid), a numeric id like 48 above.
            ItemIds = new[] { "blanket_fire", "blanket_cold", "338", "bandage" },
            ItemScaleBase = 1, ItemScaleDivisor = 10, ItemScaleCap = 5,
            PotentialSkill = 291,
        },

        // ---- Merchants Guild (own location) --------------------------------
        new PermanentDispatchDef
        {
            Id = "pd_broker", ZoneId = "guild_merchant", NameKey = "pdisp.name.broker",
            GuildId = "merchant",
            MoneyPerMonth = 500, MoneyPerSkill = 20,
            ItemIds = new[] { "MOD_byakko_RQX_guilpo_merchant" },
            ItemScaleBase = 1, ItemScaleDivisor = 20, ItemScaleCap = 10,
            PotentialSkill = -1, Special = "guilpo:merchant",
        },

        // ---- Mifu Village (rice paddies) -------------------------------------
        // Player 2026-11-02: ミフの里 is the RICE village; ヨウィン farms dry
        // fields. The old shared produce pool (apples, grapes, oranges) was
        // orchard fruit and belonged to neither.
        new PermanentDispatchDef
        {
            Id = "pd_farm", ZoneId = "foxtown", NameKey = "pdisp.name.farm",
            GateSkill = 286, GateLevel = 5, // farming > 5
            MoneyPerMonth = 100, MoneyPerSkill = 5,
            ItemIds = new[] { "rice" }, // 米
            ItemScaleBase = 1, ItemScaleDivisor = 10, ItemScaleCap = 10,
            PotentialSkill = 286,
        },
        new PermanentDispatchDef
        {
            Id = "pd_farm_yowyn", ZoneId = "yowyn", NameKey = "pdisp.name.farm_dry",
            GateSkill = 286, GateLevel = 5, // farming > 5
            MoneyPerMonth = 100, MoneyPerSkill = 5,
            ItemIds = new[] { "carrot", "cabbage", "radish", "potato", "tomato", "corn" },
            ItemScaleBase = 1, ItemScaleDivisor = 10, ItemScaleCap = 10,
            PotentialSkill = 286,
        },
        new PermanentDispatchDef
        {
            Id = "pd_altar_foxtown", ZoneId = "foxtown", NameKey = "pdisp.name.altar",
            GateSkill = 306, GateLevel = 10, // faith > 10
            MoneyPerMonth = 100, MoneyPerSkill = 5,
            PotentialSkill = 306, Special = "faith",
        },

        // ---- Lumiest (mages guild) -----------------------------------------
        new PermanentDispatchDef
        {
            Id = "pd_painter", ZoneId = "lumiest", NameKey = "pdisp.name.painter",
            GateSkill = 258, GateLevel = 10, // sculpture > 10
            MoneyPerMonth = 300, MoneyPerSkill = 10,
            ItemIds = new[] { "267", "268", "269", "270" },
            ItemScaleBase = 1, ItemScaleDivisor = 20, ItemScaleCap = 4,
            PotentialSkill = 258,
        },
        new PermanentDispatchDef
        {
            Id = "pd_tutor_mage", ZoneId = "lumiest", NameKey = "pdisp.name.tutor",
            GuildId = "mage",
            MoneyPerMonth = 500, MoneyPerSkill = 20,
            ItemIds = new[] { "MOD_byakko_RQX_guilpo_mage" },
            ItemScaleBase = 1, ItemScaleDivisor = 20, ItemScaleCap = 10,
            PotentialSkill = -1, Special = "guilpo:mage",
        },

        // ---- Palmia (the one and only stage) --------------------------------
        new PermanentDispatchDef
        {
            Id = "pd_concert", ZoneId = "palmia", NameKey = "pdisp.name.concert",
            GateSkill = 241, GateLevel = 50, // music > 50 (player 2026-11-02)
            MoneyPerMonth = 1000, MoneyPerSkill = 10, // 1000 + music x 10
            ItemIds = new[] { "plat" }, // 白金币
            ItemScaleBase = 1, ItemScaleDivisor = 10, ItemScaleCap = 10,
            PotentialSkill = 241,
            // 1% of the months the audience leaves a 1-oren bill behind.
            RareItemId = "bill", RareChancePercent = 1, RareItemCount = 1,
        },

        // ---- Specwing --------------------------------------------------------
        new PermanentDispatchDef
        {
            Id = "pd_gather", ZoneId = "specwing", NameKey = "pdisp.name.gather",
            GateSkill = 250, GateLevel = 10, // gathering > 10
            MoneyPerMonth = 100, MoneyPerSkill = 5,
            ItemIds = new[] { "berry", "nuts", "mushroom" }, // 山货：浆果/坚果/蘑菇
            ItemScaleBase = 1, ItemScaleDivisor = 20, ItemScaleCap = 6,
            PotentialSkill = 250,
        },

        // ---- Nefu Village -----------------------------------------------------
        new PermanentDispatchDef
        {
            Id = "pd_inn", ZoneId = "foxtown_nefu", NameKey = "pdisp.name.inn",
            GateSkill = 287, GateLevel = 10, // cooking > 10
            MoneyPerMonth = 100, MoneyPerSkill = 5,
            // lunch_love = 爱之盒饭: the cook packs her own lunch for you
            // (player 2026-11-02, replacing the old buns/jerky).
            ItemIds = new[] { "lunch_love" },
            ItemScaleBase = 1, ItemScaleDivisor = 10, ItemScaleCap = 10,
            PotentialSkill = 287,
        },
        new PermanentDispatchDef
        {
            Id = "pd_shrine_nefu", ZoneId = "foxtown_nefu", NameKey = "pdisp.name.altar",
            GateSkill = 306, GateLevel = 10,
            MoneyPerMonth = 100, MoneyPerSkill = 5,
            PotentialSkill = 306, Special = "faith",
        },

        // ---- Noyel (the church) ------------------------------------------------
        new PermanentDispatchDef
        {
            Id = "pd_church", ZoneId = "noyel", NameKey = "pdisp.name.church",
            GateSkill = 306, GateLevel = 10,
            MoneyPerMonth = 100, MoneyPerSkill = 5,
            PotentialSkill = 306, Special = "indulgence",
            BlessedItemId = "water",
            BlessedScaleDivisor = 30, BlessedScaleCap = 10,
        },

        // ---- Aquli Teola (the machine city, shrine of the Machine God) ----------
        new PermanentDispatchDef
        {
            Id = "pd_forge", ZoneId = "aquli", NameKey = "pdisp.name.forge",
            GateSkill = 256, GateLevel = 10, // blacksmith > 10
            MoneyPerMonth = 300, MoneyPerSkill = 10,
            ItemIds = new[] { "crystal_earth" }, // 大地的结晶
            ItemScaleBase = 3, ItemScaleDivisor = 10, ItemScaleCap = 15,
            PotentialSkill = 256,
        },
        new PermanentDispatchDef
        {
            Id = "pd_altar_aquli", ZoneId = "aquli", NameKey = "pdisp.name.altar",
            GateSkill = 306, GateLevel = 10, // faith > 10
            MoneyPerMonth = 100, MoneyPerSkill = 5,
            PotentialSkill = 306, Special = "faith",
            GodId = "machine", // 阿库里的祭坛就是机械神的那一座（altar_machine）
        },

        // ---- Tinker's Camp (has its own board) ----------------------------------
        new PermanentDispatchDef
        {
            Id = "pd_porter", ZoneId = "tinkerCamp", NameKey = "pdisp.name.porter",
            GateSkill = 207, GateLevel = 10, // weightlifting > 10
            MoneyPerMonth = 300, MoneyPerSkill = 10,
            ItemIds = new[] { "ration" }, // 旅粮
            ItemScaleBase = 1, ItemScaleDivisor = 10, ItemScaleCap = 10,
            PotentialSkill = 207,
        },

        // ---- Little Garden (no board of its own -> home board) -------------------
        new PermanentDispatchDef
        {
            Id = "pd_children", ZoneId = "little_garden", NameKey = "pdisp.name.children",
            GateSkill = 293, GateLevel = 20, // disarmTrap > 20 (player 2026-11-02)
            MoneyPerMonth = 0,
            ItemIds = new[] { "explosive_mega" }, ItemCount = 1, ItemCountMax = 5, // 巨大炎烧
            PotentialSkill = 293, Special = "influence",
        },
    };

    internal static PermanentDispatchDef? GetById(string id)
    {
        foreach (PermanentDispatchDef d in All)
        {
            if (d.Id == id)
            {
                return d;
            }
        }

        return null;
    }

    internal static List<PermanentDispatchDef> ForZone(string zoneId)
    {
        var list = new List<PermanentDispatchDef>();
        foreach (PermanentDispatchDef d in All)
        {
            if (d.ZoneId == zoneId)
            {
                list.Add(d);
            }
        }

        return list;
    }

    /// <summary>Tasks listed on the board of this zone (town board or guild board).</summary>
    internal static List<PermanentDispatchDef> ForBoard(string? zoneId)
    {
        var list = new List<PermanentDispatchDef>();
        foreach (PermanentDispatchDef d in All)
        {
            if (d.BoardMatches(zoneId))
            {
                list.Add(d);
            }
        }

        return list;
    }

    /// <summary>Tasks anchored to zones that never show a town board -> home board.</summary>
    internal static List<PermanentDispatchDef> HomeBoardDefs => ForZone("little_garden");

    internal static Zone? ResolveZone(string zoneId)
    {
        if (string.IsNullOrEmpty(zoneId))
        {
            return null;
        }

        try
        {
            List<Zone>? all = EClass.game?.spatials?.Zones;
            if (all == null)
            {
                return null;
            }

            foreach (Zone z in all)
            {
                try
                {
                    if (z != null && z.id == zoneId)
                    {
                        return z;
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// Skill id that drives pay and item count. Guild postings have no gate
    /// skill, so they use whichever trainable the NPC is best at.
    /// </summary>
    internal int ScaleSkillId(Chara npc)
    {
        if (GateSkill > 0)
        {
            return GateSkill;
        }

        return IsGuildTask ? BestTrainableId(npc, GuildId) : 0;
    }

    internal int ScaleSkillValue(Chara npc)
    {
        try
        {
            int id = ScaleSkillId(npc);
            return id > 0 ? Math.Max(0, npc.Evalue(id)) : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Guild task: the NPC's highest skill among that guild's trainables.</summary>
    internal static int BestTrainableId(Chara npc, string guildId)
    {
        string g = guildId ?? "";
        if (g.Length == 0 || !GuildTrainables.TryGetValue(g, out int[]? ids))
        {
            return 0;
        }

        int bestId = 0;
        int bestVal = -1;
        foreach (int id in ids)
        {
            try
            {
                int v = npc.Evalue(id);
                if (v > bestVal)
                {
                    bestVal = v;
                    bestId = id;
                }
            }
            catch { }
        }

        return bestId;
    }

    /// <summary>
    /// Monthly item count. Three shapes, in order:
    /// 1. a fixed range (<see cref="ItemCountMax"/> &gt; <see cref="ItemCount"/>);
    /// 2. skill-scaled <c>ItemScaleBase + skill/Divisor</c>, clamped to cap;
    /// 3. a flat <see cref="ItemCount"/>.
    /// The farm used to be a fourth, gathering-based case; it now uses shape 2
    /// like everything else (player formula 2026-11-02).
    /// </summary>
    internal int MonthlyItemCount(Chara npc)
    {
        if (ItemCountMax > ItemCount)
        {
            return ItemCount + EClass.rnd(ItemCountMax - ItemCount + 1);
        }

        if (ItemScaleDivisor > 0)
        {
            int v = ItemScaleBase + ScaleSkillValue(npc) / ItemScaleDivisor;
            int cap = ItemScaleCap > 0 ? ItemScaleCap : 99;
            return Math.Min(cap, Math.Max(1, v));
        }

        if (ItemCount > 0)
        {
            return ItemCount;
        }

        return 1;
    }

    /// <summary>
    /// How many of <see cref="BlessedItemId"/> one month pays. Scaled the same
    /// way as ordinary loot unless a fixed count is configured.
    /// </summary>
    internal int BlessedCount(Chara npc)
    {
        if (BlessedScaleDivisor > 0)
        {
            int v = 1 + ScaleSkillValue(npc) / BlessedScaleDivisor;
            int cap = BlessedScaleCap > 0 ? BlessedScaleCap : 99;
            return Math.Min(cap, Math.Max(1, v));
        }

        return Math.Max(1, BlessedItemCount);
    }

    /// <summary>
    /// Zone-effect points per month. Patrols pay a flat <see cref="ZonePoints"/>;
    /// anything else falls back to the legacy skill curve.
    /// </summary>
    internal int MonthlyZonePoints(Chara npc)
    {
        if (ZonePoints > 0)
        {
            return ZonePoints;
        }

        try
        {
            int tactics = Math.Max(0, npc.Evalue(132));
            return Math.Min(10, 1 + Math.Max(0, tactics - 20) / 20);
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>
    /// Gold for one settled month: flat base plus a per-skill-point rate
    /// (player formula 2026-11-02). 0/0 = the posting does not pay money.
    /// </summary>
    internal int MonthlyMoney(Chara npc)
    {
        if (MoneyPerMonth == 0 && MoneyPerSkill == 0)
        {
            return 0;
        }

        return MoneyPerMonth + MoneyPerSkill * ScaleSkillValue(npc);
    }
}
