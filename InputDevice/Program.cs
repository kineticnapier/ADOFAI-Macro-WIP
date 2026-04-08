using System.IO.Ports;

namespace InputDevice
{
    internal class Program
    {
        static void Main(string[] args)
        {
            using var port = new SerialPort("COM4", 115200);
            port.Open();
            var strings = "hello world";
            foreach (var s in strings)
            {
                Thread.Sleep(1000);
                port.Write(s.ToString());
            }

            port.Close();
        }
    }
}
