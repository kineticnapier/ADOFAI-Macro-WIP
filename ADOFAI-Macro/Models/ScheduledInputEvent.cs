namespace ADOFAI_Macro.Models;

public sealed class ScheduledInputEvent
{
    public long TargetTick { get; set; }
    public FingerKey Key { get; }
    public InputEventType Type { get; }

    public ScheduledInputEvent(long targetTick, FingerKey key, InputEventType type)
    {
        TargetTick = targetTick;
        Key = key;
        Type = type;
    }
}