namespace ADOFAI_Macro.Models;

public sealed record KeyGroup(
    FingerKey MainLeft,
    FingerKey MainRight,
    FingerKey SideLeft,
    FingerKey SideRight
);