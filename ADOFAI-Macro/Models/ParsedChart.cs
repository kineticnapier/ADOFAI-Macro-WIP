namespace ADOFAI_Macro.Models;

public sealed record ParsedChart(
    IReadOnlyList<double> RelativeAngles,
    IReadOnlyList<double> TileBpms,
    IReadOnlyList<ChartNote> Notes
);