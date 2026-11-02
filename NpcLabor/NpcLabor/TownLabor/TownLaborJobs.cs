using System;
using System.Collections.Generic;
using System.Reflection;

namespace NpcLabor.TownLabor;

/// <summary>Fixed v1 town shop labor catalog (shop + book/scholar kinds).</summary>
internal enum TownLaborJobKind
{
    InnChore = 1,
    KitchenHelp = 2,
    GeneralClerk = 3,
    SmithAssist = 4,
    FoodClerk = 5,
    BookClerk = 6,
    ScholarAssist = 7,
}

/// <summary>
/// Job definition + client matching. PC self-work (time+SP) is implemented via TownLaborManager.TryStartSelf.
/// </summary>
internal sealed class TownLaborJobDef
{
    internal TownLaborJobKind Kind;
    internal string Id = "";
    internal string Title = "";
    /// <summary>Short board-line reasons; pick one by seed for variety.</summary>
    internal string[] DetailVariants = Array.Empty<string>();
    internal int SkillId;
    internal string SkillLabel = "";
    /// <summary>Primary leftover card ids tried in order.</summary>
    internal string[] LeftoverIds = Array.Empty<string>();
    internal Emo WorkEmo = Emo.happy;

    /// <summary>Stable pick of a short board detail for this offer.</summary>
    internal string PickDetail(int seed)
    {
        if (DetailVariants == null || DetailVariants.Length == 0)
        {
            return NpcLabor.LaborText.T("town.ui.needHelp");
        }

        int idx = seed;
        if (idx < 0)
        {
            idx = -idx;
        }

        return DetailVariants[idx % DetailVariants.Length];
    }
}

internal static class TownLaborJobs
{
    static string J(string key) => NpcLabor.LaborText.T("job." + key);

    static string J(string key, params object[] args) => NpcLabor.LaborText.T("job." + key, args);

    // Life skill ids from SKILL.
    internal const int SkillCooking = 287;
    internal const int SkillNegotiation = 291;
    internal const int SkillBlacksmith = 256;
    internal const int SkillHandicraft = 261;
    internal const int SkillWeightlifting = 207;
    internal const int SkillCarpentry = 255;
    internal const int SkillFarming = 286;
    internal const int SkillReading = 285;
    internal const int SkillMemorization = 307;
    internal const int SkillAppraising = 289;

    internal const int MaxConcurrentPerTown = 2;
    /// <summary>Board shows a random sample of free offers, not every shop.</summary>
    internal const int MinBoardOffers = 2;
    internal const int MaxBoardOffers = 3;

    /// <summary>Work duration is random hoursMin-hoursMax (no difficulty axis).</summary>
    internal static int RollWorkHours()
    {
        int min = LaborConfig.HoursMin;
        int max = LaborConfig.HoursMax;
        int span = Math.Max(0, max - min);
        try
        {
            return min + EClass.rnd(span + 1);
        }
        catch
        {
            return Math.Max(min, (min + max) / 2);
        }
    }

    /// <summary>Prefer board-offered hours when already in configured range; else roll.</summary>
    internal static int ResolveWorkHours(int offeredHours)
    {
        int min = LaborConfig.HoursMin;
        int max = LaborConfig.HoursMax;
        if (offeredHours >= min && offeredHours <= max)
        {
            return offeredHours;
        }

        return RollWorkHours();
    }

    /// <summary>Client shop level from TraitMerchant.ShopLv (or formula fallback).</summary>
    internal static int GetShopLv(Chara? client)
    {
        if (client == null)
        {
            return 1;
        }

        try
        {
            if (client.trait is TraitMerchant m)
            {
                int lv = m.ShopLv;
                if (lv > 0)
                {
                    return Math.Max(1, lv);
                }
            }
        }
        catch
        {
        }

        int development = 0;
        int invest = 0;
        int bonus = 0;
        try
        {
            Zone? z = client.currentZone ?? EClass._zone;
            if (z != null)
            {
                development = Math.Max(0, z.development);
            }
        }
        catch
        {
        }

        try { invest = Math.Max(0, client.c_invest); } catch { invest = 0; }
        try { bonus = Guild.Merchant.InvestBonus(); } catch { bonus = 0; }
        int shop = development / 10 + invest * (100 + bonus) / 100 + 1;
        return Math.Max(1, shop);
    }

    /// <summary>
    /// Whether a free shop-invest grant is allowed, in two gates:
    ///
    /// <para>
    /// 1. <b>Skill beats the shop</b> — worker skill &gt; the shop's own investment
    /// level (<c>c_invest</c>). A worker good enough for that shop can earn it a
    /// level; one who is already behind the shop cannot.
    /// </para>
    /// <para>
    /// 2. <b>Town development beats the shop</b> — the shop may climb only up to
    /// the town's development value (the number the investment NPC shows in its
    /// title, "城镇发展度：46"): <c>c_invest &lt; development</c>. Paid town
    /// invests push development up (vanilla <c>ModDevelopment</c>), and so do mod
    /// patrols, so the ceiling climbs as the town grows. Vanilla paid invests are
    /// not affected by this gate.
    /// </para>
    ///
    /// <para>
    /// History: two earlier caps compared numbers that cancelled out or self-fed —
    /// one against a base built from <c>dev/10</c> (flat cap), one against
    /// <c>Trait.ShopLv</c>, which already contains the shop's own <c>c_invest</c>
    /// and is therefore always greater (<c>dev/10 + c_invest + 1 &gt; c_invest</c>).
    /// Both were vacuous gates. A third revision folded <c>zone.investment</c>
    /// (accumulated gold) into a payment count by dividing by the current price;
    /// the design was later settled on the plainly visible development value
    /// instead (player verified in-game against the secretary dialog).
    /// </para>
    /// </summary>
    internal static bool CanGrantShopInvest(Chara? client, int workerSkill)
    {
        if (client == null)
        {
            return false;
        }

        int invest = 0;
        try { invest = Math.Max(0, client.c_invest); } catch { invest = 0; }

        // Gate 1: the worker has to out-skill the shop it is working for.
        if (Math.Max(0, workerSkill) <= invest)
        {
            return false;
        }

        // Gate 2: the town must still be ahead of the shop.
        return TownDevelopment(client) > invest;
    }

    /// <summary>
    /// 城镇发展度 for the shop's town — the raw value shown by the investment NPC
    /// in its title ("城镇发展度：46"). This is the free-invest ceiling: a shop's
    /// own <c>c_invest</c> may never reach it through town-labor grants. Grows
    /// when the player pays town invests (vanilla raises <c>ModDevelopment</c>)
    /// and when mod dispatches (patrol) add to it.
    /// </summary>
    internal static int TownDevelopment(Chara? client)
    {
        Zone? z = null;
        try { z = client?.currentZone ?? client?.homeZone ?? EClass._zone; } catch { z = EClass._zone; }
        if (z == null)
        {
            return 0;
        }

        try
        {
            return Math.Max(0, z.development);
        }
        catch
        {
            return 0;
        }
    }

    internal static int GetWorkerSkill(Chara? worker, int skillId)
    {
        if (worker == null || skillId <= 0)
        {
            return 0;
        }

        try
        {
            return Math.Max(0, worker.Evalue(skillId));
        }
        catch
        {
            return 0;
        }
    }

    internal static IReadOnlyList<TownLaborJobDef> All { get; } = new List<TownLaborJobDef>
    {
        new TownLaborJobDef
        {
            Kind = TownLaborJobKind.InnChore,
            Id = "inn_chore",
            Title = J("inn.title"),
            DetailVariants = new[]
            {
                J("inn.r1"),
                J("inn.r2"),
                J("inn.r3"),
                J("inn.r4"),
            },
            SkillId = SkillWeightlifting,
            SkillLabel = J("inn.skill"),
            LeftoverIds = new[] { "ration", "bread", "beer", "48", "junk" }, // 48 = wine（无 "wine" 这个 thing id）
            WorkEmo = Emo.water,
        },
        new TownLaborJobDef
        {
            Kind = TownLaborJobKind.KitchenHelp,
            Id = "kitchen_help",
            Title = J("kitchen.title"),
            DetailVariants = new[]
            {
                J("kitchen.r1"),
                J("kitchen.r2"),
                J("kitchen.r3"),
                J("kitchen.r4"),
            },
            SkillId = SkillCooking,
            SkillLabel = J("kitchen.skill"),
            LeftoverIds = new[] { "flour", "egg", "meat", "vegetable", "bread", "ration" },
            WorkEmo = Emo.cook,
        },
        new TownLaborJobDef
        {
            Kind = TownLaborJobKind.GeneralClerk,
            Id = "general_clerk",
            Title = J("general.title"),
            DetailVariants = new[]
            {
                J("general.r1"),
                J("general.r2"),
                J("general.r3"),
                J("general.r4"),
            },
            SkillId = SkillNegotiation,
            SkillLabel = J("general.skill"),
            LeftoverIds = new[] { "junk", "rope", "torch", "bandage", "pottery" },
            WorkEmo = Emo.idea,
        },
        new TownLaborJobDef
        {
            Kind = TownLaborJobKind.SmithAssist,
            Id = "smith_assist",
            Title = J("smith.title"),
            DetailVariants = new[]
            {
                J("smith.r1"),
                J("smith.r2"),
                J("smith.r3"),
                J("smith.r4"),
            },
            SkillId = SkillBlacksmith,
            SkillLabel = J("smith.skill"),
            LeftoverIds = new[] { "scrap", "ingot", "nail", "ore", "chunk" },
            WorkEmo = Emo.build,
        },
        new TownLaborJobDef
        {
            Kind = TownLaborJobKind.FoodClerk,
            Id = "food_clerk",
            Title = J("food.title"),
            DetailVariants = new[]
            {
                J("food.r1"),
                J("food.r2"),
                J("food.r3"),
                J("food.r4"),
            },
            SkillId = SkillCooking,
            SkillLabel = J("food.skill"),
            LeftoverIds = new[] { "fruit", "vegetable", "egg", "meat", "flour", "ration" },
            WorkEmo = Emo.hungry,
        },
        new TownLaborJobDef
        {
            Kind = TownLaborJobKind.BookClerk,
            Id = "book_clerk",
            Title = J("book.title"),
            DetailVariants = new[]
            {
                J("book.r1"),
                J("book.r2"),
                J("book.r3"),
                J("book.r4"),
            },
            SkillId = SkillReading,
            SkillLabel = J("book.skill"),
            LeftoverIds = new[] { "book", "book_exp", "scroll", "paper", "junk" },
            WorkEmo = Emo.idea,
        },
        new TownLaborJobDef
        {
            Kind = TownLaborJobKind.ScholarAssist,
            Id = "scholar_assist",
            Title = J("scholar.title"),
            DetailVariants = new[]
            {
                J("scholar.r1"),
                J("scholar.r2"),
                J("scholar.r3"),
                J("scholar.r4"),
            },
            SkillId = SkillMemorization,
            SkillLabel = J("scholar.skill"),
            LeftoverIds = new[] { "book", "book_exp", "paper", "bandage", "junk" },
            WorkEmo = Emo.idea,
        },
    };

    internal static TownLaborJobDef? Get(TownLaborJobKind kind)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i].Kind == kind)
            {
                return All[i];
            }
        }

        return null;
    }

    internal static TownLaborJobDef? GetById(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        for (int i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return All[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Which storefront a client runs. ResolveBoardTitle picks the wording from this and
    /// MatchJob picks the job kind, so the two cannot drift apart: the trait ladder lives
    /// here and nowhere else.
    /// </summary>
    enum ShopKind
    {
        None,
        Inn,
        Kitchen,
        Book,
        Scholar,
        Smith,
        Fish,
        Meat,
        Fruit,
        Bread,
        Milk,
        Booze,
        Food,
        Junk,
        Souvenir,
        General,
    }

    /// <summary>
    /// Classify a client by trait and shop type. <see cref="ShopKind.None"/> means nothing
    /// matched, and each caller turns that into its own fallback.
    /// </summary>
    static ShopKind ClassifyShop(Chara? client)
    {
        if (client == null)
        {
            return ShopKind.None;
        }

        Trait? trait = null;
        try { trait = client.trait; } catch { trait = null; }

        if (trait == null)
        {
            return ShopKind.None;
        }

        ShopType? shop = ShopTypeOf(trait);

        // Inn first: an innkeeper also carries the food merchant trait.
        if (trait is TraitInnkeeper)
        {
            return ShopKind.Inn;
        }

        if (trait is TraitChef)
        {
            return ShopKind.Kitchen;
        }

        // Bookstore / red-book clerk - before the broad scholar fallback.
        if (trait is TraitMerchantBook || shop == ShopType.Book || shop == ShopType.RedBook)
        {
            return ShopKind.Book;
        }

        // Desk work: guild clerks, healers, plat library, magic shop.
        if (trait is TraitGuildClerk
            || trait is TraitClerk_Mage
            || trait is TraitClerk_Merchant
            || trait is TraitClerk_Fighter
            || trait is TraitClerk_Thief
            || trait is TraitGuildPersonnel
            || trait is TraitHealer
            || trait is TraitMerchantPlat
            || trait is TraitMerchantMagic
            || shop == ShopType.Guild
            || shop == ShopType.Plat
            || shop == ShopType.Magic
            || shop == ShopType.Healer)
        {
            // Doorman is guild personnel but not desk work.
            return trait is TraitGuildDoorman ? ShopKind.None : ShopKind.Scholar;
        }

        // Blacksmith / weapon merchant.
        if (trait is TraitMerchantWeapon
            || shop == ShopType.Weapon
            || string.Equals(SafeAmbience(trait), "blacksmith", StringComparison.OrdinalIgnoreCase))
        {
            return ShopKind.Smith;
        }

        // Specialised food counters keep their own wording.
        if (trait is TraitMerchantFish)
        {
            return ShopKind.Fish;
        }

        if (trait is TraitMerchantMeat)
        {
            return ShopKind.Meat;
        }

        if (trait is TraitMerchantFruit)
        {
            return ShopKind.Fruit;
        }

        if (trait is TraitMerchantBread)
        {
            return ShopKind.Bread;
        }

        if (trait is TraitMerchantMilk)
        {
            return ShopKind.Milk;
        }

        if (trait is TraitMerchantBooze)
        {
            return ShopKind.Booze;
        }

        // Generic food shop: one that can serve food reads as a kitchen.
        if (trait is TraitMerchantFood || shop == ShopType.Food)
        {
            return CanServeFood(trait) ? ShopKind.Kitchen : ShopKind.Food;
        }

        // True general goods only - junk / souvenir / general shelves.
        if (trait is TraitMerchantJunk || shop == ShopType.Junk)
        {
            return ShopKind.Junk;
        }

        if (trait is TraitMerchantSouvenir || shop == ShopType.Souvenir)
        {
            return ShopKind.Souvenir;
        }

        if (trait is TraitMerchantGeneral || trait is TraitMerchantGeneralExotic
            || shop == ShopType.General || shop == ShopType.GeneralExotic
            || shop == ShopType.Goods)
        {
            return ShopKind.General;
        }

        return ShopKind.None;
    }

    /// <summary>Shop type of a merchant trait, or null for anything that is not one.</summary>
    static ShopType? ShopTypeOf(Trait trait)
    {
        try
        {
            if (trait is TraitMerchant m)
            {
                return m.ShopType;
            }
        }
        catch
        {
        }

        return null;
    }

    static bool CanServeFood(Trait? trait)
    {
        try
        {
            return trait is TraitCitizen c && c.CanServeFood;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Player-facing board title for a client. Prefer shop identity over generic clerk labels.
    /// Example: fishmonger -> "鱼店需要人手" instead of "食品店员".
    /// </summary>
    internal static string ResolveBoardTitle(Chara? client, TownLaborJobDef? def)
    {
        string fallback = def?.Title
            ?? (def != null ? def.Id : NpcLabor.LaborTerms.TownWork);
        if (client == null)
        {
            return fallback;
        }

        switch (ClassifyShop(client))
        {
            case ShopKind.Inn: return J("shop.inn.need");
            case ShopKind.Kitchen: return J("shop.kitchen.need");
            case ShopKind.Book: return J("shop.book.need");
            case ShopKind.Scholar: return J("shop.scholar.need");
            case ShopKind.Smith: return J("shop.smith.need");
            case ShopKind.Fish: return J("shop.fish.need");
            case ShopKind.Meat: return J("shop.meat.need");
            case ShopKind.Fruit: return J("shop.fruit.need");
            case ShopKind.Bread: return J("shop.bread.need");
            case ShopKind.Milk: return J("shop.milk.need");
            case ShopKind.Booze: return J("shop.booze.need");
            case ShopKind.Food: return J("shop.food.need");
            case ShopKind.Junk: return J("shop.junk.need");
            case ShopKind.Souvenir: return J("shop.souvenir.need");
            case ShopKind.General: return J("shop.general.need");
        }

        // Fallback: "<client job/name> needs help" when we only know the generic def.
        try
        {
            string who = client.NameSimple ?? client.Name ?? "";
            string job = "";
            try { job = client.job?.GetName() ?? ""; } catch { job = ""; }
            if (string.IsNullOrEmpty(job))
            {
                try { job = client.trait?.Name ?? ""; } catch { job = ""; }
            }

            if (!string.IsNullOrEmpty(job))
            {
                return J("shop.generic.need", job);
            }

            if (!string.IsNullOrEmpty(who))
            {
                return J("shop.generic.need", who);
            }
        }
        catch
        {
        }

        return fallback;
    }

    /// <summary>
    /// Match the best single job for a town NPC. One client maps to at most one job kind.
    /// Priority: inn -> kitchen -> book -> scholar -> smith -> food -> general.
    /// </summary>
    internal static TownLaborJobDef? MatchJob(Chara? client)
    {
        if (client == null || client.isDead || client.IsPC)
        {
            return null;
        }

        switch (ClassifyShop(client))
        {
            case ShopKind.Inn: return Get(TownLaborJobKind.InnChore);
            case ShopKind.Kitchen: return Get(TownLaborJobKind.KitchenHelp);
            case ShopKind.Book: return Get(TownLaborJobKind.BookClerk);
            case ShopKind.Scholar: return Get(TownLaborJobKind.ScholarAssist);
            case ShopKind.Smith: return Get(TownLaborJobKind.SmithAssist);

            // Every food counter is the same job; one that can serve food is kitchen help.
            case ShopKind.Fish:
            case ShopKind.Meat:
            case ShopKind.Fruit:
            case ShopKind.Bread:
            case ShopKind.Milk:
            case ShopKind.Booze:
            case ShopKind.Food:
                return CanServeFood(client.trait)
                    ? Get(TownLaborJobKind.KitchenHelp)
                    : Get(TownLaborJobKind.FoodClerk);

            case ShopKind.Junk:
            case ShopKind.Souvenir:
            case ShopKind.General:
                return Get(TownLaborJobKind.GeneralClerk);
        }

        return null;
    }

    static string? SafeAmbience(Trait trait)
    {
        if (trait == null)
        {
            return null;
        }

        // IdAmbience is not always on base Trait; read via reflection when present.
        try
        {
            var prop = trait.GetType().GetProperty("IdAmbience");
            if (prop != null)
            {
                return prop.GetValue(trait) as string;
            }
        }
        catch
        {
        }

        try
        {
            var field = trait.GetType().GetField("IdAmbience");
            if (field != null)
            {
                return field.GetValue(trait) as string;
            }
        }
        catch
        {
        }

        return null;
    }

}
