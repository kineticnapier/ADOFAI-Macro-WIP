namespace ADOFAI_Macro.Fingering;

public sealed class HandKeyCountAllocator
{
    private bool _preferLeftForOdd = true;

    public (int leftCount, int rightCount) Allocate(int totalKeyCount)
    {
        int leftCount = totalKeyCount / 2;
        int rightCount = totalKeyCount / 2;

        if (totalKeyCount % 2 == 1)
        {
            if (_preferLeftForOdd)
            {
                leftCount++;
            }
            else
            {
                rightCount++;
            }

            _preferLeftForOdd = !_preferLeftForOdd;
        }

        return (leftCount, rightCount);
    }
}