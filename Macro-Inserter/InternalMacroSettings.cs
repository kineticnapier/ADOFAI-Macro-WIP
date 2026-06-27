using UnityModManagerNet;

namespace Macro_Inserter;

public sealed class InternalMacroSettings : UnityModManager.ModSettings
{
    public bool EnableInternalMacro = false;
    public bool DryRun = false;
    public double MacroOffsetMs = 0.0;
    public bool StartFromCurrentFloor = false;
    public bool UseAudioTime = true;
    public ClockMode ClockMode = ClockMode.ConductorSongPosition;
    public FireMode FireMode = FireMode.DirectHit;
    public FirstHitMode FirstHitMode = FirstHitMode.Manual;
    public StateMode StateMode = StateMode.Default;
    public LoggingMode LoggingMode = LoggingMode.Minimal;
    public FailureMode FailureMode = FailureMode.Stop;
    public double MaxLateRetryMs = 40.0;
    public bool EnableHighDensityMode = false;
    public int MaxHitsPerPlayerControlUpdate = 8;
    public bool ExperimentalTimeSpoofForDirectHit = false;
    public bool EnableAdaptiveOffset = false;
    public bool ValidateAfterHit = false;
    public bool DirectHitIgnoreInput = true;
    public string VirtualInputKey = "Space";
    public int VirtualInputKeyCount = 1;

    public override void Save(UnityModManager.ModEntry modEntry)
    {
        Save(this, modEntry);
    }
}
