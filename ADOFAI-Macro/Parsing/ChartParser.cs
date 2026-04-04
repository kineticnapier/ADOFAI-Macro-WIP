using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Parsing;

public sealed class ChartParser
{
    public ParsedChart Parse(RawChart raw)
    {
        IList<double> angleData = raw.AngleData as IList<double> ?? raw.AngleData.ToList();

        List<double> relativeAngles = AdofaiAngleConverter.ConvertToRelativeAngles(
            angleData,
            raw.TwirlFloors);

        List<double> tileBpms = AdofaiBpmCalculator.BuildTileBpmList(
            angleData,
            raw.InitialBpm,
            raw.SpeedEvents);

        List<ChartNote> notes = BuildNotes(relativeAngles, tileBpms);

        return new ParsedChart(relativeAngles, tileBpms, notes);
    }

    private static List<ChartNote> BuildNotes(
        IReadOnlyList<double> relativeAngles,
        IReadOnlyList<double> tileBpms)
    {
        if (relativeAngles.Count != tileBpms.Count)
            throw new InvalidOperationException("relativeAngles and tileBpms count mismatch.");

        List<ChartNote> notes = new(relativeAngles.Count);

        if (relativeAngles.Count == 0)
            return notes;

        double currentTimeMs = 0.0;

        // ややこしいので変更しないで
        // i=1から始めると、一番最初のタイルが無視され、そこを手動で補正するのでこれでよい
        for (int i = 1; i < relativeAngles.Count; i++)
        {
            double beats = relativeAngles[i] / 180.0;
            double msPerBeat = 60000.0 / tileBpms[i];
            double deltaMs = beats * msPerBeat;
            currentTimeMs += deltaMs;


            notes.Add(new ChartNote(
                i,
                currentTimeMs,
                relativeAngles[i]
            ));


        }

        return notes;
    }
}
