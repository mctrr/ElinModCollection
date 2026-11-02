using System;
using System.Collections.Generic;
using System.Linq;

namespace NpcLabor.PermanentDispatch;

/// <summary>
/// Debug helpers exposed from the mod config page (player request 2026-10-30:
/// "我需要一个可以瞬间完成常驻任务的功能"). They only fast-forward the same
/// accounting the hourly tick does — no separate code path, so what the debug
/// button pays is exactly what a real month would pay.
/// </summary>
internal static partial class PermanentDispatchManager
{
    /// <summary>
    /// Credit <paramref name="months"/> whole months to every active posting
    /// right now (potential / development / influence apply immediately, gold
    /// and items stay as counters until the posting ends).
    /// Returns how many postings were touched.
    /// </summary>
    internal static int DebugAdvanceMonths(int months)
    {
        if (months <= 0 || Missions.Count == 0)
        {
            return 0;
        }

        int touched = 0;
        foreach (PermanentDispatchMission m in Missions.ToList())
        {
            try
            {
                Chara? worker = null;
                try { worker = RefChara.Get(m.uidChara); } catch { }
                if (worker == null || worker.isDead)
                {
                    continue;
                }

                for (int i = 0; i < months; i++)
                {
                    m.settledThrough++;
                    m.monthsPaid++;
                    CreditMonthly(m, worker);
                }

                touched++;
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("pdispatch debug advance: " + ex.Message);
            }
        }

        if (touched > 0)
        {
            try { Save(); } catch { }
        }

        return touched;
    }

    /// <summary>
    /// Finish every posting immediately: credit one more whole month, then run
    /// the normal settle path (single parcel to the home mailbox, worker home).
    /// Returns how many postings were settled.
    /// </summary>
    internal static int DebugFinishAll()
    {
        if (Missions.Count == 0)
        {
            return 0;
        }

        int done = 0;
        foreach (PermanentDispatchMission m in Missions.ToList())
        {
            try
            {
                Chara? worker = null;
                try { worker = RefChara.Get(m.uidChara); } catch { }
                if (worker != null && !worker.isDead)
                {
                    m.settledThrough++;
                    m.monthsPaid++;
                    CreditMonthly(m, worker);
                }

                SettleOut(m, recalled: true);
                done++;
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("pdispatch debug finish: " + ex.Message);
            }
        }

        return done;
    }
}
