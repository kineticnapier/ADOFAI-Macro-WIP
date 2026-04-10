using ADOFAI_Macro.Fingering;
using ADOFAI_Macro.Input;
using ADOFAI_Macro.Models;
using ADOFAI_Macro.Parsing;
using ADOFAI_Macro.Pico;
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
        }
        else
        {
            Console.WriteLine("Input the path to the adofai map:");
            path = Console.ReadLine();
        }

        if (path == string.Empty || path == null)
        {
            throw new InvalidOperationException("No path provided.");
        }

        MacroSettings settings = new();

        RawChart rawChart = ChartLoader.Load(path);
        ParsedChart parsedChart = ChartParser.Parse(rawChart);

        IReadOnlyList<double> delayTable = DelayTableGenerator.Generate(parsedChart.Notes, settings.GlobalOffsetMs);

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
            FingerKey.U,
            FingerKey.V,
            FingerKey.W,
            FingerKey.X,
            FingerKey.Y,
            FingerKey.Z,
            FingerKey.D2,
            FingerKey.D3,
            FingerKey.D4,
            FingerKey.D5,
            FingerKey.D6,
            FingerKey.D7,
            FingerKey.D8,
            FingerKey.D9,
            FingerKey.D0,
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

        IReadOnlyList<FingerKey> fingering = fingeringStrategy.Generate(parsedChart.Notes);

        IReadOnlyList<ScheduledNote> scheduledNotes =
        ScheduleBuilder.Build(parsedChart.Notes, delayTable, fingering);

        if (scheduledNotes.Count < 2)
        {
            throw new InvalidOperationException("ノート数が不足しています。");
        }

        long firstManualNoteTick = scheduledNotes[0].TargetTick;

        IReadOnlyList<ScheduledNote> autoNotes = scheduledNotes
            .Skip(1)
            .ToList();

        IReadOnlyList<ScheduledInputEvent> inputEvents =
            InputEventBuilder.Build(
                autoNotes,
                settings.NormalHoldMs,
                settings.ReleaseLeadMs
            );

        const long StartCompensationUs = -250000;

        IReadOnlyList<PicoInputEvent> picoEvents =
            PicoInputEventConverter.Convert(
                inputEvents,
                firstManualNoteTick,
                StartCompensationUs);

        Console.Write("PicoのCOMポート名を入力してください (例: COM3): ");
        string comPort = Console.ReadLine() ?? throw new InvalidOperationException("COMポートが入力されていません。");

        using PicoSerialClient pico = new(comPort);
        pico.Open();

        Console.WriteLine("Picoへイベントを送信しています...");
        pico.ResetEvents();
        pico.SendEvents(picoEvents);
        Console.WriteLine($"送信完了: {picoEvents.Count} events");

        Console.WriteLine("最初のタイルを手動で叩いて開始してください。(開始はSpaceキー)");
        Console.WriteLine("再生中: ←で早める、→で遅らせる");
        
        long startTick = StartTrigger.WaitForFirstPress(VirtualKeys.SPACE);
        _ = startTick;

        using CancellationTokenSource cts = new();

        PicoOffsetController offsetController = new(pico, 0.5);
        Thread offsetThread = new(() => offsetController.Run(cts.Token))
        {
            IsBackground = true
        };
        offsetThread.Start();

        EscapeStopController escapeStopController = new(pico);
        Thread escThread = new(() => escapeStopController.Run(cts.Token))
        {
            IsBackground = true
        }; 
        escThread.Start();

        try
        {
            Console.WriteLine("Picoへ開始コマンドを送信します。");
            pico.Start();

            long expectedFirstDownTick = autoNotes[0].TargetTick - firstManualNoteTick;
            long expectedFirstDownUs = PicoInputEventConverter.TickToMicroseconds(expectedFirstDownTick);

            PicoInputEvent? firstDownEvent = picoEvents
                .FirstOrDefault(e => e.EventType == "DOWN");

            Console.WriteLine("---- timing debug ----");
            Console.WriteLine($"manual note tick   : {firstManualNoteTick}");
            Console.WriteLine($"first auto tick    : {autoNotes[0].TargetTick}");
            Console.WriteLine($"expected down tick : {expectedFirstDownTick}");
            Console.WriteLine($"expected down us   : {expectedFirstDownUs}");

            if (firstDownEvent is not null)
            {
                Console.WriteLine($"actual first down  : {firstDownEvent.OffsetUs} us  {firstDownEvent.KeyName}");
            }
            else
            {
                Console.WriteLine("actual first down  : not found");
            }

            Console.WriteLine("----------------------");
            Console.ReadLine();
        }
        finally
        {
            cts.Cancel();

            try
            {
                pico.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Picoへの停止コマンド送信中にエラーが発生しました: " + ex.Message);
            }

            offsetThread.Join(50);
            escThread.Join(50);
        }
    }


    public static List<KeyCountRange> ReadKeyCountRangesFromConsole()
    {
        Console.Write("制限変更の数を入力してください: ");
        int count = int.Parse(Console.ReadLine() ?? throw new InvalidOperationException());

        List<KeyCountRange> result = new(count);

        for (int i = 0; i < count; i++)
        {
            Console.Write($"制限{i + 1} (開始タイル番号(1始まり) キー数): ");
            string line = Console.ReadLine() ?? throw new InvalidOperationException();

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
            {
                throw new FormatException("入力形式は \"開始タイル番号 キー数\" です。");
            }

            int startTileNumber = int.Parse(parts[0]);
            int keyCount = int.Parse(parts[1]);

            if (startTileNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startTileNumber), "開始タイル番号は1以上で入力してください。");
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
