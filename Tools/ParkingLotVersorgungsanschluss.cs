using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * WAS DER NUTZER SELBST ANGESCHLOSSEN HAT, UEBERLEBT DEN UMBAU.
     *
     * Sein Befund am 2026-09-05:
     *
     *   *"Wenn ich edite und bereits unter unseren Strassen Stromleitung
     *   (unterirdisch) und Wasser/Abwasser (Doppelrohr) gelegt habe von der
     *   Hauptstrasse an, und ich wieder auf Bauen gehe, dann sind die
     *   ehemaligen Verbindungen unterbrochen weil neue Strasse."*
     *
     * WARUM CS2 DEN ANSCHLUSS NICHT VON SELBST WIEDERFINDET - im Dekompilat
     * von `Game.Tools.GenerateEdgesSystem` nachgelesen, nicht vermutet:
     *
     *   Der seitliche Anschluss einer Leitung an eine Strasse steht als
     *   `ConnectedNode` im Puffer der STRASSENKANTE. Wer ihn dort eintraegt,
     *   ist `FindNodeConnections`. Diese Suche kennt zwei Faelle:
     *
     *   - Beim Bau aus einer Definition laeuft sie mit `isPermanent: false`
     *     und findet deshalb ausschliesslich TEMPORAERE Knoten. Einen
     *     bestehenden Leitungsknoten sieht sie nicht. Was CS2 stattdessen
     *     rettet, ist die ALTE KANTE: `TryGetOldEntity` holt deren
     *     `ConnectedNode`-Puffer und uebernimmt daraus, was noch passt.
     *   - Fuer eine bereits dauerhafte Kante mit `Updated` laeuft
     *     `UpdateNodeConnections` mit `isPermanent: true` - und die findet
     *     sehr wohl dauerhafte Leitungsknoten.
     *
     *   Wir loeschen unsere alten Kanten einen Frame VOR dem Neubau. Damit
     *   gibt es keine alte Kante mehr, von der geerbt werden koennte, und der
     *   Anschluss faellt ersatzlos weg. Genau das meldet der Nutzer.
     *
     * ERST GEBETEN, DANN SELBST GESCHRIEBEN. Der erste Anlauf setzte nur
     * `Updated` auf unsere neuen Kanten und die Leitungsknoten, damit CS2 die
     * Suche wiederholt. Das blieb dreimal wirkungslos, und der Lauf vom
     * 13:38 hat gezeigt, dass es NICHT am Zeitpunkt lag: alle vier Bedingungen
     * aus `FindNodeConnections` waren erfuellt, und die Frame-fuer-Frame-
     * Beobachtung sah den Anschluss weder entstehen noch verschwinden.
     *
     * Seitdem schreibt `KnuepfeVersorgungsanschluss` die zwei Eintraege
     * selbst - dieselben, die CS2 anlegen wuerde, an der gemessenen
     * Kurvenlage, und nur wenn dieselben vier Bedingungen erfuellt sind.
     */
    internal struct Versorgungsanschluss
    {
        /** Der fremde Leitungsknoten, der an unserer Strasse hing. */
        internal Entity Fremdknoten;

        /** Unsere Kante, an der er hing - nach dem Loeschen nur noch Nummer. */
        internal Entity UnsereKante;

        /** Wo auf unserer Kante, als Kurvenparameter 0..1. */
        internal float Kurvenlage;

        /** Weltposition des Fremdknotens, damit der Neubau ihn wiederfindet. */
        internal float3 Position;

        /** Name des Leitungsprefabs - fuer den Bauzettel. */
        internal string Leitung;

        /**
         * Traegt der Knoten `Game.Net.LocalConnect`?
         *
         * Ohne diese Komponente nimmt CS2 ihn nicht in die Kandidatenliste
         * auf, und dann kann die Wiederherstellung nicht greifen. Die Zahl
         * steht im Log, damit ein Fehlschlag nicht wieder als Raetsel
         * dasteht.
         */
        internal bool Anschlussfaehig;
    }

    public sealed partial class ParkingLotToolSystem
    {
        private readonly List<Versorgungsanschluss> _versorgungsanschluesse
            = new List<Versorgungsanschluss>();

        internal IReadOnlyList<Versorgungsanschluss> Versorgungsanschluesse
            => _versorgungsanschluesse;

        /**
         * Die Nachkontrolle laeuft ausserhalb des Bearbeiten-Zustands weiter -
         * `ClearEditState` darf sie nicht mitreissen, sonst faellt die Antwort
         * auf "hat es geklappt?" genau dann aus, wenn sie gebraucht wird.
         */
        private readonly List<Entity> _versorgungKnotenPruefung
            = new List<Entity>();
        private readonly HashSet<Entity> _versorgungNeueKanten
            = new HashSet<Entity>();
        private int _versorgungPruefFrame;

        /**
         * Wer hier drin steht, HING schon einmal an einer neuen Kante.
         *
         * Das trennt die beiden einzigen verbliebenen Erklaerungen
         * voneinander: "der Anschluss entsteht nie" und "er entsteht und wird
         * gleich wieder weggenommen". Ohne diese Unterscheidung waere jede
         * Abhilfe wieder geraten.
         */
        private readonly HashSet<Entity> _versorgungJeVerbunden
            = new HashSet<Entity>();

        /**
         * Wurde das Warnsymbol schon neu berechnen lassen?
         *
         * Trennt die zwei Beobachtungsfenster: erst haelt der Anschluss, dann
         * wird die Anzeige aufgefrischt, dann wird nochmal geprueft, ob er das
         * Auffrischen ueberlebt hat.
         */
        private bool _versorgungWarnungAufgefrischt;

        /** Wie lange auf CS2s Neuberechnung gewartet wird. */
        private const int VersorgungPruefFrames = 30;

        /**
         * Sammelt vor dem Loeschen alle FREMDEN Leitungsknoten, die seitlich
         * an unseren Strassen haengen.
         *
         * "Fremd" heisst: gehoert nicht unserem Lot. Genau die sind es, die
         * der Nutzer selbst gezogen hat und die er nach dem Umbau wiederhaben
         * will. Unsere eigenen Knoten entstehen ohnehin neu.
         *
         * Erkannt wird eine Leitung an ihren Anschlussdaten am Prefab -
         * Strom ueber `ElectricityConnectionData`, Wasser und Abwasser ueber
         * `WaterPipeConnectionData`. Das ist zuverlaessiger als der Name:
         * Namen aendern sich mit der Sprache und mit Mods.
         */
        private void ErfasseVersorgungsanschluesse(Entity lot)
        {
            _versorgungsanschluesse.Clear();
            if (lot == Entity.Null || !EntityManager.Exists(lot)) return;
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(lot)) return;

            var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(lot, true);
            var gesehen = new HashSet<Entity>();
            for (var i = 0; i < subNets.Length; i++)
            {
                var kante = subNets[i].m_SubNet;
                if (kante == Entity.Null || !EntityManager.Exists(kante))
                    continue;
                if (!EntityManager.HasBuffer<ConnectedNode>(kante)) continue;

                var angehaengt = EntityManager.GetBuffer<ConnectedNode>(
                    kante, true);
                for (var k = 0; k < angehaengt.Length; k++)
                {
                    var knoten = angehaengt[k].m_Node;
                    if (knoten == Entity.Null || !EntityManager.Exists(knoten))
                        continue;
                    if (!gesehen.Add(knoten)) continue;
                    // Automatische PLT-Anschluesse werden abgerissen und neu gebaut.
                    // Gemischte Knoten mit manuellen Leitungen weiterhin erhalten.
                    if (IstReinerAutomatischerVersorgungsknoten(knoten, lot)) continue;
                    // Unsere eigenen Knoten interessieren nicht - die baut der
                    // Neubau ohnehin neu.
                    if (EntityManager.HasComponent<Owner>(knoten)
                        && EntityManager.GetComponentData<Owner>(knoten)
                            .m_Owner == lot) continue;
                    if (!IstVersorgungsknoten(knoten, out var leitung))
                        continue;

                    /*
                     * DIE POSITION STEHT AM NETZKNOTEN, NICHT AM TRANSFORM.
                     *
                     * Der erste Anlauf las `Game.Objects.Transform` und
                     * meldete deshalb fuer jeden gefundenen Anschluss
                     * "(0,0/0,0)". Ein Leitungsknoten ist ein Netzknoten und
                     * hat keinen Objekt-Transform. Eine Null-Position sieht
                     * aus wie ein Fehler in der Suche, war aber nur der
                     * falsche Ableseort.
                     */
                    var position = float3.zero;
                    if (EntityManager.HasComponent<Game.Net.Node>(knoten))
                        position = EntityManager
                            .GetComponentData<Game.Net.Node>(knoten)
                            .m_Position;

                    _versorgungsanschluesse.Add(new Versorgungsanschluss
                    {
                        Fremdknoten = knoten,
                        UnsereKante = kante,
                        Kurvenlage = angehaengt[k].m_CurvePosition,
                        Position = position,
                        Leitung = leitung,
                        Anschlussfaehig = EntityManager
                            .HasComponent<Game.Net.LocalConnect>(knoten),
                    });
                }
            }

            if (_versorgungsanschluesse.Count == 0)
            {
                Mod.log.Info("PLT-Versorgung: keine fremden Leitungen an "
                    + "unseren Strassen - beim Umbau geht nichts verloren.");
                return;
            }

            var text = new List<string>();
            var ohneLocalConnect = 0;
            foreach (var a in _versorgungsanschluesse)
            {
                if (!a.Anschlussfaehig) ohneLocalConnect++;
                text.Add($"{a.Leitung} #{a.Fremdknoten.Index} bei "
                    + $"({a.Position.x:F1}/{a.Position.z:F1}), Kurvenlage "
                    + $"{a.Kurvenlage:F3}"
                    + (a.Anschlussfaehig ? "" : " OHNE LocalConnect"));
            }
            Mod.log.Info($"PLT-Versorgung: {_versorgungsanschluesse.Count} "
                + "fremde(r) Leitungsanschluss/-anschluesse an unseren "
                + "Strassen erfasst; sie werden nach dem Neubau wieder "
                + "angeknuepft: " + string.Join(" | ", text));
            if (ohneLocalConnect > 0)
                Mod.log.Warn($"PLT-Versorgung: {ohneLocalConnect} der "
                    + "erfassten Knoten tragen kein LocalConnect. CS2 nimmt "
                    + "sie nicht in die Kandidatenliste auf; fuer sie kann "
                    + "die Wiederherstellung nicht greifen.");
        }

        /**
         * Traegt der Knoten ein Leitungsnetz? Gefragt wird am Prefab, nicht am
         * Namen.
         */
        private bool IstVersorgungsknoten(Entity knoten, out string leitung)
        {
            leitung = null;
            if (!EntityManager.HasComponent<PrefabRef>(knoten)) return false;
            var prefab = EntityManager.GetComponentData<PrefabRef>(knoten)
                .m_Prefab;
            if (prefab == Entity.Null || !EntityManager.Exists(prefab))
                return false;

            var strom = EntityManager
                .HasComponent<ElectricityConnectionData>(prefab);
            var wasser = EntityManager
                .HasComponent<WaterPipeConnectionData>(prefab);
            if (!strom && !wasser) return false;

            /*
             * DEN NAMEN LIEFERT DAS PREFAB-VERZEICHNIS UEBER DIE INSTANZ.
             *
             * `PrefabNameOf` erwartet eine INSTANZ und schlaegt ueber deren
             * `PrefabRef` nach. Hier stand vorher die Prefab-Entity selbst -
             * die hat kein `PrefabRef`, und das Log meldete fuer jede
             * gefundene Leitung "unbekannte Leitung". Derselbe Griff hatte
             * schon den Strassenprefab-Vergleich blind gemacht.
             */
            var name = PrefabNameOf(knoten) ?? "unbekannte Leitung";
            leitung = strom && wasser ? name + " (Strom+Wasser)"
                : strom ? name + " (Strom)" : name + " (Wasser)";
            return true;
        }

        /**
         * Knuepft die erfassten Anschluesse an den neuen Strassen wieder an.
         *
         * GEMESSEN, WARUM WIR ES SELBST TUN. Der erste Anlauf hat CS2 nur
         * gebeten: neue Kanten und Leitungsknoten bekamen im selben Frame ein
         * `Updated`, damit `UpdateNodeConnections` die Suche wiederholt. Das
         * blieb dreimal wirkungslos - und der Lauf am 2026-09-05 um 13:38 hat
         * gezeigt, dass es nicht am Zeitpunkt lag:
         *
         *   - Alle vier Bedingungen aus `FindNodeConnections` waren erfuellt
         *     (Ebene hin, Ebene zurueck, Abstand, Hoehe - je "JA").
         *   - Die Frame-fuer-Frame-Beobachtung meldete NIE ein Entstehen und
         *     nie ein Verschwinden. Der Anschluss wird also nicht angelegt und
         *     wieder verworfen, sondern gar nicht erst angelegt.
         *
         * Deshalb wird er hier geschrieben: `ConnectedNode` an unserer Kante
         * und `ConnectedEdge` am Leitungsknoten - genau die zwei Eintraege,
         * die CS2 selbst anlegt, an der gemessenen Kurvenlage.
         *
         * DASS DAS BESTAND HAT, IST EBENFALLS GEMESSEN. `Rationalize
         * ConnectedNodesJob` raeumt solche Eintraege wieder weg, wenn das
         * Leitungsprefab `ChooseBest` traegt und eine NAEHERE Kante existiert.
         * Beide Leitungen trugen `ChooseBest` - aber die Nachbarliste desselben
         * Laufs zeigte je genau einen Nachbarn, naemlich das eigene Rohr
         * beziehungsweise Kabel. Es gibt keine bessere Kante, also gewinnt
         * unsere.
         *
         * Geprueft wird trotzdem dieselbe Regel, die CS2 pruefen wuerde: faellt
         * eine der vier Bedingungen aus, wird NICHT angeschlossen. Wir schaffen
         * keine Verbindung, die CS2 selbst ablehnen wuerde.
         */
        private void StelleVersorgungsanschluesseWiederHer(Entity traeger)
        {
            _versorgungKnotenPruefung.Clear();
            _versorgungNeueKanten.Clear();
            _versorgungJeVerbunden.Clear();
            _versorgungWarnungAufgefrischt = false;
            if (_versorgungsanschluesse.Count == 0) return;
            if (traeger == Entity.Null || !EntityManager.Exists(traeger)
                || !EntityManager.HasBuffer<Game.Net.SubNet>(traeger))
            {
                Mod.log.Warn("PLT-Versorgung: der neue Traeger hat kein "
                    + "SubNet; die erfassten Anschluesse koennen nicht wieder "
                    + "angeknuepft werden.");
                return;
            }

            var kanten = 0;
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
                _versorgungNeueKanten.Add(kante);
                kanten++;
            }

            var knoten = 0;
            foreach (var a in _versorgungsanschluesse)
            {
                var n = a.Fremdknoten;
                if (n == Entity.Null || !EntityManager.Exists(n)) continue;
                if (EntityManager.HasComponent<Deleted>(n)) continue;
                _versorgungKnotenPruefung.Add(n);
                knoten++;
            }

            if (kanten == 0 || knoten == 0)
            {
                Mod.log.Warn($"PLT-Versorgung: {kanten} neue Kante(n) und "
                    + $"{knoten} erhaltene(r) Leitungsknoten - fuer eine "
                    + "Wiederherstellung braucht es beides. Nichts angemeldet.");
                _versorgungKnotenPruefung.Clear();
                _versorgungNeueKanten.Clear();
                return;
            }

            _versorgungSchutzDatei = null;
            ProtokolliereVersorgungsschutz("VOR-WIEDERANSCHLUSS");
            var geknuepft = 0;
            foreach (var a in _versorgungsanschluesse)
                if (KnuepfeVersorgungsanschluss(a)) geknuepft++;

            ProtokolliereVersorgungsschutz("NACH-WIEDERANSCHLUSS");
            _versorgungPruefFrame = UnityEngine.Time.frameCount;
            Mod.log.Info($"PLT-Versorgung: {geknuepft} von {knoten} "
                + $"Leitungsanschluss/-anschluessen an {kanten} neuen "
                + "Strassenkanten wieder eingetragen.");
        }

        /**
         * Traegt EINEN Anschluss ein - nach denselben vier Regeln wie CS2.
         *
         * Der Rueckgabewert sagt, ob geschrieben wurde. Die vier Regeln stehen
         * einzeln im Log, damit ein "nein" nie als "es ist halt nichts
         * passiert" durchgeht.
         */
        private bool KnuepfeVersorgungsanschluss(Versorgungsanschluss a)
        {
            var knoten = a.Fremdknoten;
            if (knoten == Entity.Null || !EntityManager.Exists(knoten))
                return false;
            if (!EntityManager.HasComponent<PrefabRef>(knoten)) return false;
            var leitungsprefab = EntityManager
                .GetComponentData<PrefabRef>(knoten).m_Prefab;
            if (!EntityManager.HasComponent<LocalConnectData>(leitungsprefab))
            {
                Mod.log.Warn($"PLT-Versorgungstore: {a.Leitung} hat gar keine "
                    + "LocalConnectData am Prefab - CS2 kann sie seitlich an "
                    + "nichts anhaengen, wir also auch nicht.");
                return false;
            }
            var lc = EntityManager
                .GetComponentData<LocalConnectData>(leitungsprefab);
            var lNetz = EntityManager.HasComponent<NetData>(leitungsprefab)
                ? EntityManager.GetComponentData<NetData>(leitungsprefab)
                : default;
            var lGeo = EntityManager
                    .HasComponent<NetGeometryData>(leitungsprefab)
                ? EntityManager
                    .GetComponentData<NetGeometryData>(leitungsprefab)
                : default;
            var suchweite = math.max(0f,
                lGeo.m_DefaultWidth * 0.5f + lc.m_SearchDistance);
            var ort = EntityManager.HasComponent<Game.Net.Node>(knoten)
                ? EntityManager.GetComponentData<Game.Net.Node>(knoten)
                    .m_Position
                : a.Position;

            // Die naechstgelegene unserer neuen Kanten ist die, an der CS2 den
            // Anschluss am ehesten festmachen wuerde.
            var beste = Entity.Null;
            var besterAbstand = float.MaxValue;
            var besteLage = 0f;
            foreach (var kante in _versorgungNeueKanten)
            {
                if (!EntityManager.HasComponent<Game.Net.Curve>(kante))
                    continue;
                var bogen = EntityManager
                    .GetComponentData<Game.Net.Curve>(kante).m_Bezier;
                var abstand = MathUtils.Distance(bogen.xz, ort.xz, out var t);
                if (abstand >= besterAbstand) continue;
                besterAbstand = abstand;
                besteLage = t;
                beste = kante;
            }
            if (beste == Entity.Null)
            {
                Mod.log.Warn($"PLT-Versorgungstore: {a.Leitung} - keine "
                    + "einzige neue Kante mit Kurve gefunden.");
                return false;
            }

            var strassenprefab = EntityManager
                .GetComponentData<PrefabRef>(beste).m_Prefab;
            var sNetz = EntityManager.HasComponent<NetData>(strassenprefab)
                ? EntityManager.GetComponentData<NetData>(strassenprefab)
                : default;
            var sGeo = EntityManager
                    .HasComponent<NetGeometryData>(strassenprefab)
                ? EntityManager
                    .GetComponentData<NetGeometryData>(strassenprefab)
                : default;
            var strassenname = PrefabNameOf(beste) ?? "unbekannt";

            var torEbeneHin = (lc.m_Layers & sNetz.m_ConnectLayers) != 0;
            var torEbeneZurueck =
                (lNetz.m_ConnectLayers & sNetz.m_LocalConnectLayers) != 0;
            var rand = besterAbstand - sGeo.m_DefaultWidth * 0.5f;
            var torAbstand = rand <= suchweite;
            var bogen2 = EntityManager
                .GetComponentData<Game.Net.Curve>(beste).m_Bezier;
            var hoehendifferenz =
                MathUtils.Position(bogen2, besteLage).y - ort.y;
            var torHoehe = MathUtils.Intersect(
                lc.m_HeightRange, hoehendifferenz);
            var alleTore = torEbeneHin && torEbeneZurueck && torAbstand
                && torHoehe;

            Mod.log.Info($"PLT-Versorgungstore {a.Leitung}: naechste eigene "
                + $"Kante #{beste.Index} '{strassenname}', Abstand "
                + $"{besterAbstand:F2} m, Lage {besteLage:F3}."
                + $" | 1 Leitung will unsere Ebene: "
                + $"{(torEbeneHin ? "JA" : "NEIN")} "
                + $"(Leitung [{lc.m_Layers}] gegen Strasse Connect "
                + $"[{sNetz.m_ConnectLayers}])"
                + $" | 2 Strasse nimmt die Leitung an: "
                + $"{(torEbeneZurueck ? "JA" : "NEIN")} "
                + $"(Leitung Connect [{lNetz.m_ConnectLayers}] gegen Strasse "
                + $"LocalConnect [{sNetz.m_LocalConnectLayers}])"
                + $" | 3 Abstand passt: {(torAbstand ? "JA" : "NEIN")} "
                + $"(Rand {rand:F2} m <= Suchweite {suchweite:F2} m)"
                + $" | 4 Hoehe passt: {(torHoehe ? "JA" : "NEIN")} "
                + $"(Unterschied {hoehendifferenz:F2} m gegen Bereich "
                + $"{lc.m_HeightRange.min:F2}..{lc.m_HeightRange.max:F2})"
                + $" | Flags [{lc.m_Flags}]");

            if (!alleTore)
            {
                Mod.log.Warn($"PLT-Versorgung: {a.Leitung} wird NICHT wieder "
                    + "angeschlossen - mindestens eine der vier Bedingungen "
                    + "ist nicht erfuellt. Wir bauen keine Verbindung, die CS2 "
                    + "selbst ablehnen wuerde.");
                return false;
            }

            /*
             * BEIDE RICHTUNGEN, SONST HAELT ES NICHT.
             *
             * `ConnectedNode` an der Kante ist die fuehrende Seite - daraus
             * baut `ReferencesSystem` alles Weitere. `ConnectedEdge` am Knoten
             * traegt es normalerweise selbst nach; wir schreiben es trotzdem
             * mit, damit die Verbindung schon im selben Frame vollstaendig ist
             * und die Versorgungsgraphen sie sofort sehen.
             */
            var kantenPuffer = EntityManager.GetBuffer<ConnectedNode>(beste);
            var schonDa = false;
            for (var i = 0; i < kantenPuffer.Length; i++)
                if (kantenPuffer[i].m_Node == knoten) { schonDa = true; break; }
            if (!schonDa)
                kantenPuffer.Add(new ConnectedNode(knoten, besteLage));

            if (EntityManager.HasBuffer<ConnectedEdge>(knoten))
            {
                var knotenPuffer = EntityManager
                    .GetBuffer<ConnectedEdge>(knoten);
                var schonDa2 = false;
                for (var i = 0; i < knotenPuffer.Length; i++)
                    if (knotenPuffer[i].m_Edge == beste)
                    { schonDa2 = true; break; }
                if (!schonDa2) knotenPuffer.Add(new ConnectedEdge(beste));
            }

            /*
             * DIE KANTE JA, DEN LEITUNGSKNOTEN NICHT. DAS IST DER UNTERSCHIED.
             *
             * Der Lauf am 2026-09-05 um 13:44 hat den Eintrag nachweislich
             * geschrieben ("#48550 wieder an Kante #63897 eingetragen") und
             * eine Sekunde spaeter stand wieder "0 von 2". Etwas hat ihn also
             * sofort entfernt - und das war unser eigenes `Updated` auf dem
             * Leitungsknoten.
             *
             * `UpdateNodeConnections` in `Game.Tools.GenerateEdgesSystem`
             * baut den `ConnectedNode`-Puffer einer aktualisierten Kante NEU
             * auf. Was es aus dem alten Puffer uebernimmt, entscheidet diese
             * Zeile:
             *
             *     if (!m_NodeMap.ContainsKey(new NodeMapKey(Entity.Null,
             *             position, isPermanent: true, isEditor)))
             *         nodes.Add(elem);
             *
             * In `m_NodeMap` steht JEDER Knoten, der `Updated` traegt. Wer
             * dort steht, gilt als anderswo neu erzeugt und wird aus dem alten
             * Puffer NICHT uebernommen. Indem wir den Leitungsknoten
             * angestupst haben, haben wir ihn selbst in diese Liste gesetzt -
             * und damit unseren eigenen Eintrag zum Wegwerfen freigegeben.
             *
             * Ohne das `Updated` am Knoten bleibt er aus der Liste, und der
             * Eintrag ueberlebt den Neuaufbau. Die KANTE bekommt ihr `Updated`
             * weiterhin, damit Komposition und Versorgungsgraphen den
             * Anschluss auswerten.
             */
            if (!EntityManager.HasComponent<Updated>(beste))
                EntityManager.AddComponent<Updated>(beste);

            // Wir HABEN geschrieben - also gilt der Knoten ab jetzt als
            // verbunden. Verschwindet er danach, meldet die Nachkontrolle
            // einen Verlust statt "war nie da". Genau das hat am 13:44
            // gefehlt.
            _versorgungJeVerbunden.Add(knoten);

            VerbindeVersorgungsgraph(beste, knoten, leitungsprefab, a.Leitung);

            Mod.log.Info($"PLT-Versorgung: {a.Leitung} #{knoten.Index} wieder "
                + $"an Kante #{beste.Index} '{strassenname}' bei Lage "
                + $"{besteLage:F3} eingetragen"
                + (schonDa ? " (stand dort bereits)" : "") + ".");
            return true;
        }

        /**
         * DIE NACHBARSCHAFT IST NICHT DIE LEITUNG.
         *
         * Am 2026-09-05 um 17:54 hielt der Anschluss zum ersten Mal
         * ("2 von 2 ... haengen wieder an einer neuen Strasse"), und der
         * Nutzer meldete trotzdem weiter fehlenden Strom und fehlendes Wasser.
         * Das ist kein Widerspruch: `ConnectedNode` und `ConnectedEdge` sind
         * nur die geometrische Nachbarschaft. Wer wirklich leitet, steht in
         * einem zweiten, eigenen Netz.
         *
         * Belegt in `Game.Simulation.ElectricityEdgeGraphSystem` (Wasser
         * gleichlautend in `WaterPipeEdgeGraphSystem`):
         *
         *   - Seine Abfrage ist `ElectricityConnection + Edge + PrefabRef +
         *     Created`, ohne `Temp`. Sie laeuft also NUR in dem Frame, in dem
         *     eine Kante ENTSTEHT - nicht, wenn sie sich spaeter aendert.
         *   - `CreateEdgeMiddleNodeConnections` geht dabei den
         *     `ConnectedNode`-Puffer der Kante durch und legt fuer jeden
         *     angehaengten Leitungsknoten eine FLUSSKANTE an.
         *
         * Unsere neuen Strassen entstehen beim Uebernehmen. In genau diesem
         * Frame ist ihr `ConnectedNode`-Puffer noch leer - wir tragen den
         * Anschluss erst ein paar Frames spaeter ein, wenn Lot und Traeger
         * dauerhaft sind. Die Flusskante wurde deshalb nie gebaut: Nachbarn
         * ja, Leitung nein.
         *
         * Also wird sie hier gebaut, mit CS2s eigenem Werkzeug
         * (`ElectricityGraphUtils` / `WaterPipeGraphUtils`), zwischen genau
         * denselben zwei Flussknoten, die `CreateEdgeMiddleNodeConnections`
         * verbunden haette, und mit den Kennwerten desselben Leitungsprefabs.
         *
         * Fehlt der Strassenkante ihr Flussknoten, dann fuehrt unsere Strasse
         * diese Versorgungsart ueberhaupt nicht - ein anderer Mangel, und
         * einer, den diese Meldung benennt statt ihn zu verschlucken.
         */
        private void VerbindeVersorgungsgraph(Entity kante, Entity knoten,
            Entity leitungsprefab, string leitung)
        {
            if (EntityManager.HasComponent<ElectricityConnectionData>(
                    leitungsprefab))
            {
                if (!EntityManager.HasComponent<
                        Game.Simulation.ElectricityNodeConnection>(kante))
                    Mod.log.Warn($"PLT-Versorgungsgraph: unsere Kante "
                        + $"#{kante.Index} hat keinen Stromflussknoten - sie "
                        + "fuehrt gar keinen Strom. Der Anschluss von "
                        + $"{leitung} kann nichts leiten.");
                else if (!EntityManager.HasComponent<
                        Game.Simulation.ElectricityNodeConnection>(knoten))
                    Mod.log.Warn($"PLT-Versorgungsgraph: {leitung} "
                        + $"#{knoten.Index} hat keinen Stromflussknoten.");
                else
                {
                    var kantenknoten = EntityManager.GetComponentData<
                        Game.Simulation.ElectricityNodeConnection>(kante)
                        .m_ElectricityNode;
                    var leitungsknoten = EntityManager.GetComponentData<
                        Game.Simulation.ElectricityNodeConnection>(knoten)
                        .m_ElectricityNode;
                    if (VersorgungsflussBereit(leitungsknoten, kantenknoten, "Strom")
                        && StromflusskanteFehlt(leitungsknoten, kantenknoten))
                    {
                        var daten = EntityManager.GetComponentData<
                            ElectricityConnectionData>(leitungsprefab);
                        var fluss = World.GetOrCreateSystemManaged<
                            Game.Simulation.ElectricityFlowSystem>();
                        Game.Simulation.ElectricityGraphUtils.CreateFlowEdge(
                            EntityManager, fluss.edgeArchetype,
                            leitungsknoten, kantenknoten,
                            daten.m_Direction, daten.m_Capacity);
                        Mod.log.Info($"PLT-Versorgungsgraph: Stromflusskante "
                            + $"fuer {leitung} #{knoten.Index} an Kante "
                            + $"#{kante.Index} angelegt (Kapazitaet "
                            + $"{daten.m_Capacity}, Richtung "
                            + $"{daten.m_Direction}).");
                    }
                }
            }

            if (!EntityManager.HasComponent<WaterPipeConnectionData>(
                    leitungsprefab)) return;
            if (!EntityManager.HasComponent<
                    Game.Simulation.WaterPipeNodeConnection>(kante))
            {
                Mod.log.Warn($"PLT-Versorgungsgraph: unsere Kante "
                    + $"#{kante.Index} hat keinen Wasserflussknoten - sie "
                    + $"fuehrt gar kein Wasser. Der Anschluss von {leitung} "
                    + "kann nichts leiten.");
                return;
            }
            if (!EntityManager.HasComponent<
                    Game.Simulation.WaterPipeNodeConnection>(knoten))
            {
                Mod.log.Warn($"PLT-Versorgungsgraph: {leitung} "
                    + $"#{knoten.Index} hat keinen Wasserflussknoten.");
                return;
            }
            var wKantenknoten = EntityManager.GetComponentData<
                Game.Simulation.WaterPipeNodeConnection>(kante)
                .m_WaterPipeNode;
            var wLeitungsknoten = EntityManager.GetComponentData<
                Game.Simulation.WaterPipeNodeConnection>(knoten)
                .m_WaterPipeNode;
            if (!VersorgungsflussBereit(wLeitungsknoten, wKantenknoten, "Wasser")) return;
            if (!WasserflusskanteFehlt(wLeitungsknoten, wKantenknoten)) return;
            var wDaten = EntityManager
                .GetComponentData<WaterPipeConnectionData>(leitungsprefab);
            var wFluss = World.GetOrCreateSystemManaged<
                Game.Simulation.WaterPipeFlowSystem>();
            Game.Simulation.WaterPipeGraphUtils.CreateFlowEdge(
                EntityManager, wFluss.edgeArchetype,
                wLeitungsknoten, wKantenknoten,
                wDaten.m_FreshCapacity, wDaten.m_SewageCapacity);
            Mod.log.Info($"PLT-Versorgungsgraph: Wasserflusskante fuer "
                + $"{leitung} #{knoten.Index} an Kante #{kante.Index} angelegt "
                + $"(frisch {wDaten.m_FreshCapacity}, Abwasser "
                + $"{wDaten.m_SewageCapacity}).");
        }

        // Schutzversion nach dem nativen Absturz vom 2026-09-06:
        // kein spaetes Updated an erhaltenen Leitungsknoten. Der Zustand
        // wird dauerhaft protokolliert und weitere 30 Frames beobachtet.
        private void FrischeVersorgungswarnungAuf()
        {
            _versorgungWarnungAufgefrischt = true;
            ProtokolliereVersorgungsschutz("SYMBOL-UPDATE-UNTERDRUECKT");
            _versorgungPruefFrame = UnityEngine.Time.frameCount;
            Mod.log.Warn("PLT-Versorgungsschutz: spaete Symbolaktualisierung "
                + "vorsorglich ausgelassen; KEIN Updated durch PLT gesetzt. "
                + "Warnsymbol kann veraltet bleiben. Zweite Beobachtung laeuft.");
            _uiSystem?.SetStatus(ParkingLotTexte.T(
                "Versorgungsschutz aktiv: Anschluss-Symbol wird vorerst nicht aktualisiert.",
                "Utility safeguard active: connection icon refresh temporarily skipped."));
        }

        /** Gibt es zwischen den beiden Flussknoten noch KEINE Stromkante? */
        private bool StromflusskanteFehlt(Entity a, Entity b)
        {
            if (a == Entity.Null || b == Entity.Null) return false;
            if (!EntityManager.HasBuffer<Game.Simulation.ConnectedFlowEdge>(a))
                return false;
            var kanten = EntityManager
                .GetBuffer<Game.Simulation.ConnectedFlowEdge>(a, true);
            for (var i = 0; i < kanten.Length; i++)
            {
                if (!EntityManager.HasComponent<
                        Game.Simulation.ElectricityFlowEdge>(kanten[i].m_Edge))
                    continue;
                var f = EntityManager.GetComponentData<
                    Game.Simulation.ElectricityFlowEdge>(kanten[i].m_Edge);
                if ((f.m_Start == a && f.m_End == b)
                    || (f.m_Start == b && f.m_End == a)) return false;
            }
            return true;
        }

        /** Dasselbe fuer Wasser und Abwasser. */
        private bool WasserflusskanteFehlt(Entity a, Entity b)
        {
            if (a == Entity.Null || b == Entity.Null) return false;
            if (!EntityManager.HasBuffer<Game.Simulation.ConnectedFlowEdge>(a))
                return false;
            var kanten = EntityManager
                .GetBuffer<Game.Simulation.ConnectedFlowEdge>(a, true);
            for (var i = 0; i < kanten.Length; i++)
            {
                if (!EntityManager.HasComponent<
                        Game.Simulation.WaterPipeEdge>(kanten[i].m_Edge))
                    continue;
                var f = EntityManager.GetComponentData<
                    Game.Simulation.WaterPipeEdge>(kanten[i].m_Edge);
                if ((f.m_Start == a && f.m_End == b)
                    || (f.m_Start == b && f.m_End == a)) return false;
            }
            return true;
        }

        /**
         * Hat es geklappt?
         *
         * Gemessen wird die WIRKUNG, nicht die Absicht: steht eine unserer
         * neuen Kanten wieder im `ConnectedEdge`-Puffer des Leitungsknotens?
         * Eine Pruefung, die nur meldet "wir haben Updated gesetzt", wuerde
         * ein Nichts-Tun niemals bemerken.
         */
        private void PruefeVersorgungswiederherstellung()
        {
            if (_versorgungKnotenPruefung.Count == 0) return;
            /*
             * AB DEM ERSTEN FRAME, NICHT AB DEM ZWEITEN.
             *
             * Vorher begann die Beobachtung bei +2. Der Eintrag vom 13:44 war
             * da schon weg, und das sah im Log aus wie "ist nie entstanden" -
             * dabei war er entstanden und sofort wieder entfernt worden. Genau
             * die Unterscheidung, fuer die diese Pruefung gebaut ist, ist ihr
             * dadurch entgangen.
             */
            var vergangen = UnityEngine.Time.frameCount - _versorgungPruefFrame;
            if (vergangen < 1) return;
            if (vergangen == 1 || vergangen == 8)
                ProtokolliereVersorgungsschutz("ZWISCHENSTAND-Frame-" + vergangen);

            var verbunden = 0;
            var gesamt = _versorgungKnotenPruefung.Count;
            for (var i = 0; i < gesamt; i++)
            {
                var n = _versorgungKnotenPruefung[i];
                if (n == Entity.Null || !EntityManager.Exists(n)) continue;
                var jetzt = HaengtAnUnsererKante(n);
                if (jetzt) verbunden++;

                // Der Wechsel ist die Auskunft, nicht der Endstand.
                if (jetzt && _versorgungJeVerbunden.Add(n))
                    Mod.log.Info($"PLT-Versorgung: Leitungsknoten #{n.Index} "
                        + $"haengt seit Frame +{vergangen} wieder an einer "
                        + "unserer neuen Kanten.");
                else if (!jetzt && _versorgungJeVerbunden.Remove(n))
                    Mod.log.Warn($"PLT-Versorgung: Leitungsknoten #{n.Index} "
                        + $"hing bereits an einer neuen Kante und in Frame "
                        + $"+{vergangen} NICHT MEHR. Der Anschluss entsteht "
                        + "also und wird danach wieder weggenommen.");
            }

            /*
             * NICHT BEIM ERSTEN ERFOLG AUFHOEREN.
             *
             * Vorher wurde "haengen wieder an einer neuen Strasse" gemeldet
             * und sofort abgeraeumt. Am 2026-09-05 um 17:54 stand diese Zeile
             * "nach 1 Frames" im Log - und der Nutzer hatte trotzdem keinen
             * Strom. Ein Erfolg nach einem Frame beweist nur, dass es einen
             * Frame lang gehalten hat.
             *
             * Beobachtet wird deshalb bis zum Ende der Frist. Ein Verlust
             * dazwischen faellt als eigene Zeile auf.
             */
            if (verbunden >= gesamt && vergangen < VersorgungPruefFrames)
                return;
            if (vergangen < VersorgungPruefFrames) return;
            ProtokolliereVersorgungsschutz(_versorgungWarnungAufgefrischt
                ? "BEOBACHTUNG-NACH-SCHUTZ" : "BEOBACHTUNG-VOR-SCHUTZ");

            if (verbunden >= gesamt)
            {
                Mod.log.Info($"PLT-Versorgung: {verbunden} von {gesamt} "
                    + "Leitungsanschluessen haengen nach "
                    + $"{vergangen} Frames stabil an einer neuen Strasse.");
                for (var i = 0; i < gesamt; i++)
                    MeldeVersorgungsnachbarn(_versorgungKnotenPruefung[i]);
                if (!_versorgungWarnungAufgefrischt)
                {
                    FrischeVersorgungswarnungAuf();
                    return;
                }
                _versorgungKnotenPruefung.Clear();
                _versorgungNeueKanten.Clear();
                _versorgungJeVerbunden.Clear();
                return;
            }

            Mod.log.Warn($"PLT-Versorgung: nur {verbunden} von {gesamt} "
                + $"Leitungsanschluessen wurden nach {vergangen} Frames wieder "
                + "angeknuepft. Strom oder Wasser fehlen dem Parkplatz "
                + "dadurch weiterhin.");
            for (var i = 0; i < gesamt; i++)
                MeldeVersorgungsnachbarn(_versorgungKnotenPruefung[i]);
            _uiSystem?.SetStatus(ParkingLotTexte.T(
                $"{verbunden} von {gesamt} Leitungsanschlüssen wiederhergestellt.",
                $"{verbunden} of {gesamt} utility connections restored."));
            _versorgungKnotenPruefung.Clear();
            _versorgungNeueKanten.Clear();
            _versorgungJeVerbunden.Clear();
        }

        /**
         * Steht der Knoten in einer unserer neuen Kanten?
         *
         * Gefragt wird in BEIDE Richtungen. `UpdateNodeConnections` schreibt
         * den Anschluss in den `ConnectedNode`-Puffer der KANTE; die
         * Gegenrichtung im `ConnectedEdge`-Puffer des Knotens traegt erst
         * `ReferencesSystem` einen Schritt spaeter nach. Wer nur eine Seite
         * abfragt, haelt eine Verzoegerung fuer einen Fehlschlag.
         */
        private bool HaengtAnUnsererKante(Entity knoten)
        {
            if (EntityManager.HasBuffer<ConnectedEdge>(knoten))
            {
                var kanten = EntityManager
                    .GetBuffer<ConnectedEdge>(knoten, true);
                for (var k = 0; k < kanten.Length; k++)
                    if (_versorgungNeueKanten.Contains(kanten[k].m_Edge))
                        return true;
            }
            foreach (var kante in _versorgungNeueKanten)
            {
                if (!EntityManager.Exists(kante)) continue;
                if (!EntityManager.HasBuffer<ConnectedNode>(kante)) continue;
                var knotenPuffer = EntityManager
                    .GetBuffer<ConnectedNode>(kante, true);
                for (var k = 0; k < knotenPuffer.Length; k++)
                    if (knotenPuffer[k].m_Node == knoten) return true;
            }
            return false;
        }

        /**
         * An welchen Strassen haengt der Leitungsknoten sonst noch?
         *
         * `ChooseBest` am Leitungsprefab heisst: CS2 behaelt genau EINE
         * Strasse je Knoten, naemlich die mit dem kleinsten Randabstand. Ist
         * die Hauptstrasse naeher als unsere, waere ein Anschluss an uns
         * regelkonform wieder entfernt worden - und dann hilft kein weiteres
         * Anstupsen, sondern nur ein anderer Anschlusspunkt.
         *
         * Deshalb steht hier der Randabstand JEDER Nachbarkante, gerechnet
         * wie CS2 ihn rechnet: Mittellinienabstand minus halbe Strassenbreite.
         */
        private void MeldeVersorgungsnachbarn(Entity knoten)
        {
            if (knoten == Entity.Null || !EntityManager.Exists(knoten)) return;
            if (!EntityManager.HasComponent<Game.Net.Node>(knoten)) return;
            if (!EntityManager.HasBuffer<ConnectedEdge>(knoten)) return;
            var ort = EntityManager
                .GetComponentData<Game.Net.Node>(knoten).m_Position;
            var kanten = EntityManager.GetBuffer<ConnectedEdge>(knoten, true);
            var text = new List<string>();
            for (var k = 0; k < kanten.Length; k++)
            {
                var kante = kanten[k].m_Edge;
                if (!EntityManager.Exists(kante)) continue;
                var name = PrefabNameOf(kante) ?? "ohne Prefabnamen";
                var unser = _versorgungNeueKanten.Contains(kante)
                    ? " [UNSERE]" : "";
                if (!EntityManager.HasComponent<Game.Net.Curve>(kante))
                {
                    text.Add($"#{kante.Index} '{name}'{unser}");
                    continue;
                }
                var bogen = EntityManager
                    .GetComponentData<Game.Net.Curve>(kante).m_Bezier;
                var abstand = MathUtils.Distance(bogen.xz, ort.xz, out _);
                var breite = 0f;
                if (EntityManager.HasComponent<PrefabRef>(kante))
                {
                    var p = EntityManager
                        .GetComponentData<PrefabRef>(kante).m_Prefab;
                    if (EntityManager.HasComponent<NetGeometryData>(p))
                        breite = EntityManager
                            .GetComponentData<NetGeometryData>(p)
                            .m_DefaultWidth;
                }
                text.Add($"#{kante.Index} '{name}' Rand "
                    + $"{abstand - breite * 0.5f:F2} m{unser}");
            }
            Mod.log.Info($"PLT-Versorgungsnachbarn #{knoten.Index}: "
                + (text.Count == 0
                    ? "keine einzige Kante"
                    : string.Join(" | ", text)));
        }
    }
}
