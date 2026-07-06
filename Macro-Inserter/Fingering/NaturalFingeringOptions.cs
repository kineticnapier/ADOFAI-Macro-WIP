using System;
using UnityEngine;

namespace Macro_Inserter;

internal enum PseudoChordUiLogMode
{
    None = 0,
    Minimal = 1,
    Normal = 2,
    Verbose = 3
}

internal static class NaturalFingeringOptions
{
    private const string Prefix = "MacroInserter.PseudoChord.v40.";

    public static bool CleanUiEnabled { get; set; } = true;
    public static double FoldDownMaxBpm { get; set; } = 1000.0;
    public static double RaiseUpMaxBpm { get; set; } = 500.0;

    // v65: when one visual beat bucket overflows the upper-bank keys, switch that
    // whole BPM section to a stable chronological rolling order instead of resetting
    // to the same-side upper bank every beat. This keeps dense / magic-circle parts
    // looking like two-hand rolling instead of large one-beat key dumps.
    public static bool EnableRollingOverflowFingering { get; set; } = true;
    public static int RollingOverflowStartInputs { get; set; } = 5;
    public static int RollingOverflowMaxKeys { get; set; } = 24;
    public static bool RollingOverflowUseFeet { get; set; } = true;

    public static PseudoChordUiLogMode LogMode { get; set; } = PseudoChordUiLogMode.Minimal;

    public static bool EnableFingeringLog { get; set; } = true;
    public static int FingeringNormalLogLimit { get; set; } = 96;
    public static int FingeringVerboseLogLimit { get; set; } = 384;

    // Processing spike: time spent inside the directKeyTimes prefix itself.
    public static bool EnableLagSpikeLog { get; set; } = true;
    public static double LagSpikeLogMs { get; set; } = 8.0;

    // Late spike: scheduler/input entry was already late when directKeyTimes ran.
    public static bool EnableLateSpikeLog { get; set; } = true;
    public static double LateSpikeLogMs { get; set; } = 8.0;
    public static double SpikeLogMinIntervalMs { get; set; } = 50.0;

    public static bool EnableRain { get; set; } = true;
    public static double RainGrowPerSecond { get; set; } = 220.0;
    public static double RainDecayPerSecond { get; set; } = 55.0;

    private static bool loaded;

    public static void Load()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        try
        {
            CleanUiEnabled = PlayerPrefs.GetInt(Prefix + nameof(CleanUiEnabled), CleanUiEnabled ? 1 : 0) != 0;
            FoldDownMaxBpm = GetDouble(nameof(FoldDownMaxBpm), FoldDownMaxBpm);
            RaiseUpMaxBpm = GetDouble(nameof(RaiseUpMaxBpm), RaiseUpMaxBpm);
            EnableRollingOverflowFingering = PlayerPrefs.GetInt(Prefix + nameof(EnableRollingOverflowFingering), EnableRollingOverflowFingering ? 1 : 0) != 0;
            RollingOverflowStartInputs = GetInt(nameof(RollingOverflowStartInputs), RollingOverflowStartInputs);
            RollingOverflowMaxKeys = GetInt(nameof(RollingOverflowMaxKeys), RollingOverflowMaxKeys);
            RollingOverflowUseFeet = PlayerPrefs.GetInt(Prefix + nameof(RollingOverflowUseFeet), RollingOverflowUseFeet ? 1 : 0) != 0;
            EnableFingeringLog = PlayerPrefs.GetInt(Prefix + nameof(EnableFingeringLog), EnableFingeringLog ? 1 : 0) != 0;
            FingeringNormalLogLimit = GetInt(nameof(FingeringNormalLogLimit), FingeringNormalLogLimit);
            FingeringVerboseLogLimit = GetInt(nameof(FingeringVerboseLogLimit), FingeringVerboseLogLimit);
            EnableLagSpikeLog = PlayerPrefs.GetInt(Prefix + nameof(EnableLagSpikeLog), EnableLagSpikeLog ? 1 : 0) != 0;
            LagSpikeLogMs = GetDouble(nameof(LagSpikeLogMs), LagSpikeLogMs);
            EnableLateSpikeLog = PlayerPrefs.GetInt(Prefix + nameof(EnableLateSpikeLog), EnableLateSpikeLog ? 1 : 0) != 0;
            LateSpikeLogMs = GetDouble(nameof(LateSpikeLogMs), LateSpikeLogMs);
            SpikeLogMinIntervalMs = GetDouble(nameof(SpikeLogMinIntervalMs), SpikeLogMinIntervalMs);
            EnableRain = PlayerPrefs.GetInt(Prefix + nameof(EnableRain), EnableRain ? 1 : 0) != 0;
            RainGrowPerSecond = GetDouble(nameof(RainGrowPerSecond), RainGrowPerSecond);
            RainDecayPerSecond = GetDouble(nameof(RainDecayPerSecond), RainDecayPerSecond);

            string modeText = PlayerPrefs.GetString(Prefix + nameof(LogMode), LogMode.ToString());
            if (Enum.TryParse(modeText, ignoreCase: true, out PseudoChordUiLogMode parsedMode))
            {
                LogMode = parsedMode;
            }
        }
        catch
        {
            // Keep defaults if PlayerPrefs is not available during very early module initialization.
        }
    }

    public static void Save()
    {
        try
        {
            PlayerPrefs.SetInt(Prefix + nameof(CleanUiEnabled), CleanUiEnabled ? 1 : 0);
            SetDouble(nameof(FoldDownMaxBpm), FoldDownMaxBpm);
            SetDouble(nameof(RaiseUpMaxBpm), RaiseUpMaxBpm);
            PlayerPrefs.SetInt(Prefix + nameof(EnableRollingOverflowFingering), EnableRollingOverflowFingering ? 1 : 0);
            SetInt(nameof(RollingOverflowStartInputs), RollingOverflowStartInputs);
            SetInt(nameof(RollingOverflowMaxKeys), RollingOverflowMaxKeys);
            PlayerPrefs.SetInt(Prefix + nameof(RollingOverflowUseFeet), RollingOverflowUseFeet ? 1 : 0);
            PlayerPrefs.SetString(Prefix + nameof(LogMode), LogMode.ToString());
            PlayerPrefs.SetInt(Prefix + nameof(EnableFingeringLog), EnableFingeringLog ? 1 : 0);
            SetInt(nameof(FingeringNormalLogLimit), FingeringNormalLogLimit);
            SetInt(nameof(FingeringVerboseLogLimit), FingeringVerboseLogLimit);
            PlayerPrefs.SetInt(Prefix + nameof(EnableLagSpikeLog), EnableLagSpikeLog ? 1 : 0);
            SetDouble(nameof(LagSpikeLogMs), LagSpikeLogMs);
            PlayerPrefs.SetInt(Prefix + nameof(EnableLateSpikeLog), EnableLateSpikeLog ? 1 : 0);
            SetDouble(nameof(LateSpikeLogMs), LateSpikeLogMs);
            SetDouble(nameof(SpikeLogMinIntervalMs), SpikeLogMinIntervalMs);
            PlayerPrefs.SetInt(Prefix + nameof(EnableRain), EnableRain ? 1 : 0);
            SetDouble(nameof(RainGrowPerSecond), RainGrowPerSecond);
            SetDouble(nameof(RainDecayPerSecond), RainDecayPerSecond);
            PlayerPrefs.Save();
        }
        catch
        {
            // Ignore persistence failures; runtime values still apply for this session.
        }
    }

    public static bool ShouldLog(PseudoChordUiLogMode required)
    {
        Load();
        return LogMode != PseudoChordUiLogMode.None && LogMode >= required;
    }

    public static PseudoChordUiLogMode FromLoggingMode(LoggingMode mode)
    {
        return mode switch
        {
            LoggingMode.None => PseudoChordUiLogMode.None,
            LoggingMode.Minimal => PseudoChordUiLogMode.Minimal,
            LoggingMode.Normal => PseudoChordUiLogMode.Normal,
            LoggingMode.Verbose => PseudoChordUiLogMode.Verbose,
            _ => PseudoChordUiLogMode.Minimal
        };
    }

    public static LoggingMode ToLoggingMode(PseudoChordUiLogMode mode)
    {
        return mode switch
        {
            PseudoChordUiLogMode.None => LoggingMode.None,
            PseudoChordUiLogMode.Minimal => LoggingMode.Minimal,
            PseudoChordUiLogMode.Normal => LoggingMode.Normal,
            PseudoChordUiLogMode.Verbose => LoggingMode.Verbose,
            _ => LoggingMode.Minimal
        };
    }

    private static double GetDouble(string name, double fallback)
    {
        string text = PlayerPrefs.GetString(Prefix + name, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;
    }

    private static void SetDouble(string name, double value)
    {
        PlayerPrefs.SetString(Prefix + name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static int GetInt(string name, int fallback)
    {
        string text = PlayerPrefs.GetString(Prefix + name, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;
    }

    private static void SetInt(string name, int value)
    {
        PlayerPrefs.SetString(Prefix + name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
