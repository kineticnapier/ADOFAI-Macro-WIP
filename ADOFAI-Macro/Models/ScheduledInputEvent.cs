namespace ADOFAI_Macro.Models;

public sealed class ScheduledInputEvent(long targetTick, FingerKey key, InputEventType type)
{
    public long TargetTick { get; set; } = targetTick;
    public FingerKey Key { get; } = key;
    public InputEventType Type { get; } = type;
}