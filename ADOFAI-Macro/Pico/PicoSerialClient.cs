using System.IO.Ports;
using System.Text;

using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Pico;

public sealed class PicoSerialClient : IDisposable
{
    private readonly SerialPort _port;

    public PicoSerialClient(string portName, int baudRate = 115200)
    {
        _port = new SerialPort(portName, baudRate)
        {
            NewLine = "\r\n",
            Encoding = Encoding.ASCII,
            ReadTimeout = 3000,
            WriteTimeout = 3000
        };
    }

    public void Open()
    {
        if (_port.IsOpen)
        {
            return;
        }

        _port.DtrEnable = true;
        _port.RtsEnable = true;

        _port.Open();
        Thread.Sleep(1500);

        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
    }

    public void Dispose()
    {
        _port.Dispose();
    }

    public void ResetEvents()
    {
        RetryCommand("RESET", maxAttempts: 5);
    }
    private void RetryCommand(string command, int maxAttempts = 3)
    {
        string? lastResponse = null;

        for (int i = 0; i < maxAttempts; i++)
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();

            WriteLine(command);

            string response = ReadResponse();
            lastResponse = response;

            if (response.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (response.Equals("ERR UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                Thread.Sleep(300);
                continue;
            }

            throw new InvalidOperationException($"{command} の応答が異常です: {response}");
        }

        throw new InvalidOperationException($"{command} の応答が異常です: {lastResponse}");
    }

    public void AddOffsetUs(long deltaUs)
    {
        WriteLine($"OFFSET,{deltaUs}");
        EnsureOk(ReadResponse(), "OFFSET");
    }
    public void Stop()
    {
        WriteLine("STOP");
        EnsureOk(ReadResponse(), "STOP");
    }

    public void SendEvents(IReadOnlyList<PicoInputEvent> events)
    {
        foreach (PicoInputEvent e in events)
        {
            WriteLine($"EVENT,{e.OffsetUs},{e.KeyName},{e.EventType}");
            EnsureOk(ReadResponse(), "EVENT");
        }

        WriteLine("COMMIT");
        EnsureOk(ReadResponse(), "COMMIT");
    }

    public void Start()
    {
        WriteLine("START");
        EnsureOk(ReadResponse(), "START");
    }

    public void StartWithDelayUs(uint delayUs)
    {
        WriteLine($"START,{delayUs}");
        EnsureOk(ReadResponse(), "START");
    }

    private void WriteLine(string text)
    {
        _port.WriteLine(text);
    }

    private string ReadResponse()
    {
        for (int i = 0; i < 10; i++)
        {
            string line = _port.ReadLine().Trim();

            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            if (line.StartsWith("OK", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("ERR", StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        throw new TimeoutException("有効な応答を受信できませんでした。");
    }

    private static void EnsureOk(string response, string commandName)
    {
        if (!response.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{commandName} の応答が異常です: {response}");
        }
    }
}