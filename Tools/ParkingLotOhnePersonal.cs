using Colossal.Serialization.Entities;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * PARKPLATZ OHNE PERSONAL (Issue #23, Wunsch aus dem Forum, 29.10.2026).
     *
     * Die meisten Parkplaetze ausserhalb grosser Staedte sind kostenlos und
     * haben kein Personal. Der Spieler will beides: bewachte Parkplaetze mit
     * Gebuehr und freie ohne Angestellte. Gesetzt wird das je Parkplatz im
     * Infofenster, Standard bleibt "mit Personal".
     *
     * Ein eigenes Kennzeichen, kein Feld in `ParkingLotEconomyData`: deren
     * Instanzen stehen im Spielstand ohne Laengenangabe hintereinander, ein
     * weiteres Feld verschoebe jeden folgenden Datensatz alter Spielstaende.
     * Fehlt das Kennzeichen - alter Stand, Standard -, gilt "mit Personal".
     *
     * Wirkung an genau einer Stelle: `ParkingLotEmployeeSystem` setzt den
     * Begleiter auf 0 Stellen, CS2 entlaesst die Angestellten selbst
     * (derselbe Weg wie beim Abbau, wenn die Wirtschaft ausgeschaltet wird).
     *
     * DER UNTERHALT SINKT VON SELBST, UND NUR UM DIE LOEHNE. Die Stadt zahlt
     * fuer ein staedtisches Gebaeude Instandhaltung plus die Loehne seiner
     * Angestellten (`CityServiceBudgetSystem`, Zeile 400-410:
     * `GetUpkeepOfEmployeeWage`). Ohne Angestellte faellt der Lohnanteil weg;
     * die Instandhaltung bleibt - "extern gestellt", wie der Nutzer sagt.
     * Ein zusaetzlicher Abschlag waere doppelt gekuerzt (erster Entwurf vom
     * 29.10.: pauschal die Haelfte, vom Nutzer verworfen).
     */
    public struct ParkingLotOhnePersonal : IComponentData, ISerializable
    {
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(1);
        public void Deserialize<TReader>(TReader reader) where TReader : IReader { reader.Read(out int version); }
    }

    internal static class ParkingLotPersonal
    {
        internal static bool Ohne(EntityManager em, Entity lot)
            => lot != Entity.Null && em.Exists(lot) && em.HasComponent<ParkingLotOhnePersonal>(lot);
    }
}
