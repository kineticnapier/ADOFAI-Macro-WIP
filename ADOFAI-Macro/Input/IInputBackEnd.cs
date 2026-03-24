using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Input;

public interface IInputBackend
{
    void KeyDown(FingerKey key);
    void KeyUp(FingerKey key);
}