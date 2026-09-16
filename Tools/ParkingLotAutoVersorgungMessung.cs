using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private EntityQuery AvTempQuery() => GetEntityQuery(
            ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Temp>(),
            ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Curve>(),
            ComponentType.Exclude<Deleted>());

        private bool AvTempKantenDa()
        {
            using var alle = AvTempQuery().ToEntityArray(Allocator.Temp);
            var voll = 0;
            var passend = 0;
            var unbrauchbar = 0;
            var definitionen = 0;
            var updated = 0;
            var details = new List<string>();
            foreach (var kurs in _avKurse)
            {
                kurs.Kanten.Clear();
                kurs.Anschlussstuecke.Clear();
                var fremdeStuecke = new List<string>();
                foreach (var definition in kurs.Definitionen)
                if (EntityManager.Exists(definition))
                {
                    definitionen++;
                    if (EntityManager.HasComponent<Updated>(definition)) updated++;
                }
                var abschnitte = new List<List<float2>>();
                for (var i = 1; i < kurs.Punkte.Count; i++) abschnitte.Add(new List<float2>());
                var prefabTemp = 0;
                var naechsterAbstand = float.MaxValue;
                var naechste = "keine";
                foreach (var e in alle)
                {
                    if (EntityManager.GetComponentData<PrefabRef>(e).m_Prefab != kurs.Prefab) continue;
                    prefabTemp++;
                    var b = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                    var abstand = math.distance((b.a.xz + b.d.xz) * 0.5f,
                        (kurs.Start.xz + kurs.Ende.xz) * 0.5f);
                    if (abstand < naechsterAbstand)
                    {
                        naechsterAbstand = abstand;
                        naechste = $"{e}, Mittelpunktabstand {abstand:F2} m, "
                            + $"Welt-Y {b.a.y:F2}/{b.d.y:F2}";
                    }
                    var segment = -1;
                    var abschnitt = default(float2);
                    for (var i = 1; i < kurs.Punkte.Count; i++)
                        if (VersorgungskursPruefung.Abschnitt(kurs.Punkte[i - 1].xz, kurs.Punkte[i].xz,
                            b.a.xz, b.b.xz, b.c.xz, b.d.xz, out abschnitt)) { segment = i - 1; break; }
                    if (segment < 0)
                    {
                        /*
                         * SENKRECHT HEISST IN DER DRAUFSICHT: KEINE LAENGE.
                         *
                         * Unsere Leitung liegt auf -10 m, die Rohre der Stadt
                         * liegen hoeher. Fuer den Anschluss baut CS2 ein
                         * kurzes SENKRECHTES Stueck dazwischen. Die Zuordnung
                         * oben rechnet aber mit `Abschnitt` in X und Z - und
                         * dort hat so ein Stueck keine Laenge, Anfang und Ende
                         * liegen uebereinander. Es faellt durch, bekommt nie
                         * die Markierung `ParkingLotVersorgungsleitung` und
                         * bleibt beim Abriss stehen.
                         *
                         * Der Nutzer am 2026-09-14, nachdem der Knoten weg war
                         * und das Stueck nicht: *"Ein Beispiel: ich sage
                         * loesche die Zahl 3.1274 und du kommst mit 3."*
                         *
                         * Dieselbe Fehlerklasse wie schon zweimal in diesem
                         * Projekt: in der Draufsicht gemessen, wo es um die
                         * Hoehe geht.
                         *
                         * Die Schranke ist eng: unser eigenes Prefab, frisch
                         * gebaut, in der Draufsicht unter einem Meter, mit
                         * echtem Hoehenunterschied, und ein Ende nahe an
                         * einem Punkt unseres eigenen Kurses.
                         */
                        var flach = math.distance(b.a.xz, b.d.xz);
                        var hoch = math.abs(b.a.y - b.d.y);
                        var nah = false;
                        for (var i = 0; i < kurs.Punkte.Count; i++)
                        {
                            var pk = kurs.Punkte[i].xz;
                            if (math.distance(b.a.xz, pk) < 2f
                                || math.distance(b.d.xz, pk) < 2f)
                            { nah = true; break; }
                        }
                        var tempStueck = EntityManager.GetComponentData<Temp>(e);
                        var frisch = tempStueck.m_Original == Entity.Null
                            && (tempStueck.m_Flags
                                & (TempFlags.Delete | TempFlags.Cancel)) == 0;
                        if (frisch && nah && flach < 1f && hoch > 0.5f)
                        {
                            kurs.Anschlussstuecke.Add(e);
                            continue;
                        }
                        // Alles andere unseres Prefabs nur MELDEN. Falls
                        // weiter etwas stehen bleibt, steht hier, was es ist.
                        if (frisch && fremdeStuecke.Count < 4)
                            fremdeStuecke.Add($"{e} (waagerecht {flach:F2} m, "
                                + $"senkrecht {hoch:F2} m, nah={(nah ? 1 : 0)})");
                        continue;
                    }
                    passend++;
                    var temp = EntityManager.GetComponentData<Temp>(e);
                    if (temp.m_Original != Entity.Null
                        || (temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0)
                    {
                        unbrauchbar++;
                        continue;
                    }
                    kurs.Kanten.Add(e);
                    abschnitte[segment].Add(abschnitt);
                }
                var fertig = true;
                for (var i = 0; i < abschnitte.Count; i++)
                    fertig &= VersorgungskursPruefung.Vollstaendig(
                        math.distance(kurs.Punkte[i].xz, kurs.Punkte[i + 1].xz), abschnitte[i]);
                if (fertig) voll++;
                if (kurs.Anschlussstuecke.Count > 0 || fremdeStuecke.Count > 0)
                    Mod.log.Info($"PLT-Autoversorgung ANSCHLUSSSTUECKE "
                        + $"[{kurs.Name}]: {kurs.Anschlussstuecke.Count} "
                        + "senkrechte(s) Stueck(e) als unseres uebernommen"
                        + (fremdeStuecke.Count == 0 ? "."
                            : "; nicht zugeordnet: "
                              + string.Join(", ", fremdeStuecke)
                              + ". Bleibt davon etwas stehen, steht hier, "
                              + "was es ist."));
                details.Add($"{kurs.Name}: {kurs.Kanten.Count} Kanten, voll={(fertig ? 1 : 0)}, "
                    + $"Prefab-Temp insgesamt {prefabTemp}, naechste {naechste}");
            }
            var sollDefinitionen = 0;
            foreach (var kurs in _avKurse) sollDefinitionen += kurs.Definitionen.Count;
            var baumTemp = AvTempImSuchbaum();
            var text = $"Definitionen {definitionen}/{sollDefinitionen} (Updated {updated}), "
                + $"ECS Temp+Edge {alle.Length}, kurszugeordnet {passend}, "
                + $"verworfen {unbrauchbar}, vollstaendige Kurse {voll}/{_avKurse.Count}, "
                + $"Suchbaum-Temp {baumTemp}; " + string.Join("; ", details);
            if (text != _avLetzterBefund)
            {
                Mod.log.Info("PLT-Autoversorgung MATERIALISIERUNG: " + text
                    + (passend > 0 && baumTemp == 0
                        ? ". FALL 2: Temp-Kanten vorhanden, Suchbaum findet sie nicht."
                        : passend == 0 ? ". Noch keine kurszugeordnete Temp-Kante in ECS." : "."));
                _avLetzterBefund = text;
            }
            return _avKurse.Count > 0 && voll == _avKurse.Count && unbrauchbar == 0;
        }

        // Kontrollinstrument fuer den alten Fehler: SearchSystem schliesst
        // Temp in allen vier Netzabfragen aus (lokales Dekompilat, Z. 686-731).
        private int AvTempImSuchbaum()
        {
            if (_netSearchSystem == null || _avKurse.Count == 0) return -1;
            var min = new float2(float.MaxValue);
            var max = new float2(float.MinValue);
            foreach (var k in _avKurse)
            {
                min = math.min(min, math.min(k.Start.xz, k.Ende.xz));
                max = math.max(max, math.max(k.Start.xz, k.Ende.xz));
            }
            var baum = _netSearchSystem.GetNetSearchTree(true, out var deps);
            deps.Complete();
            using var treffer = new NativeList<Entity>(64, Allocator.Temp);
            var it = new EntityIterator { Bounds = new Bounds2(min - 8f, max + 8f), Results = treffer };
            baum.Iterate(ref it);
            var gesehen = new HashSet<Entity>();
            var anzahl = 0;
            foreach (var e in treffer)
                if (gesehen.Add(e) && EntityManager.HasComponent<Edge>(e)
                    && EntityManager.HasComponent<Temp>(e)) anzahl++;
            return anzahl;
        }

        /**
         * Der zuletzt geschriebene Messbefund - zum Vergleich, nicht zur
         * Auswertung. Solange er gleich bleibt, gibt es nichts Neues zu sagen.
         */
        private string _avLetzterMessbefund;

        private bool MesseVersorgungsfluss(bool letzte)
        {
            AvErfasseStadtpfade();
            var zeilen = new List<string>();
            var abgenommen = 0;
            foreach (var kurs in _avKurse)
            {
                var dauerhaft = 0;
                var graphkanten = 0;
                long strom = long.MaxValue, frisch = long.MaxValue, abwasser = long.MaxValue;
                var start = Entity.Null;
                var ende = Entity.Null;
                var istStrom = EntityManager.HasComponent<ElectricityConnectionData>(kurs.Prefab);
                foreach (var e in kurs.Kanten)
                {
                    if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)
                        || EntityManager.HasComponent<Temp>(e)) continue;
                    dauerhaft++;
                    var netz = EntityManager.GetComponentData<Edge>(e);
                    foreach (var n in new[] { netz.m_Start, netz.m_End })
                    {
                        if (!EntityManager.HasComponent<Node>(n)) continue;
                        var pos = EntityManager.GetComponentData<Node>(n).m_Position;
                        if (math.distance(pos.xz, kurs.Start.xz) <= VersorgungskursPruefung.Toleranz) start = n;
                        if (math.distance(pos.xz, kurs.Ende.xz) <= VersorgungskursPruefung.Toleranz) ende = n;
                    }
                    // Genau die beiden Haelften dieser gebauten Leitung lesen.
                    // Die alte Summe aller inzidenten Kanten zaehlte Transit doppelt
                    // und konnte zusaetzlich seitlich angeschlossene Verbraucher enthalten.
                    var mitte = AvFlussknoten(e, istStrom);
                    if (mitte == Entity.Null) continue;
                    foreach (var n in new[] { netz.m_Start, netz.m_End })
                    {
                        if (!AvLeseFlusshaelfte(mitte, AvFlussknoten(n, istStrom), istStrom,
                            out var s, out var f, out var a)) continue;
                        graphkanten++;
                        strom = System.Math.Min(strom, s);
                        frisch = System.Math.Min(frisch, f);
                        abwasser = System.Math.Min(abwasser, a);
                    }
                }
                // Nur der kleinste Betrag ALLER Haelften belegt Durchfluss
                // durch den ganzen Kurs; eine einzelne gespeiste Haelfte reicht nicht.
                if (graphkanten != kurs.Kanten.Count * 2 || graphkanten == 0)
                    strom = frisch = abwasser = 0;
                var startAn = AvGraphAnStrasse(start, kurs.Trasse.Startnetz, istStrom);
                var zielAn = AvGraphAnStrasse(ende, new[] { kurs.Trasse.Zielkante }, istStrom);
                kurs.StromMax = System.Math.Max(kurs.StromMax, strom);
                kurs.FrischMax = System.Math.Max(kurs.FrischMax, frisch);
                kurs.AbwasserMax = System.Math.Max(kurs.AbwasserMax, abwasser);
                var fluss = istStrom ? kurs.StromMax > 0
                    : kurs.FrischMax > 0 && kurs.AbwasserMax > 0;
                var stadtpfad = AvZielHatStadtpfad(kurs.Trasse.Zielkante);
                var ok = stadtpfad && dauerhaft == kurs.Kanten.Count && dauerhaft > 0
                    && graphkanten == dauerhaft * 2 && startAn && zielAn && fluss
                    && AvKursZusammenhaengend(kurs, start, ende);
                if (ok) abgenommen++;
                zeilen.Add($"PLT-Autoversorgung FLUSS [{kurs.Name}]: dauerhaft "
                    + $"{dauerhaft}/{kurs.Kanten.Count}, Graphhaelften {graphkanten}/{kurs.Kanten.Count * 2}, "
                    + $"Stadtpfad {(stadtpfad ? 1 : 0)}, Graphanschluss Start/Ziel {(startAn ? 1 : 0)}/{(zielAn ? 1 : 0)}; "
                    + $"aktuell Minimum aller Haelften Strom/Frisch/Abwasser {strom}/{frisch}/{abwasser}, "
                    + $"Maximum seit Neubau (Delta zu 0) {kurs.StromMax}/{kurs.FrischMax}/{kurs.AbwasserMax}; "
                    + (ok ? "Fluss belegt." : "Abnahme OFFEN."));
            }
            /*
             * DIE ABDECKUNG OHNE EIGENE MELDUNG.
             *
             * `AvMesseNetzabdeckung` schreibt selbst ins Log - richtig, wenn
             * es einmal je Planungsrunde gefragt wird, falsch hier, wo alle
             * halbe Sekunde gemessen wird. Die Zahl kommt deshalb aus
             * `AvGedeckteNetze`, und die Zeile geht durch dieselbe
             * Wiederholungssperre wie die Flusszeilen.
             */
            var erreicht = AvGedeckteNetze(out var gesamt);
            var netzeErreicht = erreicht == gesamt;
            zeilen.Add($"PLT-Autoversorgung NETZABDECKUNG: {erreicht}/{gesamt} "
                + "versorgungsfaehige Netze mit Stadtpfad fuer Strom UND "
                + "Wasser/Abwasser; Flussabnahme separat."
                + (netzeErreicht ? "" : " Anschlussnachweis unvollstaendig."));

            var befund = string.Join("\n", zeilen);
            if (letzte || befund != _avLetzterMessbefund)
            {
                _avLetzterMessbefund = befund;
                for (var i = 0; i < zeilen.Count; i++)
                {
                    if (i == zeilen.Count - 1 && !netzeErreicht) Mod.log.Warn(zeilen[i]);
                    else Mod.log.Info(zeilen[i]);
                }
            }
            var alle = _avKurse.Count > 0 && abgenommen == _avKurse.Count && netzeErreicht;
            if (alle || letzte)
            {
                var text = $"PLT-Autoversorgung ABNAHME: {abgenommen}/{_avKurse.Count} Kurse "
                    + "dauerhaft, beidseitig im Versorgungsgraph und mit positivem Fluss bestaetigt.";
                if (alle) Mod.log.Info(text);
                else Mod.log.Warn(text + " NICHT bestanden/belegt. Nullfluss allein unterscheidet "
                    + "fehlende Nachfrage, pausierte Simulation und defekten Anschluss nicht.");
            }
            return alle;
        }

        private Entity AvFlussknoten(Entity e, bool strom)
        {
            if (strom && EntityManager.HasComponent<Game.Simulation.ElectricityNodeConnection>(e))
                return EntityManager.GetComponentData<Game.Simulation.ElectricityNodeConnection>(e).m_ElectricityNode;
            if (!strom && EntityManager.HasComponent<Game.Simulation.WaterPipeNodeConnection>(e))
                return EntityManager.GetComponentData<Game.Simulation.WaterPipeNodeConnection>(e).m_WaterPipeNode;
            return Entity.Null;
        }

        private bool AvLeseFlusshaelfte(Entity a, Entity b, bool strom,
            out long s, out long f, out long w)
        {
            s = f = w = 0;
            if (a == Entity.Null || b == Entity.Null
                || !EntityManager.HasBuffer<Game.Simulation.ConnectedFlowEdge>(a)) return false;
            var gefunden = false;
            foreach (var verbindung in EntityManager.GetBuffer<Game.Simulation.ConnectedFlowEdge>(a, true))
            {
                var e = verbindung.m_Edge;
                if (EntityManager.HasComponent<Deleted>(e)) continue;
                if (strom && EntityManager.HasComponent<Game.Simulation.ElectricityFlowEdge>(e))
                {
                    var edge = EntityManager.GetComponentData<Game.Simulation.ElectricityFlowEdge>(e);
                    if (!((edge.m_Start == a && edge.m_End == b) || (edge.m_Start == b && edge.m_End == a))) continue;
                    s = System.Math.Max(s, System.Math.Abs((long)edge.m_Flow));
                    gefunden = true;
                }
                if (!strom && EntityManager.HasComponent<Game.Simulation.WaterPipeEdge>(e))
                {
                    var edge = EntityManager.GetComponentData<Game.Simulation.WaterPipeEdge>(e);
                    if (!((edge.m_Start == a && edge.m_End == b) || (edge.m_Start == b && edge.m_End == a))) continue;
                    f = System.Math.Max(f, System.Math.Abs((long)edge.m_FreshFlow));
                    w = System.Math.Max(w, System.Math.Abs((long)edge.m_SewageFlow));
                    gefunden = true;
                }
            }
            return gefunden;
        }

        private bool AvGraphAnStrasse(Entity knoten, IEnumerable<Entity> strassen, bool strom)
        {
            var flussknoten = AvFlussknoten(knoten, strom);
            if (flussknoten == Entity.Null || strassen == null) return false;
            foreach (var strasse in strassen)
                if (EntityManager.Exists(strasse) && !EntityManager.HasComponent<Deleted>(strasse)
                    && AvLeseFlusshaelfte(flussknoten, AvFlussknoten(strasse, strom), strom,
                        out _, out _, out _)) return true;
            return false;
        }
    }
}
