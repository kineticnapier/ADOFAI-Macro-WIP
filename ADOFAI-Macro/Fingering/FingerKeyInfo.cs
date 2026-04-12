using ADOFAI_Macro.Models;
namespace ADOFAI_Macro.Fingering;

public static class FingerKeyInfo
{
    public static Hand GetHand(FingerKey key)
    {
        return key switch
        {
            //FingerKey.E => Hand.Left,
            //FingerKey.D2 => Hand.Left,
            //FingerKey.P => Hand.Right,
            //FingerKey.Caret => Hand.Right,
            FingerKey.D => Hand.Left,
            FingerKey.C => Hand.Left,
            FingerKey.B => Hand.Left,
            FingerKey.A => Hand.Left,

            FingerKey.E => Hand.Right,
            FingerKey.F => Hand.Right,
            FingerKey.G => Hand.Right,
            FingerKey.H => Hand.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
    }
}