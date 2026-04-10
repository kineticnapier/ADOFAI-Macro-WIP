using ADOFAI_Macro.Fingering;
using ADOFAI_Macro.Input;
using ADOFAI_Macro.Models;
using ADOFAI_Macro.Parsing;
using ADOFAI_Macro.Scheduling;

using System.Runtime.InteropServices;

using static System.Runtime.InteropServices.JavaScript.JSType;

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

        List<FingerKey> usingKeys = [
                FingerKey.A,
                FingerKey.B,
                FingerKey.C,
                FingerKey.D,
                FingerKey.E,
                FingerKey.F,
                FingerKey.G,
                FingerKey.H,
                FingerKey.I,
                FingerKey.J,
                FingerKey.K,
                FingerKey.L,
                FingerKey.D1,
                FingerKey.N,
                FingerKey.O,
                FingerKey.P,
                FingerKey.Q,
                FingerKey.R,
                FingerKey.S,
                FingerKey.T,
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
            ];

        List<KeyCountRange> ranges = ReadKeyCountRangesFromConsole();

        var keyCounts = KeyCountResolver.ResolvePerNoteKeyCounts(
            parsedChart.Notes, 
            ranges, 
            defaultKeyCount: usingKeys.Count);

        IFingeringStrategy fingeringStrategy =
            new KeyLimitedSequentialFingeringStrategy(
                usingKeys,
                keyCounts
            );

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

        Console.WriteLine("Please hit the first tile manually to start. (Default start key: Space)");
        Console.WriteLine("During playback: Left arrow to speed up / Right arrow to slow down");

        Console.WriteLine("");
        Console.WriteLine("------------");
        Console.WriteLine("Tips for getting a Perfect Play (PP)");
        Console.WriteLine("1. Keep the number of keys set to a minimum (using different input keys can cause slight processing lag).");
        Console.WriteLine("2. Watch the judgment bar and adjust the offset with the arrow keys as needed.");
        Console.WriteLine("3. For charts with strict judgment limits like HALL or long wait times at the beginning, leave it to luck.");
        Console.WriteLine("------------");

        Console.WriteLine("");
        Console.WriteLine("Note: 4 - digit KPS will always fail because the input lag is too severe. (In the developer's environment, 500 KPS was the limit.)");

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


    public static List<KeyCountRange> ReadKeyCountRangesFromConsole()
    {
        Console.Write("Please enter the number of key limit changes:");
        int count = int.Parse(Console.ReadLine() ?? throw new InvalidOperationException());

        List<KeyCountRange> result = new(count);

        for (int i = 0; i < count; i++)
        {
            Console.Write($"Limit {i + 1} (Start tile number (1-indexed) Key count):");
            string line = Console.ReadLine() ?? throw new InvalidOperationException();

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
            {
                throw new FormatException("The input format is \"(Start tile number) (Key count)\".");
            }

            int startTileNumber = int.Parse(parts[0]);
            int keyCount = int.Parse(parts[1]);

            if (startTileNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startTileNumber), "Please enter a start tile number of 1 or greater.");
            }

            result.Add(new KeyCountRange
            {
                StartTileIndex = startTileNumber - 1,
                KeyCount = keyCount
            });
        }

        return result;
    }
}
