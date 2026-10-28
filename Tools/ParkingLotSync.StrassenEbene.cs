using System.Collections.Generic;
using System.Linq;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * WER BRAUCHT SCHRITT 16 (Issue #22, 2026-10-28)?
     *
     * Ein heute gebauter Parkplatz hat hinter jeder Autozufahrt und jeder
     * Alley eine Flaeche mit Strassen-Ebene: die Fahrbahn mit eingesetzter
     * Vorflaeche bzw. den Alley-Belag. Ein alter hat dort nur den Asphalt,
     * der seit dem Bruecken-Fix nicht mehr auf Strassen zeichnet - Alley
     * offen, Vorflaeche endet vor dem Gehweg, Halbkreis auf der Randstrasse.
     *
     * Geprueft wird ohne Layoutrechnung: je Zufahrt laut Bauzettel ein Punkt
     * 2 m hinter dem Umriss; liegt dort keine eigene Flaeche mit
     * Strassen-Ebene, fehlt die neue Ebene. Dann tauscht Schritt 16 die
     * Flaechen gegen die, die der Bau heute plant (ParkingLotFlaechenSync.
     * Neuaufbau.cs) - nur Flaechen, Wege bleiben.
     */
    public sealed partial class ParkingLotSyncSystem
    {
        /** So weit hinter dem Umriss wird nach der Flaeche mit Strassen-Ebene gesucht. */
        private const float StrassenEbeneProbe = 2f;

        private readonly HashSet<string> _strassenEbeneBefunde = new();

        /*
         * Schritt 16: die Flaechen sind von vor der Fahrbahn-Ebene. Er tauscht
         * nur Flaechen aus dem Bauzettel und ruehrt keinen Weg an. Eine erste
         * Wegpruefung suchte die Wege ueber den Besitzer (SubNet-Puffer,
         * Teilnetz-Zuordnung) und fand die unsichtbaren Zufahrtswege nicht; der
         * darauf gesetzte Hintergrund-Neubau (Schritt 17) beschaedigte einen
         * Parkplatz (Alleys mehrfach, Loecher) - beides raus (28.10.). Geprueft
         * wird jetzt nach der Art des Wegs (PruefeEinmuendungen).
         */
        private bool BrauchtStrassenEbene(Entity lot)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || !EntityManager.HasBuffer<Game.Areas.SubArea>(lot)) return false;
            if (!ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out var k, out _, melden: false)) return false;
            if (!k.Zettel.SurfaceRoadOn) return false;
            var umriss = k.Punkte.Select(p => p.xz).ToArray();
            var muendungen = Einmuendungen(umriss, ParkingLotToolSystem.LayoutEinstellungen(k).Entrances);
            if (muendungen.Count == 0) return false;
            var offen = ZufahrtenOhneStrassenEbene(lot, umriss, muendungen);
            /*
             * BAUZETTEL GEGEN DIE WELT (28.10.). Schritt 16 plant die Flaechen
             * aus dem Bauzettel. Passt der nicht mehr zu dem, was in der Stadt
             * steht, schriebe der Sync Flaechen fest, die zu keinem Weg gehoeren.
             * Gemessen an "Parking Lot (192 spaces)": Bauzettel zwei Gassen,
             * im Spielstand fehlte das ganze Wegenetz (Bauprotokoll 44 Wege,
             * Netz-Suchbaum 0, nur die Zoningstrassen da). Also nur, wenn an
             * jeder Einmuendung der Weg liegt, den der Bauzettel dort sagt:
             * Alley-Weg bei Gasse, unsichtbarer Zufahrtsweg sonst. Nach der ART
             * des Wegs, nicht nach dem Besitzer - die Besitzerzuordnung fand die
             * unsichtbaren Zufahrtswege nicht. Sonst bleibt der Parkplatz, wie
             * er ist, und das Log sagt warum; reparieren kann ihn das Edit.
             */
            PruefeEinmuendungen(muendungen, out var abweichend, out var fehlend);
            var passt = abweichend == 0 && fehlend == 0;
            var text = $"PLT-Sync: Lot {lot.Index} Fahrbahn-Ebene: {muendungen.Count} Zufahrt(en), "
                + $"{offen} ohne Flaeche mit Strassen-Ebene, {abweichend} mit anderem Weg als im Bauzettel, "
                + $"{fehlend} ohne Weg -> " + (offen == 0 ? "nichts zu tun"
                    : passt ? "Flaechen neu (Schritt 16)" : "NICHT angefasst");
            if (_strassenEbeneBefunde.Add(text)) Mod.log.Info(text);
            if (offen > 0 && !passt)
            {
                /*
                 * Nicht "nichts zu tun": dann setzte die Aufnahme den Stand,
                 * und der Parkplatz kaeme nie wieder dran. Gescheitert heisst
                 * bis zum naechsten Laden kein Versuch, danach wird neu geprueft.
                 */
                var schritt = System.Array.FindIndex(Migrationskatalog.Schritte, x => x.Name == "FlaechenNeuAufbauen");
                Scheitern(lot, schritt, "FlaechenNeuAufbauen", $"Bauzettel passt nicht zum Bestand ({abweichend} "
                    + $"Zufahrt(en) mit anderem Weg, {fehlend} ohne Weg) - Flaechen unangetastet, Parkplatz im Edit pruefen");
                return true;
            }
            return offen > 0;
        }

        /** Wie viele Einmuendungen 2 m dahinter keine eigene Flaeche mit Strassen-Ebene haben. */
        private int ZufahrtenOhneStrassenEbene(Entity lot, float2[] umriss, List<(float2 Punkt, float2 Kante, Zufahrtsart Art)> muendungen)
        {
            var klone = World.GetOrCreateSystemManaged<ParkingLotApronPrefabSystem>();
            var strasse = new List<float2[]>();
            var unter = EntityManager.GetBuffer<Game.Areas.SubArea>(lot, true);
            for (var i = 0; i < unter.Length; i++)
            {
                var f = unter[i].m_Area;
                if (!EntityManager.Exists(f) || EntityManager.HasComponent<Deleted>(f)
                    || !EntityManager.HasComponent<Game.Areas.Surface>(f)
                    || !EntityManager.HasComponent<PrefabRef>(f) || !EntityManager.HasBuffer<Game.Areas.Node>(f)) continue;
                if (!klone.IstStrassenklon(EntityManager.GetComponentData<PrefabRef>(f).m_Prefab)) continue;
                var knoten = EntityManager.GetBuffer<Game.Areas.Node>(f, true);
                var ring = new float2[knoten.Length];
                for (var j = 0; j < knoten.Length; j++) ring[j] = knoten[j].m_Position.xz;
                strasse.Add(ring);
            }
            var offen = 0;
            foreach (var m in muendungen)
            {
                var n = new float2(-m.Kante.y, m.Kante.x);
                var p = InRing(m.Punkt + n * StrassenEbeneProbe, umriss) ? m.Punkt + n * StrassenEbeneProbe
                    : m.Punkt - n * StrassenEbeneProbe;
                if (!InRing(p, umriss)) continue;
                if (!strasse.Any(r => r.Length >= 3 && InRing(p, r))) offen++;
            }
            return offen;
        }

        /** So nah muss der Weg einer Zufahrt an ihrer Einmuendung laut Bauzettel liegen. */
        private const float EinmuendungAbstand = 1.5f;

        /**
         * Je Einmuendung: liegt dort der Weg, den der Bauzettel sagt? Alley-Weg
         * (PLT Zufahrtsgasse, alt: Vanilla "Alley") bei Gasse, unsichtbarer
         * Zufahrtsweg ("Invisible Road Path") bei Zufahrt/Einfahrt/Ausfahrt.
         * Gegengeprueft an den Berichten vom 28.10.: wo die Wege stehen, liegen
         * sie 0,00-0,05 m neben dem Punkt aus Kante und Along.
         */
        private void PruefeEinmuendungen(List<(float2 Punkt, float2 Kante, Zufahrtsart Art)> muendungen,
            out int abweichend, out int fehlend)
        {
            abweichend = fehlend = 0;
            var prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            var baum = World.GetOrCreateSystemManaged<Game.Net.SearchSystem>().GetNetSearchTree(true, out var deps);
            deps.Complete();
            using var treffer = new NativeList<Entity>(16, Allocator.Temp);
            foreach (var m in muendungen)
            {
                treffer.Clear();
                var suche = new NetzImKasten
                {
                    Kasten = new Bounds2(m.Punkt - EinmuendungAbstand, m.Punkt + EinmuendungAbstand),
                    Treffer = treffer,
                };
                baum.Iterate(ref suche);
                bool alley = false, auto = false;
                foreach (var e in treffer)
                {
                    if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)
                        || EntityManager.HasComponent<Game.Tools.Temp>(e)
                        || !EntityManager.HasComponent<Game.Net.Edge>(e) || !EntityManager.HasComponent<Game.Net.Curve>(e)
                        || !EntityManager.HasComponent<PrefabRef>(e)) continue;
                    if (Abstand(EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier, m.Punkt) >= EinmuendungAbstand) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!prefabs.TryGetPrefab<PrefabBase>(prefab, out var p) || p == null) continue;
                    if (GassenPrefab.Ist(prefabs, prefab) || p.name == "Alley") alley = true;
                    else if (p.name.Contains("Invisible Road Path")) auto = true;
                }
                var sollAlley = Zufahrtsarten.IstGasse(m.Art);
                if (!alley && !auto) fehlend++;
                else if (sollAlley ? !alley : !auto) abweichend++;
            }
        }

        private struct NetzImKasten : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Kasten;
            public NativeList<Entity> Treffer;

            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Kasten);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Kasten)) Treffer.Add(entity);
            }
        }

        private static float Abstand(Bezier4x3 kurve, float2 p)
        {
            var beste = float.MaxValue;
            for (var i = 0; i <= 16; i++)
                beste = math.min(beste, math.distance(MathUtils.Position(kurve, i / 16f).xz, p));
            return beste;
        }

        /** Wo jede Auto- oder Alley-Zufahrt laut Bauzettel den Umriss kreuzt, mit Kantenrichtung und Art. */
        private static List<(float2 Punkt, float2 Kante, Zufahrtsart Art)> Einmuendungen(float2[] umriss, Entrance[] zufahrten)
        {
            var liste = new List<(float2, float2, Zufahrtsart)>();
            if (zufahrten == null || umriss == null || umriss.Length < 3) return liste;
            foreach (var z in zufahrten)
            {
                if (z == null || z.Art == Zufahrtsart.Fussweg) continue;
                if (z.Edge < 0 || z.Edge >= umriss.Length) continue;
                var a = umriss[z.Edge];
                var b = umriss[(z.Edge + 1) % umriss.Length];
                var d = math.normalizesafe(b - a);
                if (math.lengthsq(d) < 0.5f) continue;
                // Along: Meter ab Kantenanfang (siehe Kurvengruppe).
                liste.Add((a + d * (float)z.Along, d, z.Art));
            }
            return liste;
        }

        private static bool InRing(float2 p, float2[] ring)
        {
            var innen = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                if ((ring[i].y > p.y) != (ring[j].y > p.y)
                    && p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x)
                    innen = !innen;
            return innen;
        }
    }
}
