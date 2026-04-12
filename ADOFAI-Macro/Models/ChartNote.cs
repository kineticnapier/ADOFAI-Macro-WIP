
namespace ADOFAI_Macro.Models;

public sealed record ChartNote(
    int Index,
    int TileIndex,
    double TimeMs,
    double RelativeAngle,
    bool IsAutoTile
);