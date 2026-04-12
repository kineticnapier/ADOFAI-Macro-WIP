namespace ADOFAI_Macro.Fingering;

public sealed class KeyCountDecider
{
    private readonly FingeringDensityProfile _densityProfile;

    public KeyCountDecider(FingeringDensityProfile densityProfile)
    {
        _densityProfile = densityProfile;
    }

    public int DecideRequiredKeyCount(double avgDeltaMs, int maxAvailableKeys)
    {
        int keyCount;

        if (avgDeltaMs >= _densityProfile.TwoKeyMaxDeltaMs)
        {
            keyCount = 2;
        }
        else if (avgDeltaMs >= _densityProfile.ThreeKeyMaxDeltaMs)
        {
            keyCount = 3;
        }
        else if (avgDeltaMs >= _densityProfile.FourKeyMaxDeltaMs)
        {
            keyCount = 4;
        }
        else if (avgDeltaMs >= _densityProfile.FiveKeyMaxDeltaMs)
        {
            keyCount = 5;
        }
        else if (avgDeltaMs >= _densityProfile.SixKeyMaxDeltaMs)
        {
            keyCount = 6;
        }
        else if (avgDeltaMs >= _densityProfile.SevenKeyMaxDeltaMs)
        {
            keyCount = 7;
        }
        else
        {
            keyCount = 8;
        }

        if (keyCount > maxAvailableKeys)
        {
            keyCount = maxAvailableKeys;
        }

        if (keyCount < 2)
        {
            keyCount = 2;
        }

        return keyCount;
    }
}