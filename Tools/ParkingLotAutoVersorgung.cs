using System.Collections.Generic;
using Colossal.Mathematics;
using ParkingLotTool.Geometry;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    // Trassenwahl je zusammenhaengendem eigenen Strassennetz.
    // Materialisierung, Vanilla-Apply und Graph-/Flussnachweis stehen in den
    // weiteren AutoVersorgung-Teildateien; Naehe allein gilt nicht als Anschluss.
    internal struct Versorgungstrasse
    {
        /** Wo an unserem Parkplatz die Leitung beginnt. */
        internal float3 Start;

        /** Die fremde Strassenkante, an der sie enden soll. */
        internal Entity Zielkante;
        internal List<Entity> Startnetz;
        internal Entity Startknoten;
        internal List<Entity> Startkanten;
        internal List<float2> Stromweg, Wasserweg;

        /** Der Punkt auf dieser Kante. */
        internal float3 Ziel;

        /** Wie der Startpunkt zustande kam - fuer den Bauzettel. */
        internal string Herkunft;

        /** Laenge der geplanten Trasse in Metern. */
        internal float Laenge;

        internal bool Gefunden => Zielkante != Entity.Null;
    }

    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Wie weit um den Parkplatz herum nach einer Stadtstrasse gesucht
         * wird. Derselbe Wert wie bei der Zufahrtssuche - was fuer eine
         * Zufahrt zu weit weg ist, ist es fuer eine Leitung auch.
         */
        private const float AutoVersorgungSuchradius = 128f;

        /**
         * Sucht Start und Ziel der automatischen Versorgungsleitung.
         *
         * Liefert eine Trasse mit `Gefunden == false`, wenn es keine
         * brauchbare Gegenstelle gibt. Das ist kein Fehler - der Parkplatz
         * bleibt gebaut, es gibt nur keinen Anschluss.
         */
        private List<Versorgungstrasse> WaehleVersorgungstrassen(
            Entity traeger)
        {
            var trassen = new List<Versorgungstrasse>();

            var unsere = SammleUnsereKanten(traeger);
            if (unsere.Count == 0)
            {
                Mod.log.Warn("PLT-Autoversorgung: der Traeger hat keine "
                    + "eigenen Strassenkanten - kein Startpunkt bestimmbar.");
                return trassen;
            }

            var fremde = SammleZielstrassen(unsere);

            /*
             * JEDE GRUPPE BRAUCHT IHRE EIGENE LEITUNG.
             *
             * Ansage des Nutzers am 2026-09-05: *"wichtig ist auch dass
             * Strassen bzw Gebiete die einzeln stehen bzw nicht mit anderen
             * verbunden sind auch eine Verbindung bekommen."* Astra hatte
             * denselben Fall auf seiner Liste: ein Anschluss ist kein Nachweis
             * fuer alle Gruppen.
             *
             * Zwei Strassenstuecke gehoeren zur selben Gruppe, wenn sie sich
             * einen KNOTEN teilen. Genau entlang dieser Verbindungen leitet
             * CS2 Strom und Wasser weiter - geometrische Naehe allein
             * verbindet nichts.
             */
            var versorgbare = unsere.FindAll(KanteNimmtVersorgung);
            var gruppen = SammleVersorgungsgruppen(versorgbare);
            Mod.log.Info($"PLT-Autoversorgung: {unsere.Count - versorgbare.Count} Fahrgassen-/Pfadkanten ohne Versorgungsbedarf; "
                + $"{versorgbare.Count} versorgungsfaehige Kanten. Pfade verbinden keine Versorgungsgruppen.");
            Mod.log.Info($"PLT-Autoversorgung: {unsere.Count} eigene Kante(n) "
                + $"bilden {gruppen.Count} getrennte(s) Netz(e); Anschlussbedarf wird je Netz geprueft.");

            _avNochOffeneNetze = 0;
            for (var g = 0; g < gruppen.Count; g++)
            {
                if (gruppen[g].Exists(e => _avVersucht.Contains(e))) continue;
                if (gruppen[g].TrueForAll(AvZielHatStadtpfad)) continue;
                _avNochOffeneNetze++;
                var trasse = WaehleTrasseFuerGruppe(gruppen[g], _avAlleEigenen, fremde,
                    $"Netz {g + 1}/{gruppen.Count}");
                if (trasse.Gefunden)
                {
                    trasse.Startnetz = gruppen[g];
                    trassen.Add(trasse);
                }
            }
            trassen.Sort((a, b) => a.Laenge.CompareTo(b.Laenge));
            if (trassen.Count > 1) trassen.RemoveRange(1, trassen.Count - 1);
            if (trassen.Count == 1)
            {
                _avNochOffeneNetze--;
                foreach (var e in trassen[0].Startnetz) _avVersucht.Add(e);
                var t = trassen[0];
                Mod.log.Info($"PLT-Autoversorgung AUSWAHL: {t.Herkunft}, {t.Laenge:F3} m, "
                    + $"Start ({t.Start.x:F2}/{t.Start.z:F2}), Ziel {t.Zielkante} ({t.Ziel.x:F2}/{t.Ziel.z:F2}), "
                    + $"{(t.Startknoten == Entity.Null ? 1 : 0)} Kantenstart; {_avNochOffeneNetze} weitere offene Netze.");
            }
            else AvMesseNetzabdeckung();
            return trassen;
        }

        /**
         * Sucht Start und Ziel fuer EIN zusammenhaengendes Strassennetz.
         *
         * Hindernis ist dabei immer das GESAMTE eigene Netz, nicht nur die
         * eigene Gruppe: eine Leitung darf auch nicht unter den Strassen einer
         * anderen Gruppe entlanglaufen.
         */
        private Versorgungstrasse WaehleTrasseFuerGruppe(
            List<Entity> gruppe,
            List<Entity> unsere,
            List<(Entity Kante, Bezier4x3 Bogen)> fremde,
            string name)
        {
            var ergebnis = new Versorgungstrasse { Zielkante = Entity.Null };
            var versorgbar = 0;
            foreach (var e in gruppe) if (KanteNimmtVersorgung(e)) versorgbar++;
            if (versorgbar == 0)
            {
                Mod.log.Info($"PLT-Autoversorgung {name}: {gruppe.Count} Kante(n), "
                    + "0 versorgungsfaehige Strassen; Fahrgassen benoetigen keinen automatischen Anschluss.");
                return ergebnis;
            }
            if (fremde.Count == 0)
            {
                Mod.log.Warn($"PLT-Autoversorgung {name}: {versorgbar} versorgungsfaehige Strassen, "
                    + $"aber 0 Zielstrassen innerhalb {AutoVersorgungSuchradius:F0} m. Anschluss fehlt.");
                return ergebnis;
            }

            var ziele = fremde.FindAll(z => !gruppe.Contains(z.Kante) && AvZielHatStadtpfad(z.Kante));
            var starts = SammleGruppenpunkte(gruppe, ziele);
            VersucheKandidaten(starts, ziele, gruppe, name + ": Kante/Knoten", ref ergebnis);
            AvUmweg(starts, gruppe, unsere, ziele, name, ref ergebnis);
            if (!ergebnis.Gefunden)
                Mod.log.Warn($"PLT-Autoversorgung {name}: {starts.Count} Starts, {ziele.Count} Ziele mit Stadtpfad, 0 zulaessige Trassen.");
            return ergebnis;
        }

        /**
         * Zerlegt unsere Kanten in zusammenhaengende Netze.
         *
         * Verbunden heisst: gemeinsamer Knoten. Das ist dieselbe Bedingung,
         * nach der CS2 Strom und Wasser weiterleitet.
         */
        private List<List<Entity>> SammleVersorgungsgruppen(
            List<Entity> unsere)
        {
            var gruppen = new List<List<Entity>>();
            var offen = new HashSet<Entity>(unsere);
            var knotenZuKanten = new Dictionary<Entity, List<Entity>>();
            for (var i = 0; i < unsere.Count; i++)
            {
                var kd = EntityManager
                    .GetComponentData<Game.Net.Edge>(unsere[i]);
                foreach (var knoten in new[] { kd.m_Start, kd.m_End })
                {
                    if (knoten == Entity.Null) continue;
                    if (!knotenZuKanten.TryGetValue(knoten, out var liste))
                    {
                        liste = new List<Entity>();
                        knotenZuKanten[knoten] = liste;
                    }
                    liste.Add(unsere[i]);
                }
            }

            while (offen.Count > 0)
            {
                var start = Entity.Null;
                foreach (var e in offen) { start = e; break; }
                offen.Remove(start);
                var gruppe = new List<Entity> { start };
                // Bewusst eine Liste als Stapel: `Stack<T>` gibt es in
                // dieser Umgebung in zwei Assemblies, und der Verweis waere
                // mehrdeutig.
                var stapel = new List<Entity> { start };
                while (stapel.Count > 0)
                {
                    var oben = stapel[stapel.Count - 1];
                    stapel.RemoveAt(stapel.Count - 1);
                    var kd = EntityManager
                        .GetComponentData<Game.Net.Edge>(oben);
                    foreach (var knoten in new[] { kd.m_Start, kd.m_End })
                    {
                        if (knoten == Entity.Null) continue;
                        if (!knotenZuKanten.TryGetValue(knoten, out var liste))
                            continue;
                        for (var i = 0; i < liste.Count; i++)
                        {
                            if (!offen.Remove(liste[i])) continue;
                            gruppe.Add(liste[i]);
                            stapel.Add(liste[i]);
                        }
                    }
                }
                gruppen.Add(gruppe);
            }
            return gruppen;
        }

        /**
         * Vergleicht alle Start-/Zielkombinationen ohne Vorrang fuer Sackgassen.
         *
         * Sortiert wird nach Laenge: der kuerzeste zulaessige Weg gewinnt.
         * "Zulaessig" ist die harte Bedingung des Nutzers, nicht ein Wunsch -
         * deshalb wird ein kuerzerer, aber kreuzender Weg verworfen und nicht
         * etwa bevorzugt.
         */
        private bool VersucheKandidaten(
            List<float3> kandidaten,
            List<(Entity Kante, Bezier4x3 Bogen)> fremde,
            List<Entity> gruppe,
            string herkunft,
            ref Versorgungstrasse ergebnis)
        {
            var r = Versorgungsnetz.Gerade(kandidaten.ConvertAll(p => p.xz), p => AvZielpunkte(p, fremde),
                (i, z) => AvWege(new List<float2> { kandidaten[i].xz, z.Punkt },
                    AvStartstrassen(kandidaten[i], gruppe), fremde[z.Index].Kante, out _, out _));
            if (r.Punkte == null || (ergebnis.Gefunden && !Versorgungsnetz.Kuerzer(r.Laenge, ergebnis.Laenge))) return false;
            var start = kandidaten[r.Start]; var ziel = fremde[r.Ziel];
            MathUtils.Distance(ziel.Bogen.xz, r.Punkte[1], out var t);
            var startstrassen = AvStartstrassen(start, gruppe);
            AvWege(r.Punkte, startstrassen, ziel.Kante, out var strom, out var wasser);
            ergebnis = new Versorgungstrasse {
                Start = start, Startknoten = AvStartknoten(start, gruppe),
                Startkanten = gruppe.FindAll(e => startstrassen.Contains(e.Index)),
                Zielkante = ziel.Kante, Ziel = MathUtils.Position(ziel.Bogen, t),
                Herkunft = herkunft, Laenge = r.Laenge, Stromweg = strom, Wasserweg = wasser };
            Mod.log.Info($"PLT-Autoversorgung GERADE [{herkunft}]: {kandidaten.Count} Starts, "
                + $"{r.Zielpruefungen} Kombinationen, kuerzeste zulaessige Gerade {r.Laenge:F3} m; "
                + $"Start ({start.x:F2}/{start.z:F2}), Ziel {ziel.Kante}, "
                + $"eigene Zielstrasse {(EntityManager.HasComponent<Owner>(ziel.Kante) ? 1 : 0)}. Graphwege werden noch verglichen.");
            return true;
        }

        /** Alle dauerhaften Strassenkanten unseres Traegers. */
        private List<Entity> SammleUnsereKanten(Entity traeger)
        {
            var kanten = new List<Entity>();
            if (traeger == Entity.Null || !EntityManager.Exists(traeger))
                return kanten;
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(traeger))
                return kanten;
            var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(
                traeger, true);
            for (var i = 0; i < subNets.Length; i++)
            {
                var kante = subNets[i].m_SubNet;
                if (kante == Entity.Null || !EntityManager.Exists(kante))
                    continue;
                if (!EntityManager.HasComponent<Game.Net.Edge>(kante)) continue;
                if (EntityManager.HasComponent<Deleted>(kante)) continue;
                if (EntityManager.HasComponent<Game.Tools.Temp>(kante))
                    continue;
                kanten.Add(kante);
            }
            return kanten;
        }

        /**
         * Freie Enden unseres Netzes.
         *
         * Ein Knoten, an dem genau EINE Kante haengt, ist eine Sackgasse.
         * Gezaehlt werden nur unsere eigenen Kanten - eine Sackgasse, die
         * bereits an einer fremden Strasse haengt, ist keine mehr.
         */
        /**
         * NICHT JEDE UNSERER STRASSEN NIMMT EINE LEITUNG AN.
         *
         * GEMESSEN am 2026-09-05, 23:26. Drei Netze, drei verschiedene
         * Ergebnisse - und die neue Torzeile nennt den Grund beim Namen:
         *
         *   STARTTORE [Netz 1/3]: Kante ..., Layer hin/zurueck 0/0,
         *     Randabstand -2,000 m / Suchradius 0,750 m, Hoehe 10,000 m
         *   STARTTORE [Netz 2/3]: Kante ..., Layer hin/zurueck 1/1, ...
         *   STARTTORE [Netz 3/3]: Kante ..., Layer hin/zurueck 1/1, ...
         *
         * Abstand und Hoehe stimmten ueberall. Bei Netz 1 scheiterte allein
         * die EBENENPRUEFUNG, und zwar in beide Richtungen. Unser Parkplatz
         * besteht nicht nur aus der Zoningstrasse: die Fahrgassen und
         * Querwege sind unsichtbare Pfade, und die fuehren weder Strom noch
         * Wasser. Ein Anschlusspunkt an so einem Weg kann gar nicht klappen.
         *
         * Deshalb kommen nur Punkte an Kanten in Frage, die BEIDE Leitungsarten annehmen. Was keinen Strom
         * fuehrt, ist kein Anschlusspunkt - egal wie guenstig es liegt.
         */
        private bool KanteNimmtVersorgung(Entity kante)
        {
            if (kante == Entity.Null || !EntityManager.Exists(kante))
                return false;
            if (!EntityManager.HasComponent<PrefabRef>(kante)) return false;
            var prefab = EntityManager
                .GetComponentData<PrefabRef>(kante).m_Prefab;
            if (!EntityManager.HasComponent<NetData>(prefab)) return false;
            var ebenen = EntityManager
                .GetComponentData<NetData>(prefab).m_LocalConnectLayers;
            return (ebenen & Layer.PowerlineLow) != 0
                && (ebenen & Layer.WaterPipe) != 0
                && (ebenen & Layer.SewagePipe) != 0;
        }

        /** Liegt am Knoten eine Kante dieser Gruppe, die Versorgung annimmt? */
        private bool KnotenNimmtVersorgung(Entity knoten, HashSet<Entity> gruppe)
        {
            if (!EntityManager.HasBuffer<ConnectedEdge>(knoten)) return false;
            var angehaengt = EntityManager
                .GetBuffer<ConnectedEdge>(knoten, true);
            for (var i = 0; i < angehaengt.Length; i++)
                if (gruppe.Contains(angehaengt[i].m_Edge)
                    && KanteNimmtVersorgung(angehaengt[i].m_Edge)) return true;
            return false;
        }

        // Der Lauf 20:35 zeigt Start/Ziel 0/1 auch an inneren Knoten.
        // Die Auswahl bewahrt ihre Entity; die Vorschauanmeldung ist separat.
        private List<float3> SammleGruppenpunkte(
            List<Entity> gruppe,
            List<(Entity Kante, Bezier4x3 Bogen)> fremde)
        {
            var knoten = AvKantenpunkte(gruppe, fremde);
            var mitglieder = new HashSet<Entity>(gruppe);
            var gesehen = new HashSet<Entity>();
            for (var i = 0; i < gruppe.Count; i++)
            {
                var kd = EntityManager
                    .GetComponentData<Game.Net.Edge>(gruppe[i]);
                foreach (var n in new[] { kd.m_Start, kd.m_End })
                {
                    if (n == Entity.Null || !EntityManager.Exists(n)) continue;
                    if (!gesehen.Add(n)) continue;
                    // Nur Knoten an versorgungsfaehigen Kanten -
                    // siehe `KanteNimmtVersorgung`.
                    if (!KnotenNimmtVersorgung(n, mitglieder)) continue;
                    if (!EntityManager.HasComponent<Game.Net.Node>(n)) continue;
                    knoten.Add(EntityManager
                        .GetComponentData<Game.Net.Node>(n).m_Position);
                }
            }
            return knoten;
        }

        /**
         * Fremde Strassen im Umkreis - die moeglichen Gegenstellen.
         *
         * Eigene Kanten bleiben Kandidaten. Gruppenidentitaet und Stadtpfad
         * werden anschliessend je Startgruppe geprueft. Vorschau und geloeschte
         * Kanten bleiben ausgeschlossen. Eine Leitung
         * an einen Fussweg zu haengen bringt keinen Strom.
         */
        private List<(Entity Kante, Bezier4x3 Bogen)> SammleZielstrassen(
            List<Entity> unsere)
        {
            var treffer = new List<(Entity, Bezier4x3)>();
            if (_netSearchSystem == null) return treffer;

            /*
             * DAS SUCHFELD KOMMT AUS DEN EIGENEN STRASSEN, NICHT AUS DEM
             * UMRISS.
             *
             * Der Umriss ist zu diesem Zeitpunkt schon zurueckgesetzt (siehe
             * `_avUmriss`), und selbst wenn nicht: gesucht wird eine Strasse
             * in der Naehe unserer STRASSEN. Die stehen hier ohnehin fertig
             * da und sind die verlaesslichere Quelle.
             */
            var min = new float2(float.MaxValue, float.MaxValue);
            var max = new float2(float.MinValue, float.MinValue);
            for (var i = 0; i < unsere.Count; i++)
            {
                if (!EntityManager.HasComponent<Game.Net.Curve>(unsere[i]))
                    continue;
                var bogen = EntityManager
                    .GetComponentData<Game.Net.Curve>(unsere[i]).m_Bezier;
                for (var s = 0; s <= 4; s++)
                {
                    var punkt = MathUtils.Position(bogen, s / 4f).xz;
                    min = math.min(min, punkt);
                    max = math.max(max, punkt);
                }
            }
            if (min.x > max.x) return treffer;
            var suchfeld = new Bounds2(
                min - AutoVersorgungSuchradius,
                max + AutoVersorgungSuchradius);

            var baum = _netSearchSystem.GetNetSearchTree(
                readOnly: true, out var deps);
            deps.Complete();
            using var gefunden = new NativeList<Entity>(64, Allocator.Temp);
            var iterator = new EntityIterator
            {
                Bounds = suchfeld,
                Results = gefunden,
            };
            baum.Iterate(ref iterator);

            /*
             * ZAEHLER AN JEDER ABWEISUNG.
             *
             * Bleibt die Suche leer, muss die Meldung sagen WORAN es lag -
             * sonst steht da nur "keine Strasse gefunden" und die Ursache ist
             * wieder Ratesache. Genau das ist am 2026-09-05 um 19:28 passiert.
             */
            var gesehen = new HashSet<Entity>();
            var raus_geloescht = 0;
            var raus_temp = 0;
            var raus_keineKante = 0;
            var raus_layer = 0;
            var raus_keineStrasse = 0;
            for (var i = 0; i < gefunden.Length; i++)
            {
                var kante = gefunden[i];
                if (kante == Entity.Null || !gesehen.Add(kante)) continue;
                if (!EntityManager.Exists(kante)) continue;

                if (EntityManager.HasComponent<Deleted>(kante))
                { raus_geloescht++; continue; }
                if (EntityManager.HasComponent<Game.Tools.Temp>(kante))
                { raus_temp++; continue; }
                if (!EntityManager.HasComponent<Game.Net.Edge>(kante)
                    || !EntityManager.HasComponent<Game.Net.Curve>(kante)
                    || !EntityManager.HasComponent<PrefabRef>(kante))
                { raus_keineKante++; continue; }
                if (!KanteNimmtVersorgung(kante))
                { raus_layer++; continue; }
                var prefab = EntityManager
                    .GetComponentData<PrefabRef>(kante).m_Prefab;
                if (!EntityManager.HasComponent<RoadData>(prefab))
                { raus_keineStrasse++; continue; }
                treffer.Add((kante, EntityManager
                    .GetComponentData<Game.Net.Curve>(kante).m_Bezier));
            }
            Mod.log.Info($"PLT-Autoversorgung Strassensuche: Feld "
                + $"({min.x:F0}/{min.y:F0}) bis ({max.x:F0}/{max.y:F0}), "
                + $"{gefunden.Length} Treffer im Suchbaum, {treffer.Count} "
                + $"brauchbar. Verworfen: {raus_geloescht} geloescht, {raus_temp} Vorschau, "
                + $"{raus_keineKante} ohne Kante/Kurve, {raus_layer} ohne "
                + $"Versorgungslayer, {raus_keineStrasse} keine Strasse.");
            return treffer;
        }

        private const float AutoVersorgungAnschlussbereich = 8f;
        private const float AutoVersorgungSicherheitszugabe = 0.5f;
    }
}
