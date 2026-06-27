namespace Macro_Inserter;

internal sealed record HitInvokeResult(
    bool Accepted,
    bool ImmediateAdvanced,
    bool AtOrPastTarget,
    bool ShouldConsume,
    int BeforeFloorSeqId,
    int AfterFloorSeqId,
    int TargetSeqId);
