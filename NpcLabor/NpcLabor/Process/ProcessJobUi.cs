using System.Collections.Generic;
using NpcLabor.CoCraft;

namespace NpcLabor.Process;

/// <summary>
/// Slice B multi-machine UI: one list of every live processor job, so the player can see
/// all machines running at once and cancel any single one. Reached from a dedicated
/// button on the craft grid, next to the operator-mode button.
///
/// Rows are rebuilt from scratch on every open (no caching) — same policy as the operator
/// picker — so a job cleared while the list is open simply disappears on reopen.
/// </summary>
internal static class ProcessJobUi
{
    /// <summary>Sentinel row id offset: real job tokens start at 1 and are monotonic.</summary>
    const int RowBase = 0;

    /// <summary>Open the "all processing jobs" list. Safe to call with zero jobs.</summary>
    internal static void OpenJobsMenu()
    {
        try
        {
            LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);

            // Snapshot the jobs into row ids so the list cannot shift under the click
            // handler if a job clears while the menu is open.
            List<int> rowJobs = new List<int>();
            var jobs = ProcessorJobSession.Jobs;
            for (int i = 0; i < jobs.Count; i++)
            {
                ProcessorJob job = jobs[i];
                if (job != null && (job.Active || job.NpcUid != 0))
                {
                    rowJobs.Add(job.JobToken);
                }
            }

            List<int> options = new List<int>();
            if (rowJobs.Count == 0)
            {
                options.Add(RowBase); // single placeholder row
            }
            else
            {
                options.AddRange(rowJobs);
            }

            PersonPickerUi.BeginRowCost();
            menu.SetList2(
                    options,
                    id => RowLabel(id, rowJobs),
                    (id, item) => OnRowClick(id, rowJobs),
                    (id, item) =>
                    {
                        string label = RowLabel(id, rowJobs);
                        string sub = RowSub(id, rowJobs);
                        // No portrait face: rows are machines, not people. menuSelf/auto
                        // sentinels stay at their defaults so StyleRow disables the icon.
                        PersonPickerUi.StyleRow(item, id, label, sub);
                    })
                .SetSize(460f);
            PersonPickerUi.LogRowCost("process-jobs");

            string title = NpcLabor.LaborText.T("proc.ui.jobsTitle");
            try
            {
                menu.SetHeader(title);
            }
            catch
            {
                try
                {
                    if (menu.windows != null && menu.windows.Count > 0)
                    {
                        menu.windows[0].SetCaption(title);
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("ProcessJobUi.cs silent catch: " + __e.Message); }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("process jobs menu failed: " + ex);
        }
    }

    /// <summary>Main row text: "machine · worker" or the empty-list placeholder.</summary>
    static string RowLabel(int id, List<int> rowJobs)
    {
        if (rowJobs.Count == 0)
        {
            return NpcLabor.LaborText.T("proc.ui.jobsNone");
        }

        ProcessorJob? job = ProcessorJobSession.Resolve(id);
        if (job == null)
        {
            return "";
        }

        string machine = job.MachineName ?? NpcLabor.LaborText.T("proc.msg.machineFallback");
        string worker = job.NpcName ?? NpcLabor.LaborText.T("proc.ui.jobsIdle");
        return machine + " · " + worker;
    }

    /// <summary>Row sub-line: state chip + progress + ETA. Never throws.</summary>
    static string RowSub(int id, List<int> rowJobs)
    {
        if (rowJobs.Count == 0)
        {
            return "";
        }

        ProcessorJob? job = ProcessorJobSession.Resolve(id);
        if (job == null)
        {
            return "";
        }

        // State chip first: an away/resident job has no live progress to show.
        if (job.Suspended)
        {
            return NpcLabor.LaborText.T("proc.ui.jobsSuspended");
        }

        if (job.IsReturningIngredients)
        {
            return NpcLabor.LaborText.T("proc.ui.jobsReturning");
        }

        int total = job.Completed + job.Remaining;
        string progress = NpcLabor.LaborText.T("proc.ui.jobsProgress", job.Completed, total <= 0 ? job.Completed : total);

        // ETA only while a live AI is actually working (not idle-standing-and-waiting).
        string eta = "";
        try
        {
            if (ProcessorJobSession.GetWorker(job) != null && job.Remaining > 0)
            {
                int minutes = ProcessorJobSession.EstimateDuration(job) * job.Remaining;
                int hours = (minutes + 59) / 60;
                if (hours > 0)
                {
                    eta = " · " + NpcLabor.LaborText.T("proc.ui.jobsEtaHours", hours);
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("ProcessJobUi.cs silent catch: " + __e.Message); }

        return progress + eta;
    }

    /// <summary>Row click: current behaviour is cancel-with-confirm for real rows.</summary>
    static void OnRowClick(int id, List<int> rowJobs)
    {
        if (rowJobs.Count == 0)
        {
            return;
        }

        ProcessorJob? job = ProcessorJobSession.Resolve(id);
        if (job == null)
        {
            return;
        }

        // A worker still pinned to another machine: report instead of silently cancelling.
        // (Machine is the registry key, so this only fires if the token was reused.)
        try
        {
            if (ProcessorJobSession.ForMachine(job.MachineUid) != job)
            {
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.busy"));
                return;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("ProcessJobUi.cs silent catch: " + __e.Message); }

        string name = job.MachineName ?? job.NpcName ?? "";
        ProcessorJobSession.Clear(job, "ui-cancel");
        Msg.SayRaw(NpcLabor.LaborText.T("proc.ui.jobsCancelDone", name));
        SE.Click();

        // Reopen so the player sees the updated (possibly empty) list immediately.
        PersonPickerUi.ClosePicker();
        OpenJobsMenu();
    }
}
