namespace ADOFAI_Macro.Models;

public sealed class PicoInputEvent(uint offsetUs, string keyName, string eventType)
{
    public uint OffsetUs { get; } = offsetUs;
    public string KeyName { get; } = keyName;
    public string EventType { get; } = eventType; // "DOWN" or "UP"
}