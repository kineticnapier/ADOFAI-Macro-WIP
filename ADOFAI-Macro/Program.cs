using ADOFAI_Macro.Fingering;
using ADOFAI_Macro.Input;
using ADOFAI_Macro.Models;
using ADOFAI_Macro.Parsing;
using ADOFAI_Macro.Scheduling;

namespace ADOFAI_Macro;

internal static class Program
{
    static void Main(string[] args)
    {
        string path = Console.ReadLine()
            ?? throw new InvalidOperationException("No path provided.");

        MacroSettings settings = new();

        KeyGroup keyGroup = new(
            FingerKey.E,
            FingerKey.P,
            FingerKey.D2,
            FingerKey.Caret
        );

        RawChart rawChart = new ChartLoader().Load(path);
        ParsedChart parsedChart = new ChartParser().Parse(rawChart);

        IReadOnlyList<double> delayTable =
            new DelayTableGenerator().Generate(parsedChart.Notes, settings.GlobalOffsetMs);

        //IFingeringStrategy fingeringStrategy =
        //    new AdvancedFingeringStrategy(
        //        keyGroup,
        //        settings.PseudoChordThreshold,
        //        settings.StreamAngle);

        // 差し替え例
        IFingeringStrategy fingeringStrategy =
            new SequentialFingeringStrategy(new[]
            {
                 FingerKey.Tab, FingerKey.D1, FingerKey.D2, FingerKey.E,
                 FingerKey.P, FingerKey.Caret, FingerKey.Backslash, FingerKey.Enter
            });

        // IFingeringStrategy fingeringStrategy =
        //     new PseudoChordFingeringStrategy(keyGroup, settings.PseudoChordThreshold);

        IReadOnlyList<FingerKey> fingering = fingeringStrategy.Generate(parsedChart.Notes);

        IReadOnlyList<ScheduledNote> scheduledNotes =
            new ScheduleBuilder().Build(parsedChart.Notes, delayTable, fingering);

        IReadOnlyList<ScheduledNote> autoNotes = scheduledNotes.Skip(1).ToList();

        IReadOnlyList<ScheduledInputEvent> inputEvents =
            new InputEventBuilder().Build(
                autoNotes,
                settings.NormalHoldMs,
                settings.StreamHoldMs,
                settings.ReleaseLeadMs,
                settings.StreamAngle);

        Console.WriteLine("最初のタイルを手動で叩いて開始してください。");
        long startTick = new StartTrigger().WaitForFirstPress(VirtualKeys.SPACE);

        IInputBackend backend = new WindowsInputBackend();
        InputScheduler scheduler = new(backend);
        scheduler.PlayEventsFromBaseTick(inputEvents, startTick);
    }
}