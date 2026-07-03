using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}
#endif

namespace Macro_Inserter
{

internal static class PseudoChordInputPlanFix
{
    private static readonly Harmony Harmony = new("Macro-Inserter.PseudoChordInputPlanFix");
    private static readonly FieldInfo? SettingsField = AccessTools.Field(typeof(InternalMacroService), "settings");
    private static readonly FieldInfo? LogField = AccessTools.Field(typeof(InternalMacroService), "log");
    private static bool patched;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (patched)
        {
            return;
        }

        try
        {
            MethodInfo? original = AccessTools.Method(
                typeof(InternalMacroService),
                "BuildInputPlan",
                new[] { typeof(IReadOnlyList<MacroPlanEntry>) });
            MethodInfo? prefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(BuildInputPlanPrefix));
            if (original == null || prefix == null)
            {
                return;
            }

            Harmony.Patch(original, prefix: new HarmonyMethod(prefix));
            patched = true;
        }
        catch
        {
            // Avoid breaking mod load if this compatibility patch cannot be installed.
        }
    }

    // Replacement for InternalMacroService.BuildInputPlan.
    // Only exact-duplicate groups that actually reduce hit count remain grouped.
    // Near pseudo-chords and exact groups with rawEntryCount == emittedHitCount are expanded
    // into normal single InputPlanEntry items so the scheduler cannot skip hidden entries.
    private static bool BuildInputPlanPrefix(
        InternalMacroService __instance,
        IReadOnlyList<MacroPlanEntry> macroPlan,
        ref IReadOnlyList<InputPlanEntry> __result)
    {
        InternalMacroSettings? settings = SettingsField?.GetValue(__instance) as InternalMacroSettings;
        Action<string>? log = LogField?.GetValue(__instance) as Action<string>;
        if (settings == null)
        {
            return true;
        }

        __result = BuildInputPlan(macroPlan, settings, log);
        return false;
    }

    private static IReadOnlyList<InputPlanEntry> BuildInputPlan(
        IReadOnlyList<MacroPlanEntry> macroPlan,
        InternalMacroSettings settings,
        Action<string>? log)
    {
        if (macroPlan.Count == 0)
        {
            return Array.Empty<InputPlanEntry>();
        }

        double windowMs = Math.Max(0.0, settings.PseudoChordWindowMs);
        double configuredMaxSpanMs = Math.Max(0.0, settings.PseudoChordMaxSpanMs);
        double maxSpanMs = configuredMaxSpanMs > 0.0
            ? Math.Min(windowMs, configuredMaxSpanMs)
            : windowMs;
        double exactDuplicateEpsilonMs = Math.Max(0.0, settings.PseudoChordExactDuplicateEpsilonMs);
        int maxHitsPerGroup = Math.Max(1, settings.MaxHitsPerPseudoChordGroup);
        int keyCapacity = GetPseudoChordKeyCapacity(settings, maxHitsPerGroup);
        List<InputPlanEntry> entries = new();

        int index = 0;
        while (index < macroPlan.Count)
        {
            int startIndex = index;
            MacroPlanEntry first = macroPlan[startIndex];
            double firstTargetTimeSeconds = first.TargetTimeSeconds;
            MacroPlanEntry last = first;
            bool containsMidspin = first.IsMidspin;
            bool isNearMidspin = first.IsNearMidspin;
            index++;

            while (index < macroPlan.Count)
            {
                MacroPlanEntry candidate = macroPlan[index];
                double spanMs = (candidate.TargetTimeSeconds - firstTargetTimeSeconds) * 1000.0;
                bool isExactDuplicate = Math.Abs(spanMs) <= exactDuplicateEpsilonMs;
                if (!isExactDuplicate && spanMs > windowMs)
                {
                    break;
                }

                last = candidate;
                containsMidspin |= candidate.IsMidspin;
                isNearMidspin |= candidate.IsNearMidspin;
                index++;
            }

            int rawEntryCount = index - startIndex;
            double actualSpanMs = (last.TargetTimeSeconds - firstTargetTimeSeconds) * 1000.0;
            if (rawEntryCount == 1)
            {
                AddSingleInputPlanEntry(entries, macroPlan, startIndex);
                continue;
            }

            if (actualSpanMs > maxSpanMs + 0.001)
            {
                LogNormal(log, $"pseudoChord rejected: span exceeds window. groupStartIndex={startIndex} groupEndIndex={index - 1} firstSeqID={first.SeqId} lastSeqID={last.SeqId} spanMs={actualSpanMs:F3} windowMs={windowMs:F3} maxSpanMs={maxSpanMs:F3}");
                AddSingleInputPlanEntry(entries, macroPlan, startIndex);
                index = startIndex + 1;
                continue;
            }

            bool isExactDuplicateGroup = actualSpanMs <= exactDuplicateEpsilonMs + 0.000001;
            int emittedHitCount = isExactDuplicateGroup
                ? Math.Min(rawEntryCount, Math.Min(keyCapacity, maxHitsPerGroup))
                : rawEntryCount;
            bool isCompressed = isExactDuplicateGroup && emittedHitCount < rawEntryCount;

            if (!isCompressed)
            {
                string kind = isExactDuplicateGroup ? "exact" : "near";
                string reason = isExactDuplicateGroup ? "not-compressed" : "not-exact-duplicate";
                LogNormal(log, $"pseudoChord {kind} passthrough expanded. groupStartIndex={startIndex} groupEndIndex={index - 1} firstSeqID={first.SeqId} lastSeqID={last.SeqId} firstTargetTime={first.TargetTimeSeconds:F6}s lastTargetTime={last.TargetTimeSeconds:F6}s rawEntryCount={rawEntryCount} emittedIndividualEntries={rawEntryCount} windowMs={windowMs:F3} exactDuplicateEpsilonMs={exactDuplicateEpsilonMs:F3} spanMs={actualSpanMs:F3} reason={reason}");
                AddIndividualInputPlanEntries(entries, macroPlan, startIndex, index);
                continue;
            }

            entries.Add(new InputPlanEntry(
                startIndex,
                index,
                first.SeqId,
                last.SeqId,
                first.TargetTimeSeconds,
                last.TargetTimeSeconds,
                rawEntryCount,
                Math.Max(1, emittedHitCount),
                containsMidspin,
                isNearMidspin,
                isExactDuplicateGroup: true,
                isCompressed: true));

            LogNormal(log, $"pseudoChord exact compressed planned. groupStartIndex={startIndex} groupEndIndex={index - 1} firstSeqID={first.SeqId} lastSeqID={last.SeqId} firstTargetTime={first.TargetTimeSeconds:F6}s lastTargetTime={last.TargetTimeSeconds:F6}s rawEntryCount={rawEntryCount} emittedHitCount={emittedHitCount} windowMs={windowMs:F3} exactDuplicateEpsilonMs={exactDuplicateEpsilonMs:F3} spanMs={actualSpanMs:F3}");
        }

        return entries;
    }

    private static int GetPseudoChordKeyCapacity(InternalMacroSettings settings, int fallback)
    {
        int viewerKeyCount = MacroKeyViewerState.ParseKeyNames(settings.MacroKeyViewerKeysText).Length;
        return Math.Max(
            1,
            Math.Max(
                Math.Max(1, settings.VirtualInputKeyCount),
                viewerKeyCount > 0 ? viewerKeyCount : fallback));
    }

    private static void AddIndividualInputPlanEntries(
        List<InputPlanEntry> entries,
        IReadOnlyList<MacroPlanEntry> macroPlan,
        int startIndex,
        int endIndexExclusive)
    {
        for (int planIndex = startIndex; planIndex < endIndexExclusive; planIndex++)
        {
            AddSingleInputPlanEntry(entries, macroPlan, planIndex);
        }
    }

    private static void AddSingleInputPlanEntry(
        List<InputPlanEntry> entries,
        IReadOnlyList<MacroPlanEntry> macroPlan,
        int planIndex)
    {
        MacroPlanEntry macroEntry = macroPlan[planIndex];
        entries.Add(new InputPlanEntry(
            planIndex,
            planIndex + 1,
            macroEntry.SeqId,
            macroEntry.SeqId,
            macroEntry.TargetTimeSeconds,
            macroEntry.TargetTimeSeconds,
            rawEntryCount: 1,
            emittedHitCount: 1,
            containsMidspin: macroEntry.IsMidspin,
            isNearMidspin: macroEntry.IsNearMidspin,
            isExactDuplicateGroup: false,
            isCompressed: false));
    }

    private static void LogNormal(Action<string>? log, string message)
    {
        log?.Invoke(message);
    }
}
}
