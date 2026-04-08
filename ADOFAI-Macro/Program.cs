using ADOFAI_Macro.Fingering;
using ADOFAI_Macro.Input;
using ADOFAI_Macro.Models;
using ADOFAI_Macro.Parsing;
using ADOFAI_Macro.Scheduling;

using System.Runtime.InteropServices;

namespace ADOFAI_Macro;

internal static partial class NativeMethods
{
    [DllImport("winmm.dll", SetLastError = true)]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", SetLastError = true)]
    public static extern uint timeEndPeriod(uint uPeriod);
}

internal static class Program
{
    static void Main(string[] args)
    {
        NativeMethods.timeBeginPeriod(1);

        try
        {
            RunMain(args);

        }
        catch (Exception ex)
        {
            Console.WriteLine("エラーが発生しました: " + ex.Message);
            Console.WriteLine("スタックトレース: " + ex.StackTrace);
        }
        finally
        {
            NativeMethods.timeEndPeriod(1);
        }
    }

    static void RunMain(string[] args)
    {
        string? path = null;
        if (args.Length > 0)
        {
            path = args[0];
        } else
        {
            Console.WriteLine("Input the path to the adofai map:");
            path = Console.ReadLine();
        }
        if (path == string.Empty || path == null) throw new InvalidOperationException("No path provided.");

        MacroSettings settings = new();

        //KeyGroup keyGroup = new(
        //    FingerKey.E,
        //    FingerKey.P,
        //    FingerKey.D2,
        //    FingerKey.Caret
        //);

        RawChart rawChart = ChartLoader.Load(path);
        ParsedChart parsedChart = ChartParser.Parse(rawChart);

        IReadOnlyList<double> delayTable = DelayTableGenerator.Generate(parsedChart.Notes, settings.GlobalOffsetMs);

        //IFingeringStrategy fingeringStrategy =
        //    new AdvancedFingeringStrategy(
        //        keyGroup,
        //        settings.PseudoChordThreshold,
        //        settings.StreamAngle);

        IFingeringStrategy fingeringStrategy =
            new SequentialFingeringStrategy(
            [
                FingerKey.A,
                FingerKey.B,
                FingerKey.C,
                FingerKey.D,
                FingerKey.E,
                FingerKey.F,
                FingerKey.G,
                FingerKey.H,
                //FingerKey.I,
                //FingerKey.J,
                FingerKey.K,
                FingerKey.L,
                FingerKey.D1,
                FingerKey.N,
                //FingerKey.O,
                //FingerKey.P,
                //FingerKey.Q,
                //FingerKey.R,
                //FingerKey.S,
                //FingerKey.T,
                //FingerKey.U,
                //FingerKey.V,
                //FingerKey.W,
                //FingerKey.X,
                //FingerKey.Y,
                //FingerKey.Z,
                //FingerKey.D2,
                //FingerKey.D3,
                //FingerKey.D4,
                //FingerKey.D5,
                //FingerKey.D6,
                //FingerKey.D7,
                //FingerKey.D8,
                //FingerKey.D9,
                //FingerKey.D0,
                //FingerKey.Enter
            ]);

        // IFingeringStrategy fingeringStrategy =
        //     new PseudoChordFingeringStrategy(keyGroup, settings.PseudoChordThreshold);

        IReadOnlyList<FingerKey> fingering = fingeringStrategy.Generate(parsedChart.Notes);

        IReadOnlyList<ScheduledNote> scheduledNotes =
            ScheduleBuilder.Build(parsedChart.Notes, delayTable, fingering);

        IReadOnlyList<ScheduledInputEvent> inputEvents =
            InputEventBuilder.Build(
                scheduledNotes,
                settings.NormalHoldMs,
                settings.ReleaseLeadMs
            );

        Console.WriteLine("最初のタイルを手動で叩いて開始してください。(開始はSpaceキー)");
        Console.WriteLine("再生中: ←で早める / →で遅らせる");

        long startTick = StartTrigger.WaitForFirstPress(VirtualKeys.SPACE);

        IInputBackend backend = new WindowsInputBackend();
        InputScheduler scheduler = new(backend);

        using CancellationTokenSource cts = new();

        OffsetController offsetController = new(scheduler, 0.5);

        Thread offsetThread = new(() => offsetController.Run(cts.Token))
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };

        offsetThread.Start();

        try
        {
            scheduler.PlayEventsFromBaseTick(inputEvents, startTick, cts.Token);
        }
        finally
        {
            cts.Cancel();
            offsetThread.Join(50);
        }
    }
}
