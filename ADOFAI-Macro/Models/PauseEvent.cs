namespace ADOFAI_Macro.Models;

public sealed record PauseEvent(
    int FloorIndex,
    double Duration
);