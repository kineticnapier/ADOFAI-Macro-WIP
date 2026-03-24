using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Fingering;

public interface IFingeringStrategy
{
    IReadOnlyList<FingerKey> Generate(IReadOnlyList<ChartNote> notes);
}