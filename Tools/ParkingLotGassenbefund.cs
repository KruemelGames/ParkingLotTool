using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * WAS AUS EINER ZUFAHRTSGASSE GEWORDEN IST.
     *
     * Der Bauzettel meldet bisher nur die ABSICHT: Laenge ab Strassenmitte,
     * Fahrbahnrand, Ueberstand - geschrieben in dem Moment, in dem wir CS2
     * den Kurs hinlegen. Was das Spiel daraus macht, stand nirgends. Der
     * Nutzer am 2026-09-18: *"Ich hab gestern paar mal gebaut und hatte
     * keinen Erfolg fuer eine optisch ordentliche Kreuzung."* Ohne
     * Rueckschau bleibt dazu nur Raten.
     *
     * Gemessen wird deshalb am FERTIGEN Netz, ein paar Bilder nach dem
     * Apply, und zwar das, was eine Kreuzung unordentlich macht:
     *
     *   Kanten am Knoten   3 = Strasse sauber geteilt, die Gasse ist der
     *                          dritte Arm. 2 = nur an einen vorhandenen
     *                          Knoten angedockt, nichts geteilt. 0/1 = die
     *                          Gasse haengt frei in der Luft.
     *   Strasse ersetzt    Die Teilung ersetzt die urspruengliche Kante.
     *                      Lebt sie noch, hat CS2 nicht geteilt.
     *   t                  Wo auf der Kante wir angesetzt haben. Nahe 0
     *                      oder 1 heisst: am Kantenende, also an einem
     *                      vorhandenen Knoten - der haeufigste Grund fuer
     *                      Matsch, weil dort schon eine Kreuzung sitzt.
     *   Versatz            Abstand des entstandenen Knotens von der Stelle,
     *                      an der wir angesetzt haben.
     *   Winkel             Gasse gegen Strassentangente. 90 Grad ist der
     *                      Normalfall; wir setzen senkrecht an.
     *   Knick              Gasse gegen die Richtung der Zufahrt dahinter.
     *                      Gross heisst: am Polygonrand steht ein Knick.
     *   Nachbar            Abstand zum naechsten anderen Knoten. Zu nah,
     *                      und CS2 zieht zwei Kreuzungen ineinander.
     *
     * Der Befund ist reine Beobachtung - er aendert nichts und bricht
     * nichts ab. Er soll die Frage "woran liegt es" von einer Vermutung in
     * eine Zahl verwandeln.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /** Was wir beim Anlegen einer Gasse wussten. */
        private sealed class Gassenplan
        {
            internal int Index;
            /** Ansatzpunkt auf der Mittellinie der Stadtstrasse. */
            internal float2 Mitte;
            /** Aeusseres Ende der Gasse, im Parkplatz. */
            internal float2 Ende;
            /** Die Stadtstrasse, wie sie VOR dem Bau war. */
            internal Entity Strasse;
            /** Kurvenparameter des Ansatzpunktes auf dieser Kante. */
            internal float T;
            /** Richtung der Zufahrt dahinter, fuer den Knick. */
            internal float2 Zufahrtsrichtung;
            /** Halbe Strassenbreite, mit der die Laenge gerechnet wurde. */
            internal float HalbeBreiteGeplant;
            /** Das Gassen-Prefab, fuer seine eigene Breite. */
            internal Entity Gassenprefab;
            /** Breite, mit der die Vorflaeche gerechnet wird (settings.Ai). */
            internal float Vorflaechenbreite;
        }

        private readonly List<Gassenplan> _gassenplan = new List<Gassenplan>();

        /**
         * Ab wann gemessen wird, als Bildzaehler.
         *
         * 0 heisst: nichts angemeldet. Die Teilung einer Strasse braucht
         * mehr als ein Bild - CS2 legt Knoten und Kanten in eigenen Phasen
         * an. Dreissig Bilder sind grosszuegig und immer noch weniger als
         * eine halbe Sekunde.
         */
        private int _gassenbefundAb;

        private const int GassenbefundFrames = 30;

        /** Ab wann ein Knoten als "am Kantenende" gilt. */
        private const float GassenbefundEndnah = 0.05f;

        internal void VergissGassenplan() => _gassenplan.Clear();

        /** Vom Netzbau gerufen, sobald ein Gassenkurs angenommen wurde. */
        internal void MerkeGassenplan(int index, float2 mitte, float2 ende,
                                      Entity strasse, float t,
                                      float2 zufahrtsrichtung,
                                      float halbeBreiteGeplant,
                                      Entity gassenprefab,
                                      float vorflaechenbreite)
        {
            _gassenplan.Add(new Gassenplan
            {
                Index = index,
                Mitte = mitte,
                Ende = ende,
                Strasse = strasse,
                T = t,
                Zufahrtsrichtung = zufahrtsrichtung,
                HalbeBreiteGeplant = halbeBreiteGeplant,
                Gassenprefab = gassenprefab,
                Vorflaechenbreite = vorflaechenbreite,
            });
        }

        /** Nach dem Apply gerufen; die Messung folgt ein paar Bilder spaeter. */
        internal void MeldeGassenbefundAn()
        {
            if (_gassenplan.Count == 0) return;
            _gassenbefundAb = UnityEngine.Time.frameCount + GassenbefundFrames;
        }

        /** Jedes Bild gerufen; tut nur etwas, wenn eine Messung ansteht. */
        private void PruefeGassenbefund()
        {
            if (_gassenbefundAb == 0
                || UnityEngine.Time.frameCount < _gassenbefundAb) return;
            _gassenbefundAb = 0;
            if (_gassenplan.Count == 0) return;

            for (var i = 0; i < _gassenplan.Count; i++)
            {
                MeldeEineGasse(_gassenplan[i]);
                NimmUeberwegeAmKnoten(_gassenplan[i]);
            }
            _gassenplan.Clear();
        }

        /**
         * Nimmt den Strassenkanten am Gassenknoten ihre Fussgaengerueberwege.
         *
         * Messen und Aendern in derselben Datei ist nicht schoen; die Stelle
         * ist es trotzdem. Der Knoten steht erst Frames nach dem Bau fest,
         * und genau darauf wartet dieser Durchgang ohnehin. Die Suche ist
         * dieselbe, das Ergebnis dasselbe - ein zweiter Sucher waere eine
         * zweite Gelegenheit, den Knoten zu verfehlen.
         */
        private void NimmUeberwegeAmKnoten(Gassenplan plan)
        {
            if (KantenAmPunkt(plan.Mitte, out var knoten, out _) == 0) return;
            if (knoten == Entity.Null
                || !EntityManager.HasBuffer<ConnectedEdge>(knoten)) return;

            // Abschreiben, bevor angefasst wird: `AddComponent` ist eine
            // strukturelle Aenderung und macht den Puffer ungueltig.
            var kanten = new List<Entity>();
            var puffer = EntityManager.GetBuffer<ConnectedEdge>(knoten, true);
            for (var i = 0; i < puffer.Length; i++)
            {
                var kante = puffer[i].m_Edge;
                if (kante == Entity.Null || !EntityManager.Exists(kante)) continue;
                if (EntityManager.HasComponent<Deleted>(kante)) continue;
                if (EntityManager.HasComponent<Temp>(kante)) continue;
                kanten.Add(kante);
            }

            var gesetzt = new List<string>();
            foreach (var kante in kanten)
            {
                if (!EntityManager.HasComponent<Game.Net.Edge>(kante)) continue;
                // Die Gasse selbst bringt ihr Flag schon von der Definition
                // mit; hier geht es um die Strassenhaelften.
                var prefab = EntityManager.HasComponent<PrefabRef>(kante)
                    ? EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab
                    : Entity.Null;
                if (prefab == plan.Gassenprefab) continue;

                var edge = EntityManager.GetComponentData<Game.Net.Edge>(kante);
                var amAnfang = edge.m_Start == knoten;
                var seite = amAnfang
                    ? Game.Prefabs.CompositionFlags.Side.RemoveCrosswalk
                    : default;
                var gegenseite = amAnfang
                    ? default
                    : Game.Prefabs.CompositionFlags.Side.RemoveCrosswalk;

                var flaggen = EntityManager.HasComponent<Game.Net.Upgraded>(kante)
                    ? EntityManager.GetComponentData<Game.Net.Upgraded>(kante).m_Flags
                    : default;
                flaggen.m_Left |= seite;
                flaggen.m_Right |= gegenseite;

                if (EntityManager.HasComponent<Game.Net.Upgraded>(kante))
                    EntityManager.SetComponentData(kante,
                        new Game.Net.Upgraded { m_Flags = flaggen });
                else
                    EntityManager.AddComponentData(kante,
                        new Game.Net.Upgraded { m_Flags = flaggen });
                if (!EntityManager.HasComponent<Updated>(kante))
                    EntityManager.AddComponent<Updated>(kante);

                gesetzt.Add(kante.Index + (amAnfang ? " links" : " rechts"));
            }

            if (gesetzt.Count > 0)
                Mod.log.Info("PLT-Gassenueberwege: " + gesetzt.Count
                    + " Strassenkante(n) am Knoten ohne Ueberweg - "
                    + string.Join(", ", gesetzt)
                    + ". Seite aus Anfang/Ende der Kante abgeleitet; "
                    + "verschwinden die Streifen am falschen Ende, ist die "
                    + "Zuordnung zu tauschen.");
        }

        private void MeldeEineGasse(Gassenplan plan)
        {
            var kanten = KantenAmPunkt(plan.Mitte, out var knoten,
                out var versatz);
            var lebt = plan.Strasse != Entity.Null
                && EntityManager.Exists(plan.Strasse)
                && !EntityManager.HasComponent<Deleted>(plan.Strasse);

            var gassenrichtung = math.normalizesafe(plan.Ende - plan.Mitte);
            var winkel = WinkelZurStrasse(plan, gassenrichtung);
            var knick = Winkel(gassenrichtung,
                math.normalizesafe(plan.Zufahrtsrichtung));
            var nachbar = NaechsterAndererKnoten(knoten, plan.Mitte);

            var urteil = kanten >= 3 ? "geteilt"
                : kanten == 2 ? "nur angedockt"
                : kanten == 1 ? "Sackgasse"
                : "kein Knoten";

            Mod.log.Info($"PLT-Gassenbefund: Zufahrt {plan.Index}: {urteil} - "
                + $"{kanten} Kante(n) am Knoten, Versatz {versatz:F2} m; "
                + $"Ausgangskante {(lebt ? "lebt noch" : "ersetzt")}, "
                + $"t={plan.T:F3}"
                + (plan.T <= GassenbefundEndnah || plan.T >= 1f - GassenbefundEndnah
                    ? " (AM KANTENENDE)" : string.Empty)
                + $"; Winkel zur Strasse {winkel:F1} Grad, "
                + $"Knick zur Zufahrt {knick:F1} Grad, "
                + (nachbar < float.MaxValue
                    ? $"naechster Knoten {nachbar:F1} m"
                    : "kein zweiter Knoten in der Naehe")
                + BreitenSatz(plan) + GassenbreitenSatz(plan)
                + MuendungsSatz(plan, knoten)
                + HoehenSatz(plan, knoten)
                + NachbarschaftSatz(plan, knoten) + ".");
        }

        /**
         * DIE BEIDEN ENDEN DER GASSE - Hoehe, Anschluss, Nachbarschaft.
         *
         * Der Nutzer am 2026-09-18: auf einem Parkplatz koennen die Autos
         * nicht auf die Gasse fahren, auf einem zweiten schon. In der
         * Spuransicht steht dort eine schwarze senkrechte Flaeche - so
         * zeichnet CS2 eine Spur mit starkem Gefaelle.
         *
         * Gemessen wird deshalb: die Hoehe beider Gassenknoten, ihr
         * Unterschied, und was am aeusseren Ende ueberhaupt haengt. Der
         * Anschluss nach innen laeuft ueber `LocalConnect` und verlangt dort
         * eine Sackgasse in Reichweite (siehe plt-strassenanbindung); ob es
         * eine gibt, sagt die Kantenzahl.
         */
        private string HoehenSatz(Gassenplan plan, Entity knoten)
        {
            var kante = GassenkanteAn(knoten, plan.Gassenprefab);
            if (kante == Entity.Null
                || !EntityManager.HasComponent<Edge>(kante))
                return string.Empty;
            var edge = EntityManager.GetComponentData<Edge>(kante);
            var fern = edge.m_Start == knoten ? edge.m_End : edge.m_Start;
            if (!EntityManager.HasComponent<Game.Net.Node>(knoten)
                || !EntityManager.HasComponent<Game.Net.Node>(fern))
                return string.Empty;

            var aStrasse = EntityManager
                .GetComponentData<Game.Net.Node>(knoten).m_Position;
            var aInnen = EntityManager
                .GetComponentData<Game.Net.Node>(fern).m_Position;
            var kanten = EntityManager.HasBuffer<ConnectedEdge>(fern)
                ? EntityManager.GetBuffer<ConnectedEdge>(fern, true).Length : 0;
            var laenge = math.distance(aStrasse.xz, aInnen.xz);
            var gefaelle = aInnen.y - aStrasse.y;

            return $"; Gassenknoten Strasse {aStrasse.y:F2} m / innen "
                + $"{aInnen.y:F2} m, Unterschied {gefaelle:F2} m auf "
                + $"{laenge:F2} m"
                + (laenge > 0.1f
                    ? $" ({math.degrees(math.atan(math.abs(gefaelle) / laenge)):F1} Grad)"
                    : string.Empty)
                + $", inneres Ende mit {kanten} Kante(n)";
        }

        /**
         * WAS AM INNEREN ENDE DER GASSE IN REICHWEITE LIEGT.
         *
         * `LocalConnect` erzeugt KEINE Kante - es verbindet einen
         * Sackgassenknoten eines Weges mit einer Strassenkante. Die
         * Kantenzahl am Gassenende ist deshalb kein Beweis, und die Frage
         * lautet anders: liegt in Reichweite ueberhaupt ein Knoten unseres
         * Weges, und ist er eine Sackgasse?
         *
         * Reichweite ist `Strassenbreite/2 + Wegbreite/2 + 4 m`
         * (plt-strassenanbindung). Bei 8 m Gasse sind das rund 10 m; 12 m
         * Suchweite zeigen also auch knapp Verfehltes.
         *
         * Gemeldet werden die drei naechsten Knoten mit Abstand, Hoehe und
         * Kantenzahl. Eine Sackgasse traegt genau EINE Kante.
         */
        private string NachbarschaftSatz(Gassenplan plan, Entity knoten)
        {
            var kante = GassenkanteAn(knoten, plan.Gassenprefab);
            if (kante == Entity.Null
                || !EntityManager.HasComponent<Edge>(kante))
                return string.Empty;
            var edge = EntityManager.GetComponentData<Edge>(kante);
            var fern = edge.m_Start == knoten ? edge.m_End : edge.m_Start;
            if (!EntityManager.HasComponent<Game.Net.Node>(fern))
                return string.Empty;
            var ort = EntityManager
                .GetComponentData<Game.Net.Node>(fern).m_Position;

            var gefunden = new List<(float Abstand, int Kanten, float Hoehe)>();
            var query = GetEntityQuery(
                ComponentType.ReadOnly<Game.Net.Node>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            using (var liste = query.ToEntityArray(Allocator.TempJob))
                for (var i = 0; i < liste.Length; i++)
                {
                    if (liste[i] == fern || liste[i] == knoten) continue;
                    var p = EntityManager
                        .GetComponentData<Game.Net.Node>(liste[i]).m_Position;
                    var d = math.distance(p.xz, ort.xz);
                    if (d > 12f) continue;
                    var n = EntityManager.HasBuffer<ConnectedEdge>(liste[i])
                        ? EntityManager.GetBuffer<ConnectedEdge>(liste[i], true).Length
                        : 0;
                    gefunden.Add((d, n, p.y - ort.y));
                }
            if (gefunden.Count == 0)
                return "; KEIN Knoten in 12 m um das innere Ende";
            gefunden.Sort((a, b) => a.Abstand.CompareTo(b.Abstand));
            var text = "; um das innere Ende:";
            for (var i = 0; i < math.min(3, gefunden.Count); i++)
                text += $" {gefunden[i].Abstand:F2} m/"
                    + $"{gefunden[i].Kanten} Kante(n)/"
                    + $"{gefunden[i].Hoehe:F2} m hoeher";
            return text;
        }

        /** Die Gassenkante an einem Knoten, oder `Entity.Null`. */
        private Entity GassenkanteAn(Entity knoten, Entity gassenprefab)
        {
            if (knoten == Entity.Null || gassenprefab == Entity.Null
                || !EntityManager.HasBuffer<ConnectedEdge>(knoten))
                return Entity.Null;
            var kanten = EntityManager.GetBuffer<ConnectedEdge>(knoten, true);
            for (var i = 0; i < kanten.Length; i++)
            {
                var kante = kanten[i].m_Edge;
                if (EntityManager.Exists(kante)
                    && EntityManager.HasComponent<PrefabRef>(kante)
                    && EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab
                        == gassenprefab)
                    return kante;
            }
            return Entity.Null;
        }

        /**
         * DIE MUNDBREITE AM KNOTEN, aus CS2s eigener Knotengeometrie.
         *
         * `StartNodeGeometry`/`EndNodeGeometry` halten je Kante ein
         * `EdgeNodeGeometry`. Dessen `m_Left.m_Left` und `m_Right.m_Right`
         * sind die beiden Aussenkanten der Aufweitung am Knoten, in
         * Weltkoordinaten (Dekompilat Game.dll, 2026-09-18).
         *
         * Gemessen wird ihr Abstand am Knoten (t=0) und am anderen Ende der
         * Aufweitung (t=1). Der erste Wert ist die Oeffnung an der Strasse,
         * der zweite die Breite, in die sie ausklingt - bei uns muesste das
         * die Gassenbreite sein.
         *
         * Findet sich keine Gassenkante am Knoten, bleibt der Satz leer.
         * Eine erfundene Zahl waere hier schlimmer als keine.
         */
        private string MuendungsSatz(Gassenplan plan, Entity knoten)
        {
            if (knoten == Entity.Null
                || !EntityManager.HasBuffer<ConnectedEdge>(knoten)
                || plan.Gassenprefab == Entity.Null)
                return string.Empty;

            var kante = GassenkanteAn(knoten, plan.Gassenprefab);
            if (kante != Entity.Null
                && EntityManager.HasComponent<Edge>(kante))
            {
                var edge = EntityManager.GetComponentData<Edge>(kante);
                EdgeNodeGeometry geo;
                if (edge.m_Start == knoten
                    && EntityManager.HasComponent<StartNodeGeometry>(kante))
                    geo = EntityManager
                        .GetComponentData<StartNodeGeometry>(kante).m_Geometry;
                else if (edge.m_End == knoten
                    && EntityManager.HasComponent<EndNodeGeometry>(kante))
                    geo = EntityManager
                        .GetComponentData<EndNodeGeometry>(kante).m_Geometry;
                else return "; keine Knotengeometrie an der Gassenkante";

                var amKnoten = Abstand(geo, 0f);
                var amEnde = Abstand(geo, 1f);
                return $"; Muendung am Knoten {amKnoten:F2} m, am Ende der "
                    + $"Aufweitung {amEnde:F2} m, Mittelradius "
                    + $"{geo.m_MiddleRadius:F2} m";
            }
            return "; keine Gassenkante am Knoten gefunden";
        }

        /** Abstand der beiden Aussenkurven der Knotengeometrie bei `t`. */
        private static float Abstand(EdgeNodeGeometry geo, float t)
        {
            var links = MathUtils.Position(geo.m_Left.m_Left, t);
            var rechts = MathUtils.Position(geo.m_Right.m_Right, t);
            return math.distance(links.xz, rechts.xz);
        }

        /**
         * DIE GASSE GEGEN IHRE VORFLAECHE.
         *
         * `Entrance.Breite()` kennt drei Faelle - einspurig, Fussweg, sonst -
         * und "sonst" heisst: die Fahrgassenbreite aus den Einstellungen. Die
         * Gasse faellt in diesen Fall, obwohl sie eine echte Strasse mit
         * eigener Breite ist. Ist sie breiter als ihre Vorflaeche, bleibt an
         * ihren Flanken Gras stehen; mit einem Winkelunterschied wird daraus
         * ein einseitiger Keil.
         *
         * Genau das steht auf dem Bild des Nutzers vom 2026-09-18. Diese
         * Zeile sagt, ob die Rechnung dazu passt.
         */
        private string GassenbreitenSatz(Gassenplan plan)
        {
            if (plan.Gassenprefab == Entity.Null
                || !EntityManager.Exists(plan.Gassenprefab)
                || !EntityManager.HasComponent<NetGeometryData>(plan.Gassenprefab))
                return string.Empty;
            var gasse = EntityManager
                .GetComponentData<NetGeometryData>(plan.Gassenprefab).m_DefaultWidth;
            var fehlt = gasse - plan.Vorflaechenbreite;
            return $"; Gasse {gasse:F2} m breit, Vorflaeche "
                + $"{plan.Vorflaechenbreite:F2} m"
                + (fehlt > 0.05f
                    ? $" (VORFLAECHE ZU SCHMAL um {fehlt:F2} m)" : string.Empty);
        }

        /**
         * DIE BREITE, MIT DER WIR GERECHNET HABEN, GEGEN DIE ECHTE.
         *
         * Die Laenge der Gasse kommt aus `m_DefaultWidth` des Strassenprefabs.
         * Gebaut wird eine Kante aber nach ihrer COMPOSITION, und die kann
         * breiter sein - andere Gehwegbreite, mit RoadBuilder gebaut, je
         * Seite verschieden. Ist sie breiter, endet unsere Gasse womoeglich
         * vor dem echten Fahrbahnrand, und dann teilt CS2 nichts.
         *
         * Steht in derselben Zeile, weil man beide Zahlen nur zusammen
         * lesen kann.
         */
        private string BreitenSatz(Gassenplan plan)
        {
            if (plan.Strasse == Entity.Null
                || !EntityManager.Exists(plan.Strasse)
                || !EntityManager.HasComponent<Composition>(plan.Strasse))
                return string.Empty;
            var composition = EntityManager
                .GetComponentData<Composition>(plan.Strasse).m_Edge;
            if (composition == Entity.Null
                || !EntityManager.HasComponent<NetCompositionData>(composition))
                return string.Empty;
            var echt = EntityManager
                .GetComponentData<NetCompositionData>(composition).m_Width * 0.5f;
            var abweichung = echt - plan.HalbeBreiteGeplant;
            return $"; halbe Breite gerechnet {plan.HalbeBreiteGeplant:F2} m, "
                + $"an der Kante {echt:F2} m"
                + (math.abs(abweichung) > 0.05f
                    ? $" (ABWEICHUNG {abweichung:F2} m)" : string.Empty);
        }

        /**
         * Der naechste fertige Knoten zu einem Punkt und seine Kantenzahl.
         *
         * `Temp` und `Deleted` bleiben draussen: waehrend des Baus liegen
         * beide Fassungen nebeneinander, und die Temp-Fassung wuerde die
         * Zahl verdoppeln. Dieselbe Regel wie in der Bordsteinsonde.
         */
        private int KantenAmPunkt(float2 punkt, out Entity knoten,
                                  out float versatz)
        {
            knoten = Entity.Null;
            versatz = float.MaxValue;
            var query = GetEntityQuery(
                ComponentType.ReadOnly<Game.Net.Node>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            using var knotenliste = query.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < knotenliste.Length; i++)
            {
                var p = EntityManager
                    .GetComponentData<Game.Net.Node>(knotenliste[i]).m_Position;
                var d = math.distance(p.xz, punkt);
                if (d >= versatz) continue;
                versatz = d;
                knoten = knotenliste[i];
            }
            // Drei Meter: weiter weg ist es nicht mehr unser Knoten, sondern
            // irgendeiner. Dann zaehlt er auch nicht als Ergebnis.
            if (knoten == Entity.Null || versatz > 3f)
            {
                knoten = Entity.Null;
                versatz = float.NaN;
                return 0;
            }
            return EntityManager.HasBuffer<ConnectedEdge>(knoten)
                ? EntityManager.GetBuffer<ConnectedEdge>(knoten, true).Length
                : 0;
        }

        /**
         * Winkel zwischen Gasse und Strassentangente am Ansatzpunkt.
         *
         * Lebt die Ausgangskante nicht mehr, ist ihre Kurve weg - dann
         * bleibt der Winkel unbekannt, und das ist ehrlicher als ein Wert
         * aus einer anderen Kante.
         */
        private float WinkelZurStrasse(Gassenplan plan, float2 gassenrichtung)
        {
            if (plan.Strasse == Entity.Null
                || !EntityManager.Exists(plan.Strasse)
                || !EntityManager.HasComponent<Curve>(plan.Strasse))
                return float.NaN;
            var bogen = EntityManager.GetComponentData<Curve>(plan.Strasse).m_Bezier;
            var tangente = math.normalizesafe(
                MathUtils.Tangent(bogen, plan.T).xz);
            return Winkel(gassenrichtung, tangente);
        }

        /** Unbenannter Winkel zwischen zwei Richtungen, 0 bis 90 Grad. */
        private static float Winkel(float2 a, float2 b)
        {
            if (math.lengthsq(a) < 1e-6f || math.lengthsq(b) < 1e-6f)
                return float.NaN;
            var cos = math.clamp(math.abs(math.dot(a, b)), 0f, 1f);
            return math.degrees(math.acos(cos));
        }

        /** Abstand zum naechsten Knoten, der nicht der gefundene ist. */
        private float NaechsterAndererKnoten(Entity knoten, float2 punkt)
        {
            var beste = float.MaxValue;
            var query = GetEntityQuery(
                ComponentType.ReadOnly<Game.Net.Node>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            using var knotenliste = query.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < knotenliste.Length; i++)
            {
                if (knotenliste[i] == knoten) continue;
                var p = EntityManager
                    .GetComponentData<Game.Net.Node>(knotenliste[i]).m_Position;
                var d = math.distance(p.xz, punkt);
                if (d < beste) beste = d;
            }
            return beste;
        }
    }
}
