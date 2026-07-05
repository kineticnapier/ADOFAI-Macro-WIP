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
    public double PseudoChordWindowMs = 2.0;
    public double PseudoChordMaxSpanMs = 2.0;
    public double PseudoChordExactDuplicateEpsilonMs = 0.05;
    public int MaxHitsPerPseudoChordGroup = 8;
    public bool ExperimentalTimeSpoofForDirectHit = false;
    public bool EnableAdaptiveOffset = false;
    public bool ValidateAfterHit = false;
    public bool DirectHitIgnoreInput = true;
    public string VirtualInputKey = "Space";
    public int VirtualInputKeyCount = 1;
    public bool EnableMacroKeyViewer = false;
    public string MacroKeyViewerKeysText = "A B C D E F G H I J K L Q R S T";
    public int MacroKeyViewerPulseMs = 80;
    public float MacroKeyViewerX = 20.0f;
    public float MacroKeyViewerY = -160.0f;
    public float MacroKeyViewerScale = 1.0f;
    public string MacroKeyViewerPressedColor = "#66D9FFFF";
    public string MacroKeyViewerIdleColor = "#202020DD";
    public string MacroKeyViewerTextColor = "#FFFFFFFF";
    public string MacroKeyViewerPanelColor = "#000000AA";
    public bool EnableKeyViewerRain = true;
    public float KeyViewerRainPulseMs = 70.0f;
    public float KeyViewerRainSpeedPxPerSec = 260.0f;
    public float KeyViewerRainFadeMs = 450.0f;
    public float KeyViewerRainWidthScale = 0.72f;
    public float KeyViewerRainMinHeightPx = 8.0f;
    public float KeyViewerRainMaxHeightPx = 90.0f;
    public float KeyViewerRainAlpha = 0.72f;
    public string KeyViewerRainColor = "#66D9FFFF";
    public int KeyViewerRainMaxSegments = 512;
    public float KeyViewerRainYOffsetPx = 2.0f;

    // v48: keep gameplay hot paths quiet.  These only control the deferred
    // directKeyTimes dump emitted after the scheduler stops.
    public bool DirectKeyTimesDumpOnlyOnFailure = true;
    public bool DirectKeyTimesDumpOnWin = false;
    public int DirectKeyTimesDeferredDumpEntries = 32;

    // v48: camera-safe mode prevents the runtime directKeyTimes plan from
    // advancing too many floors in one PlayerControl_Update after a hitch.
    public bool EnableCameraSafeMode = true;
    public int CameraSafeMaxHitsPerPlayerControlUpdate = 1;
    public bool CameraSafeStrictMode = true;
    public bool CameraSafeSplitInputGroups = true;
    // v50: queue keyTimes and let the game's normal PlayerControl_Update consume them.
    // This avoids calling Simulated_PlayerControl_Update inside the macro hot path,
    // which can advance floors before camera/track state has caught up.
    public bool CameraSafeQueueOnlyMode = true;

    public override void Save(UnityModManager.ModEntry modEntry)
    {
        Save(this, modEntry);
    }
}
