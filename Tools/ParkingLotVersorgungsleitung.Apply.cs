using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private static IEnumerable<Entity> Beide(AvKurs kurs)
        {
            foreach (var e in kurs.Kanten) yield return e;
            foreach (var e in kurs.Anschlussstuecke) yield return e;
        }

        /*
         * ZUORDNUNG VOR APPLY AUS DER KURSZUORDNUNG (17.10.).
         *
         * Der Seed taugt fuer Leitungen nicht als Herkunft: teilt CS2 eine
         * frische Kante im selben Bild (Anschlussknoten), bekommen die
         * Teilstuecke abgeleitete Seeds (GenerateEdgesSystem 1075-1081,
         * kSplitEdge). Im Spiel brach daran die ganze Nebenarbeit ab.
         * Die Kurszuordnung kennt die Herkunft schon: nur Temp-Kanten
         * unseres Prefabs ohne Original (also von diesem Werkzeuglauf
         * erzeugt) bzw. im Hintergrund dauerhafte Kanten am eigenen Traeger.
         * Genau die bekommen hier ihre Marke - vor dem Apply, das die Entity
         * behaelt (ApplyNetSystem 691-704).
         */
        private void MarkiereVersorgungVorApply(Entity lot, Entity carrier)
        {
            int markiert = 0, fremd = 0;
            foreach (var kurs in _avKurse)
            {
                var strom = kurs.Name != null && kurs.Name.EndsWith("Strom");
                foreach (var e in Beide(kurs))
                {
                    if (!EigeneNeueLeitung(e, carrier)) { fremd++; continue; }
                    var marke = new ParkingLotVersorgungsleitung { Lot = lot, Carrier = carrier, Strom = strom };
                    if (EntityManager.HasComponent<ParkingLotVersorgungsleitung>(e)) EntityManager.SetComponentData(e, marke);
                    else EntityManager.AddComponentData(e, marke);
                    markiert++;
                }
            }
            if (fremd > 0)
                ParkingLotNebenarbeitDaten.Diagnose(() => Mod.log.Warn($"PLT-Autoversorgung: {fremd} kurszugeordnete Kante(n) ohne eigene Herkunft nicht markiert; {markiert} markiert."));
        }

        private bool EigeneNeueLeitung(Entity e, Entity carrier)
        {
            if (e == Entity.Null || !EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)) return false;
            if (EntityManager.HasComponent<Temp>(e))
            {
                var t = EntityManager.GetComponentData<Temp>(e);
                return t.m_Original == Entity.Null && (t.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) == 0;
            }
            return _bauarbeiter && EntityManager.HasComponent<Owner>(e)
                && EntityManager.GetComponentData<Owner>(e).m_Owner == carrier;
        }
    }
}
