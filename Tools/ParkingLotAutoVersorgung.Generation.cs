namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private long _avGeneration;

        private bool AvGenerationGueltig()
        {
            var register = World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>().Register;
            if (_avGeneration != register.Generation)
            {
                // Alte Entity-IDs niemals in der neuen Welt aufraeumen.
                _avKurse.Clear(); _avGebaut.Clear(); _avTrassen.Clear(); _avAusstehend.Clear();
                _avNetzstand.Clear(); _avVerbindungen.Clear();
                _avTraeger = Unity.Entities.Entity.Null; _avPhase = AvPhase.Idle;
                _avNochOffeneNetze = 0; _avGeneration = register.Generation;
            }
            return register.Bereit;
        }
    }
}
