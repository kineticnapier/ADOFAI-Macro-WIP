namespace ADOFAI_Macro.Models;

public sealed record RawChart(
    double InitialBpm,
    IReadOnlyList<double> AngleData,
    IReadOnlyList<int> TwirlFloors,
    IReadOnlyList<SpeedEvent> SpeedEvents,
    IReadOnlyList<PauseEvent> PauseEvents,
    IReadOnlyList<HoldEvent> HoldEvents,
    IReadOnlyList<MultiPlanetEvent> MultiPlanetEvents
);