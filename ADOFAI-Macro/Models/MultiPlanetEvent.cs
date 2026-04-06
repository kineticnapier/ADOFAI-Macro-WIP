using System.Security.Cryptography.X509Certificates;

namespace ADOFAI_Macro.Models;

public sealed record MultiPlanetEvent(
    int FloorIndex,
    int PlanetCount // 2か3
);