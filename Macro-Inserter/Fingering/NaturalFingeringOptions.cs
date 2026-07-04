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
    public static PseudoChordUiLogMode LogMode { get; set; } = PseudoChordUiLogMode.Minimal;
    public static bool EnableLagSpikeLog { get; set; } = true;
    public static double LagSpikeLogMs { get; set; } = 8.0;
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
            EnableLagSpikeLog = PlayerPrefs.GetInt(Prefix + nameof(EnableLagSpikeLog), EnableLagSpikeLog ? 1 : 0) != 0;
            LagSpikeLogMs = GetDouble(nameof(LagSpikeLogMs), LagSpikeLogMs);
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
            PlayerPrefs.SetString(Prefix + nameof(LogMode), LogMode.ToString());
            PlayerPrefs.SetInt(Prefix + nameof(EnableLagSpikeLog), EnableLagSpikeLog ? 1 : 0);
            SetDouble(nameof(LagSpikeLogMs), LagSpikeLogMs);
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
}
