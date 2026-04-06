namespace ADOFAI_Macro.Models;


public sealed record AutoPlayTilesEvent(
    int FloorIndex,
    bool Enabled
);