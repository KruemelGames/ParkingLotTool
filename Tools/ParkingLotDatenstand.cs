using Colossal.Serialization.Entities;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * Bis zu welchem Schritt des `Migrationskatalog`s ein Parkplatz
     * nachgeruestet ist. Fehlt die Komponente, ist der Stand 0.
     *
     * EIN EIGENER TYP, keine Erweiterung eines bestehenden: CS2 rahmt je
     * Komponententyp einen Block. Eine aeltere PLT-Fassung ueberspringt einen
     * ihr unbekannten Typ als "obsolete" - ein zusaetzliches Feld in einem
     * bekannten Typ dagegen verschoebe alle folgenden Datensaetze.
     */
    public struct ParkingLotDatenstand : IComponentData, IQueryTypeParameter,
                                         ISerializable
    {
        public const int CurrentVersion = 1;

        public int Stand;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(Stand);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out Stand);
        }
    }
}
