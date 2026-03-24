namespace ADOFAI_Macro.Models;

public sealed record SpeedEvent(
    int FloorIndex,
    SpeedEventType Type,
    double Value
);