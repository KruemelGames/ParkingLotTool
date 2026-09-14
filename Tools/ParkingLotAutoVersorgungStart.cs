using Game.Common;
using Game.Net;
using Game.Tools;
using Game.Prefabs;
using Colossal.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private void AvMeldeStartknoten(Versorgungstrasse trasse)
        {
            var n = trasse.Startknoten;
            if (n != Entity.Null) AvMeldeAnschlussknoten(n, trasse.Herkunft, "START");
            else if (trasse.Startkanten != null)
            {
                // Wie am Ziel: beide Enden melden die dauerhafte Kante an,
                // der unterirdische Kurs bleibt am gewaehlten inneren Punkt.
                foreach (var k in trasse.Startkanten)
                {
                    var edge = EntityManager.GetComponentData<Edge>(k);
                    AvMeldeAnschlussknoten(edge.m_Start, trasse.Herkunft, "START-KANTE");
                    AvMeldeAnschlussknoten(edge.m_End, trasse.Herkunft, "START-KANTE");
                }
            }
        }

        /**
         * WORAN HAENGEN WIR UNS UEBERHAUPT?
         *
         * Der Nutzer am 2026-09-15, mit Bild: *"Die Strasse war auch enorm
         * verformt an die das angeschlossen wurde."* Solange nicht im Log
         * steht, WAS die Zielkante ist, ist das nicht zu beantworten - ein
         * eigenstaendiges Rohr und eine Strasse mit eingebauten Rohren sind
         * voellig verschiedene Anschlusspartner.
         *
         * In CS2 tragen Strassen ihre Versorgung selbst; unsere beiden
         * Prefabs fuehren `Road` in ihren Layern und duerfen sich deshalb
         * auch an eine Strasse haengen. Wenn CS2 dafuer die Strasse TEILT,
         * entsteht dort ein Knoten - und der zieht die Geometrie.
         *
         * Deshalb nennt diese Meldung jetzt Prefab, Art und Hoehenlage der
         * Zielkante. Danach ist es eine Messung statt einer Vermutung.
         */
        private void AvMeldeZielart(Versorgungstrasse trasse)
        {
            var z = trasse.Zielkante;
            if (z == Entity.Null || !EntityManager.Exists(z)) return;
            var name = "unbekannt";
            if (EntityManager.HasComponent<PrefabRef>(z)
                && _prefabSystem != null
                && _prefabSystem.TryGetPrefab<PrefabBase>(
                    EntityManager.GetComponentData<PrefabRef>(z).m_Prefab,
                    out var pb) && pb != null)
                name = pb.name;

            var art = EntityManager.HasComponent<Game.Net.Road>(z)
                ? "STRASSE"
                : "eigenstaendige Leitung";
            var hoehe = "unbekannt";
            if (EntityManager.HasComponent<Curve>(z))
            {
                var b = EntityManager.GetComponentData<Curve>(z).m_Bezier;
                hoehe = $"Welt-Y {b.a.y:F2}/{b.d.y:F2}";
            }
            Mod.log.Info($"PLT-Autoversorgung ZIELART [{trasse.Herkunft}]: "
                + $"Kante {z}, Prefab '{name}', Art {art}, {hoehe}. Unsere "
                + "Leitung endet auf Elevation -10. Ist die Art STRASSE, "
                + "haengen wir uns an eine Strasse mit eingebauten Rohren - "
                + "dann teilt CS2 sie fuer den Anschluss.");
        }

        private void AvMeldeZielstrasse(Versorgungstrasse trasse)
        {
            AvMeldeZielart(trasse);
            if (!EntityManager.HasComponent<Edge>(trasse.Zielkante)) return;
            var e = EntityManager.GetComponentData<Edge>(trasse.Zielkante);
            AvMeldeAnschlussknoten(e.m_Start, trasse.Herkunft, "ZIEL");
            if (e.m_End != e.m_Start) AvMeldeAnschlussknoten(e.m_End, trasse.Herkunft, "ZIEL");
        }

        private void AvMeldeAnschlussknoten(Entity n, string name, string seite)
        {
            if (!EntityManager.HasComponent<Node>(n)) return;

            /*
             * PROBE 2026-09-14: DIESE KNOTENDEFINITION WEGLASSEN.
             *
             * Beim Edit-Bau stuerzt CS2 ab, und die Schrittspur endet zweimal
             * an derselben Stelle - unmittelbar nach "Versorgung: Kurs
             * anlegen". Hier unten legen wir fuer JEDEN Anschlussknoten eine
             * `CreationDefinition` mit `m_Original = n` an, und `n` ist laut
             * Log auch ein Knoten der STADTSTRASSE (`Owner 0`).
             *
             * Das ist dasselbe Muster, das am 2026-09-10 beim Loeschen
             * abgestuerzt ist: wir fassen Knoten an, die uns nicht gehoeren.
             * Belegt wurde es damals nicht durch Nachdenken, sondern durch
             * Weglassen - deshalb hier derselbe Weg.
             *
             * `leitungsknoten` in `<Spielordner>/Logs/PLT-AUS.txt` schaltet
             * es ab. Faellt der Absturz damit weg, liegt es hier. Bleibt er,
             * liegt es NICHT hier, und das ist genauso viel wert. Der Preis
             * der Probe: die Leitung uebernimmt ohne diese Definition die
             * Oberflaechenhoehe des Knotens (siehe Begruendung unten) - fuer
             * eine Probe verkraftbar.
             */
            if (Mod.Aus("leitungsknoten"))
            {
                Mod.log.Info("PLT-PROBE: Knotendefinition fuer den "
                    + "Leitungsanschluss weggelassen (Absturzprobe).");
                return;
            }
            /*
             * HIER STAND EINE DEFINITION AUF EINEM FREMDEN KNOTEN. SIE WAR
             * DIE ABSTURZURSACHE UND SIE WAR UEBERFLUESSIG.
             *
             * Angelegt wurde eine `CreationDefinition` mit
             * `m_Original = n, m_Flags = Select` und einer punktfoermigen
             * `NetCourse`, deren Start UND Ende `m_Entity = n` trugen. `n` ist
             * dabei auch ein Knoten der STADTSTRASSE - im Bauzettel steht
             * `Owner 0`.
             *
             * WAS CS2 DAMIT TUT. `GenerateNodesSystem` ruft daraufhin
             * `AddConnectedNodes(n, ...)` - zweimal, einmal je CoursePos - und
             * dort steht:
             *
             *     if (!m_NodeData.HasComponent(original)) return;
             *     DynamicBuffer<ConnectedEdge> dynamicBuffer2 = m_Edges[original];
             *
             * Der Puffer wird UNGEPRUEFT gelesen; abgesichert ist nur die
             * `Node`-Komponente. Wir reichen das unmittelbar nach einem Abriss
             * hinein, bei dem hunderte Teile und auch Knoten verschwinden.
             * Genau dieselbe Falle wie bei `SubNet`/`SubObject`.
             *
             * GEMESSEN: die Schrittspur endete zweimal unmittelbar nach
             * "Versorgung: Kurs anlegen", Absturzmeldung jedes Mal
             * `UpdateFrame added to unsupported type` ohne managed Stacktrace.
             * Der Nutzer dazu: *"Umso mehr Zoning Areas, umso haeufiger."* Das
             * passt - mehr Flaechen heissen mehr getrennte Netze und damit
             * mehr solcher Knotendefinitionen je Bau.
             *
             * WARUM SIE NICHT GEBRAUCHT WIRD. Der Kommentar von damals
             * begruendete sie mit der Oberflaechenhoehe
             * (`GenerateEdgesSystem.TryGetNode`, Zeile 1237):
             *
             *     if (isPermanent && m_NodeData.HasComponent(coursePos.m_Entity))
             *     { node = coursePos.m_Entity; return true; }
             *
             * Das greift nur, wenn man den Knoten IN DEN LEITUNGSKURS
             * schreibt. Das tun wir nicht - `AvCoursePos` setzt dort
             * `m_Entity = Entity.Null`. Ohne Entity sucht CS2 den Knoten ueber
             * die POSITION (`m_NodeMap.TryGetFirstValue(new NodeMapKey(
             * coursePos, ...))`), und den seitlichen Anschluss legt es
             * ohnehin selbst an - `AddEdgesForLocalConnectOrAttachment` und
             * `AddNodesForLocalConnect` laufen im selben System aus dem Kurs
             * heraus, mit der Position, nicht mit einer von uns benannten
             * Entity.
             *
             * Der Anschluss braucht diese Definition also nicht. Sie war
             * Guertel zum Hosentraeger, und der Guertel hat uns umgebracht.
             *
             * Die Messung bleibt: sie sagt im Bauzettel, an welchem Knoten die
             * Leitung landen soll, und das hat schon mehrfach geholfen.
             */
            var kanten = EntityManager.HasBuffer<ConnectedEdge>(n)
                ? EntityManager.GetBuffer<ConnectedEdge>(n, true).Length : 0;
            Mod.log.Info($"PLT-Autoversorgung {seite}KNOTEN [{name}]: "
                + $"Knoten {n}, Owner {(EntityManager.HasComponent<Owner>(n) ? 1 : 0)}, "
                + $"{kanten} verbundene Kanten. Keine eigene Definition - CS2 "
                + "findet den Anschluss ueber die Position des Kursendes.");
        }

        private void AvMesseStartumfeld(AvKurs kurs, Entity leitungsknoten)
        {
            var lc = EntityManager.GetComponentData<LocalConnectData>(kurs.Prefab);
            var netz = EntityManager.GetComponentData<NetData>(kurs.Prefab);
            var geo = EntityManager.GetComponentData<NetGeometryData>(kurs.Prefab);
            var radius = math.max(0, geo.m_DefaultWidth / 2 + lc.m_SearchDistance);
            var tempStrassen = 0;
            using var temp = AvTempQuery().ToEntityArray(Allocator.Temp);
            foreach (var e in temp)
                if (kurs.Trasse.Startnetz.Contains(EntityManager.GetComponentData<Temp>(e).m_Original)) tempStrassen++;
            Mod.log.Info($"PLT-Autoversorgung STARTUMFELD [{kurs.Name}]: "
                + $"{tempStrassen} temporaere Strassen der Startgruppe; Leitungsknoten {leitungsknoten}, "
                + $"LocalConnect {(EntityManager.HasComponent<LocalConnect>(leitungsknoten) ? 1 : 0)}.");
            foreach (var e in kurs.Trasse.Startnetz)
            {
                var edge = EntityManager.GetComponentData<Edge>(e);
                if (kurs.Trasse.Startkanten == null || !kurs.Trasse.Startkanten.Contains(e)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                var s = EntityManager.GetComponentData<NetData>(prefab);
                var g = EntityManager.GetComponentData<NetGeometryData>(prefab);
                var b = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                var abstand = MathUtils.Distance(b.xz, kurs.Start.xz, out var t);
                if ((g.m_Flags & GeometryFlags.NoEdgeConnection) != 0)
                {
                    t = math.distance(b.a.xz, kurs.Start.xz) < math.distance(b.d.xz, kurs.Start.xz) ? 0 : 1;
                    abstand = math.distance(MathUtils.Position(b, t).xz, kurs.Start.xz);
                }
                var hoehe = MathUtils.Position(b, t).y - kurs.Start.y;
                Mod.log.Info($"PLT-Autoversorgung STARTTORE [{kurs.Name}]: Kante {e}, "
                    + $"Layer hin/zurueck {((lc.m_Layers & s.m_ConnectLayers) != 0 ? 1 : 0)}/"
                    + $"{((netz.m_ConnectLayers & s.m_LocalConnectLayers) != 0 ? 1 : 0)}, "
                    + $"Randabstand {abstand - g.m_DefaultWidth / 2:F3} m / Suchradius {radius:F3} m, "
                    + $"Hoehe {hoehe:F3} m / Fenster {lc.m_HeightRange.min:F3}..{lc.m_HeightRange.max:F3} m.");
            }
        }
    }
}
