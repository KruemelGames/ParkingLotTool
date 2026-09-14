using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game.Common;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * WEM GEHOERT DIESE LEITUNG?
     *
     * Die automatisch gebauten Versorgungsleitungen tragen bewusst KEINEN
     * `Owner`: Entities mit Besitzer fallen aus der Unterhaltsrechnung, und
     * eine Leitung, die der Mod legt, soll die Stadt genauso kosten wie eine
     * von Hand gezogene.
     *
     * Ohne Besitzer weiss aber auch niemand mehr, dass sie zu diesem Parkplatz
     * gehoert. Der Mod merkt sich das bisher nur in einer Liste im Arbeits-
     * speicher (`_avGebaut`) - und die ueberlebt kein Speichern und Laden.
     * Wird der Parkplatz spaeter abgerissen, bleiben Kabel und Rohr stehen.
     *
     * Diese Komponente ist die Zuordnung, die das Speichern ueberlebt. Sie ist
     * bewusst NICHT `ParkingLotPartRelation`: die tragen Aufkleber,
     * Ladesaeulen und der Wirtschaftsbegleiter, und gleich fuenf Systeme
     * laufen ueber sie (Komfort, Wirtschaft, Bearbeiten, Terrain, Aufraeumer).
     * Eine Leitung ist kein Parkplatzteil in diesem Sinn - sie liegt
     * ausserhalb, gehoert keiner Bucht und darf in keiner dieser Rechnungen
     * auftauchen. Eine eigene Komponente kostet ein paar Zeilen und erspart
     * fuenf Ueberraschungen.
     */
    public struct ParkingLotVersorgungsleitung : IComponentData,
                                                 IQueryTypeParameter,
                                                 ISerializable
    {
        /** Der Parkplatz, fuer den diese Leitung gebaut wurde. */
        public Entity Lot;

        /** Sein technischer Traeger - fuer die Zuordnung beim Umbau. */
        public Entity Carrier;

        /** Wahr fuer Strom, falsch fuer Wasser/Abwasser. Nur fuer Meldungen. */
        public bool Strom;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(Lot);
            writer.Write(Carrier);
            writer.Write(Strom);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Lot);
            reader.Read(out Carrier);
            reader.Read(out Strom);
        }
    }

    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Welches Lot gehoert zum gemerkten Traeger?
         *
         * `_lotOwner` ist zu diesem Zeitpunkt laengst zurueckgesetzt - der
         * Leitungsbau laeuft Frames nach dem Parkplatzbau. Der Traeger steht
         * aber noch, und `ParkingLotCarrierReference` am Lot ist der
         * dokumentierte Rueckweg. Gesucht wird also das Lot, das auf genau
         * diesen Traeger zeigt.
         */
        private Entity AvZugehoerigesLot()
        {
            if (_avTraeger == Entity.Null || !EntityManager.Exists(_avTraeger))
                return Entity.Null;
            var query = GetEntityQuery(
                ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                ComponentType.Exclude<Game.Tools.Temp>());
            using var lots = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (var i = 0; i < lots.Length; i++)
                if (EntityManager.GetComponentData<ParkingLotCarrierReference>(
                        lots[i]).Carrier == _avTraeger) return lots[i];
            return Entity.Null;
        }

        /**
         * Schreibt die Zuordnung an alle dauerhaft gewordenen Leitungskanten.
         *
         * Aufgerufen wird das erst, wenn `AvMesseApply` die Dauerhaftigkeit
         * bestaetigt hat - eine Temp-Kante zu markieren waere sinnlos, denn
         * sie kann noch verworfen werden.
         *
         * Markiert wird die KANTE, nicht ihre Knoten. Ein Knoten kann zu
         * mehreren Kanten gehoeren, darunter fremden; ihn als "unsere Leitung"
         * zu markieren waere eine Behauptung, die wir nicht halten koennen.
         * Was beim Loeschen mit den Knoten geschieht, entscheidet der
         * Aufraeumer anhand dessen, was dann tatsaechlich noch an ihnen haengt.
         */
        private void MarkiereVersorgungsleitungen(Entity lot, Entity carrier)
        {
            if (lot == Entity.Null && carrier == Entity.Null) return;
            var markiert = 0;
            var schonDa = 0;
            foreach (var kurs in _avGebaut)
            {
                var iststrom = kurs.Name != null && kurs.Name.EndsWith("Strom");
                /*
                 * Die senkrechten Anschlussstuecke gehoeren dazu. Ohne sie
                 * bleibt beim Abriss ein Rohrstummel nach oben stehen - der
                 * Befund des Nutzers vom 2026-09-14. Warum sie getrennt
                 * gefuehrt werden, steht bei `AvKurs.Anschlussstuecke`.
                 */
                var alleKanten = new List<Entity>(kurs.Kanten);
                alleKanten.AddRange(kurs.Anschlussstuecke);
                foreach (var kante in alleKanten)
                {
                    if (kante == Entity.Null || !EntityManager.Exists(kante))
                        continue;
                    if (EntityManager.HasComponent<Deleted>(kante)) continue;
                    if (EntityManager.HasComponent<Game.Tools.Temp>(kante))
                        continue;
                    if (!EntityManager.HasComponent<Game.Net.Edge>(kante))
                        continue;

                    var zuordnung = new ParkingLotVersorgungsleitung
                    {
                        Lot = lot,
                        Carrier = carrier,
                        Strom = iststrom,
                    };
                    if (EntityManager.HasComponent<
                            ParkingLotVersorgungsleitung>(kante))
                    {
                        EntityManager.SetComponentData(kante, zuordnung);
                        schonDa++;
                        continue;
                    }
                    EntityManager.AddComponentData(kante, zuordnung);
                    markiert++;
                }
            }

            if (markiert == 0 && schonDa == 0) return;
            Mod.log.Info($"PLT-Autoversorgung ZUORDNUNG: {markiert} neue und "
                + $"{schonDa} aufgefrischte Leitungskante(n) dem Parkplatz "
                + $"{lot.Index} (Traeger {carrier.Index}) zugeordnet. Diese "
                + "Zuordnung ueberlebt Speichern und Laden; ohne sie blieben "
                + "die Leitungen beim Abriss stehen.");
        }
    }
}
