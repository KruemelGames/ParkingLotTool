using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * LOESCHT DIE AUTOMATISCH GEBAUTEN LEITUNGEN - UND ZWAR FRUEH GENUG.
     *
     * Das hier ist kein zweiter Aufraeumer neben `ParkingLotCleanupSystem`.
     * Es ist derselbe Vorgang, nur in einer anderen Phase, und die Phase ist
     * der ganze Punkt.
     *
     * DER ABSTURZ, DER DAZU GEFUEHRT HAT. Monatelang stuerzte CS2 beim
     * Weggbaggern und beim Bearbeiten eines Parkplatzes ab, sobald die
     * automatische Versorgung an war - nativ, ohne managed Stacktrace. Mit
     * abgeschalteter Versorgung lief beides sauber; der ERSTE Bau stuerzte nie
     * ab. Alles Messwerte des Nutzers vom 2026-09-14.
     *
     * WARUM DIE PHASE ENTSCHEIDET. `ParkingLotCleanupSystem` laeuft in
     * `Modification3` und hat die Leitungskanten dort als `Deleted` markiert.
     * Nun sieht die Aufzaehlung `SystemUpdatePhase` so aus, als kaeme
     * `MainLoop` vor den Modification-Phasen - das ist aber nur die
     * Reihenfolge der Aufzaehlung, nicht die der Ausfuehrung:
     *
     *   Game.Common.SystemOrder, Zeile 60:
     *     UpdateAt<ModificationSystem>(SystemUpdatePhase.MainLoop)
     *
     *   Game.Common.ModificationSystem.OnUpdate:
     *     m_UpdateSystem.Update(Modification1);
     *     ... Update(Modification2B); Update(Modification3); ...
     *
     * Die Modification-Phasen laufen also INNERHALB von `MainLoop`. Und:
     *
     *   Zeile 50: UpdateAfter<PrepareCleanUpSystem>(MainLoop)
     *
     * `PrepareCleanUpSystem` sammelt alles mit `Deleted` ein, NACHDEM MainLoop
     * durch ist - also nach unserem Markieren -, und `CleanUpSystem` (Phase
     * `Cleanup`, die letzte des Frames) zerstoert genau diese Liste.
     *
     * DIE FOLGE: `Game.Net.ReferencesSystem` laeuft in `Modification2B`, also
     * VOR `Modification3`. Es bekommt unsere geloeschte Kante nie zu sehen,
     * und im naechsten Frame gibt es die Entity nicht mehr. Sein
     * `UpdateEdgeReferencesJob` haette die Kante aus dem
     * `ConnectedEdge`-Puffer beider Endknoten genommen - es kam nie dazu.
     *
     * GEMESSEN, NICHT HERGELEITET. Eine eigens eingebaute Nachschau meldete
     * beim naechsten Bulldozern woertlich:
     *
     *     PLT-Leitungsnachschau BEFUND nach 1 Frames: 4 von 4 noch lebenden
     *     Knoten tragen 4 Verweis(e) auf Kanten, die es nicht mehr gibt.
     *
     * Und ueber genau diese Verweise laeuft `UpdateNodeReferencesJob`, wenn
     * CS2 den verwaisten Knoten spaeter abraeumt:
     *
     *     Entity edge = dynamicBuffer[j].m_Edge;
     *     CollectionUtils.RemoveValue(m_Nodes[edge], ...);
     *
     * `m_Nodes[edge]` ist ein UNGEPRUEFTER Zugriff auf eine Entity, die es
     * nicht mehr gibt, in einem Burst-Job. Das ist der native Absturz.
     *
     * Es erklaert auch den 2026-09-10 rueckwirkend: damals haben wir
     * zusaetzlich den Knoten markiert, und dann lief der Knotenzweig sofort
     * ueber diesen Puffer - der Absturz kam unmittelbar statt kurz darauf.
     * Das Weglassen hat ihn verschoben, nicht beseitigt.
     *
     * DESHALB DIESES SYSTEM, UND ZWAR IN `Modification2`. Das liegt vor
     * `Modification2B`. Damit steht das `Deleted` auf der Kante, wenn
     * `ReferencesSystem` im selben Frame darueber laeuft, und beide
     * Knotenpuffer werden sauber abgeraeumt, bevor die Entity verschwindet.
     *
     * WARUM NICHT EINFACH EINE FRUEHERE BARRIERE AUS `Modification3` HERAUS:
     * versucht und verworfen. CS2s Barrieren sind `SafeCommandBufferSystem`;
     * ausserhalb ihres eigenen Fensters wirft `CreateCommandBuffer()` die
     * Ausnahme *"Trying to create EntityCommandBuffer when it's not
     * allowed!"*. Deshalb wird hier direkt ueber den `EntityManager`
     * markiert - im eigenen Durchlauf, auf dem Hauptthread, sofort wirksam.
     *
     * DIE KNOTEN RUEHREN WIR WEITERHIN NICHT AN. Sie selbst zu markieren war
     * die Absturzursache vom 2026-09-10; das bleibt richtig und steht
     * ausfuehrlich in der Geschichte dieses Projekts.
     *
     * WAS BEIM LOESCHEN VON SELBST GESCHIEHT - im Dekompilat nachgelesen und
     * hier aufgehoben, weil es die Grundlage der ganzen Konstruktion ist:
     *
     *   - `Game.Simulation.ElectricityGraphDeleteSystem` (Abfrage bei 137)
     *     greift bei JEDER Entity mit `Deleted` ohne `Temp`, die eine
     *     `ElectricityNodeConnection` traegt, und ruft
     *     `ElectricityGraphUtils.DeleteFlowNode` (119): der Flussknoten und
     *     ALLE an ihm haengenden Flusskanten bekommen ebenfalls `Deleted`.
     *     Fuer Wasser und Abwasser gilt dasselbe ueber
     *     `WaterPipeGraphDeleteSystem`.
     *
     *     Unsere Leitungskante traegt diese Komponente - `ElectricityEdge
     *     GraphSystem` setzt sie beim Bau auf die Kante. Der Versorgungsgraph
     *     raeumt sich also selbst ab; nichts davon muss die Mod anfassen.
     *
     * BETROFFEN SIND NUR UNSERE EIGENEN LEITUNGEN, erkennbar an
     * `ParkingLotVersorgungsleitung`. Was der Nutzer selbst gezogen hat,
     * traegt diese Markierung nicht und wird nie angefasst. Geloeschte
     * Vanilla-Objekte kann er nicht wiederherstellen; hier falsch zu liegen
     * waere teurer als jeder stehengebliebene Rest.
     */
    public sealed partial class ParkingLotLeitungsabrissSystem : GameSystemBase
    {
        private EntityQuery _leitungen;

        /*
         * NACHSCHAU STATT THEORIE.
         *
         * Am 2026-09-14 habe ich dreimal eine Ursache hergeleitet und dreimal
         * danebengelegen. Diese Nachschau hat den Fehler dann in einem
         * einzigen Lauf gefunden - sie bleibt, weil sie jetzt umgekehrt
         * beweist, dass er weg ist.
         *
         * Nach dem Loeschen schaut sie 120 Frames lang in die Nachbarlisten
         * aller beruehrten Knoten: steht dort eine Kante, die es nicht mehr
         * gibt? Vor dem Umbau lautete die Antwort *"4 von 4 noch lebenden
         * Knoten tragen 4 Verweis(e) auf Kanten, die es nicht mehr gibt"*.
         * Danach muss im Log die andere Zeile stehen - "KEIN toter Verweis".
         *
         * Steht sie eines Tages wieder auf BEFUND, ist an der Phase dieses
         * Systems gedreht worden.
         */
        private readonly System.Collections.Generic.List<Entity>
            _leitungsknoten = new System.Collections.Generic.List<Entity>();
        private int _leitungsknotenFrames;
        private bool _leitungsknotenGemeldet;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            /*
             * `Temp` bleibt aussen vor: eine Bulldozer-VORSCHAU traegt
             * `Deleted + Temp`, und die darf nie etwas wirklich loeschen.
             * Derselbe Fallstrick wie im Aufraeumer.
             */
            _leitungen = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotVersorgungsleitung>(),
                    ComponentType.ReadOnly<Edge>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            /*
             * KEIN `RequireForUpdate`. Die Abfrage schliesst `Deleted` aus,
             * also waere sie leer, sobald die letzte Leitung markiert ist -
             * und dann liefe die Nachschau nie, die genau danach melden soll.
             * Der Durchlauf kostet ohne Treffer nichts.
             */
        }

        [Preserve]
        protected override void OnUpdate()
        {
            RaeumeVerwaisteKnoten();
            PruefeLeitungsknoten();
            if (_leitungen.IsEmptyIgnoreFilter) return;

            using var kanten = _leitungen.ToEntityArray(Allocator.Temp);
            var gemerkt = 0;
            var stummel = 0;
            for (var i = 0; i < kanten.Length; i++)
            {
                var kante = kanten[i];
                var zuordnung = EntityManager
                    .GetComponentData<ParkingLotVersorgungsleitung>(kante);
                if (!ParkplatzIstFort(zuordnung)) continue;
                MerkeLeitungsknoten(kante);
                stummel += LoescheAnschlussstummel(kante);
                EntityManager.AddComponent<Deleted>(kante);
                gemerkt++;
            }
            if (gemerkt == 0) return;

            Mod.log.Info("PLT-Leitungsabriss: " + gemerkt
                + " eigene Leitungskante(n) und " + stummel
                + " senkrechte(s) Anschlussstueck(e) in Modification2 "
                + "geloescht - vor Game.Net.ReferencesSystem "
                + "(Modification2B), damit es die Kanten noch sieht und aus "
                + "den Puffern der Endknoten nimmt.");
            ParkingLotSchrittmarke.Setze(
                "Leitungsabriss: " + gemerkt + " Leitungskante(n) geloescht");
        }

        /**
         * Gehoert die Leitung zu einem Parkplatz, den es nicht mehr gibt?
         *
         * Gefragt wird nach dem Parkplatz UND nach dem Traeger. Beim
         * Bearbeiten faellt zuerst der alte Traeger; beim Weggbaggern der
         * Parkplatz. Eine Leitung, deren beide Bezugspunkte noch stehen,
         * bleibt unangetastet - und was der Nutzer selbst gezogen hat, traegt
         * diese Zuordnung ohnehin nicht.
         */
        private bool ParkplatzIstFort(ParkingLotVersorgungsleitung zuordnung)
        {
            if (Lebt(zuordnung.Lot)) return false;
            if (Lebt(zuordnung.Carrier)) return false;
            // Beides Entity.Null waere eine Zuordnung ohne Bezug. Die ruehren
            // wir nicht an - lieber eine stehengebliebene Leitung als eine
            // geloeschte, die jemandem gehoert.
            return zuordnung.Lot != Entity.Null
                   || zuordnung.Carrier != Entity.Null;
        }

        private bool Lebt(Entity e)
            => e != Entity.Null && EntityManager.Exists(e)
               && !EntityManager.HasComponent<Deleted>(e);

        /**
         * NIMMT DAS SENKRECHTE ANSCHLUSSSTUECK MIT.
         *
         * Der Nutzer am 2026-09-14, mit Bildbeleg aus der Wasseransicht:
         * *"derzeit ist das so ein Knubbel der einfach nur nach unten geht
         * unter den Strassenleitungen"* - und er hat recht mit dem Grundsatz
         * dahinter: *"das Problem ist ja aber dass du den Knoten erstellt
         * hast also solltest du ihn auch loeschen"*.
         *
         * WOHER DAS STUECK KOMMT. Unsere Leitung liegt auf -10 m
         * (`AutoVersorgungTiefe`), die Rohre der Stadt liegen viel hoeher.
         * Fuer den Anschluss baut CS2 deshalb ein kurzes SENKRECHTES Stueck
         * zwischen beiden Tiefen.
         *
         * WARUM WIR ES BISHER UEBERSEHEN HABEN. `AvMesseApply`
         * (ParkingLotAutoVersorgungMessung) sammelt unsere frisch gebauten
         * Kanten ueber `VersorgungskursPruefung.Abschnitt` - und das rechnet
         * in der DRAUFSICHT, nur mit X und Z. Ein senkrechtes Stueck hat dort
         * keine Laenge, Anfang und Ende liegen uebereinander. Es faellt durch
         * das Raster, bekommt nie die Markierung
         * `ParkingLotVersorgungsleitung` und bleibt beim Abriss stehen.
         *
         * Genau dieselbe Klasse Fehler wie schon zweimal in diesem Projekt:
         * in der Draufsicht gemessen, wo es um die Hoehe geht.
         *
         * WARUM DIE SCHRANKE HIER ENG IST. Geloescht wird nur eine Kante, die
         * ALLE vier Bedingungen erfuellt:
         *
         *   - sie haengt an einem Endknoten UNSERER Leitung,
         *   - sie ist nicht selbst eine markierte Leitung von uns,
         *   - sie hat in der Draufsicht so gut wie keine Laenge (< 1 m),
         *   - sie ueberwindet dabei einen Hoehenunterschied (> 0,5 m).
         *
         * Ein Rohr der Stadt kann das nie sein: das laeuft waagerecht und ist
         * viele Meter lang. Und was der Nutzer selbst gezogen hat, koennte
         * hoechstens dann getroffen werden, wenn er eigenhaendig ein fast
         * senkrechtes Stueck unter einen Meter an genau unseren Anschluss
         * gesetzt haette.
         *
         * Alles andere, was an unseren Knoten haengt, wird nur GEMELDET. Wenn
         * also weiter etwas stehen bleibt, steht im Log, was es ist.
         */
        private int LoescheAnschlussstummel(Entity unsere)
        {
            if (!EntityManager.HasComponent<Edge>(unsere)) return 0;
            var kante = EntityManager.GetComponentData<Edge>(unsere);
            var zahl = 0;
            zahl += StummelAn(kante.m_Start, unsere);
            zahl += StummelAn(kante.m_End, unsere);
            return zahl;
        }

        private int StummelAn(Entity knoten, Entity unsere)
        {
            if (knoten == Entity.Null || !EntityManager.Exists(knoten)) return 0;
            if (!EntityManager.HasBuffer<ConnectedEdge>(knoten)) return 0;

            var zahl = 0;
            var gesehen = new System.Collections.Generic.List<string>();
            foreach (var v in EntityManager.GetBuffer<ConnectedEdge>(knoten, true))
            {
                var e = v.m_Edge;
                if (e == unsere) continue;
                if (!EntityManager.Exists(e)) continue;
                if (EntityManager.HasComponent<Deleted>(e)) continue;
                if (EntityManager.HasComponent<Temp>(e)) continue;
                // Eine zweite eigene Leitung raeumt ihr eigener Durchlauf ab.
                if (EntityManager.HasComponent<ParkingLotVersorgungsleitung>(e))
                    continue;
                if (!EntityManager.HasComponent<Curve>(e)) continue;

                var b = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                var flach = math.distance(b.a.xz, b.d.xz);
                var hoch = math.abs(b.a.y - b.d.y);
                if (flach < 1f && hoch > 0.5f)
                {
                    EntityManager.AddComponent<Deleted>(e);
                    zahl++;
                    continue;
                }
                if (gesehen.Count < 3)
                    gesehen.Add($"#{e.Index} (waagerecht {flach:F2} m, "
                        + $"senkrecht {hoch:F2} m)");
            }

            if (gesehen.Count > 0)
                Mod.log.Info("PLT-Leitungsabriss: an Knoten #" + knoten.Index
                    + " bleibt stehen: " + string.Join(", ", gesehen)
                    + ". Das ist kein senkrechtes Anschlussstueck - "
                    + "vermutlich das Rohr der Stadt.");
            return zahl;
        }

        /**
         * NIMMT DIE KNOTEN MIT, DIE NACH DER LEITUNG UEBRIG BLEIBEN.
         *
         * Der alte Code hat darauf gesetzt, dass CS2 verwaiste Knoten selbst
         * abraeumt. GEMESSEN AM 2026-09-14, unmittelbar nach dem Fix der
         * Loeschphase:
         *
         *     PLT-Leitungsnachschau: nach 120 Frames KEIN toter Verweis an
         *     den 4 beruehrten Knoten (4 davon leben noch).
         *
         * Alle vier leben noch. Zwei davon sind die Rohrknoten der
         * Stadtstrasse und sollen leben - die anderen zwei sind unsere und
         * bleiben als Reste stehen. Der Nutzer sieht sie. Die Annahme war
         * also falsch.
         *
         * WARUM DAS JETZT GEHT UND AM 2026-09-10 DAS SPIEL UMGEBRACHT HAT.
         * Damals haben wir die Knoten mitmarkiert - aber in `Modification3`,
         * also NACH `Game.Net.ReferencesSystem` (`Modification2B`). Dann lief
         * dessen Knotenzweig ueber einen Puffer voller Kanten, die im selben
         * Frame verschwanden, und griff ungeprueft darauf zu. Es war nie
         * falsch, einen Knoten zu loeschen - es war die Phase. Dieses System
         * laeuft in `Modification2`, also davor.
         *
         * WAS ALS UNSERES GILT: ein Knoten aus unserer eigenen Leitung, an
         * dem KEINE Kante mehr haengt. Das ist eine harte Schranke, kein
         * Ermessen - ein Knoten der Stadtstrasse hat immer Kanten. Es kann
         * also nichts Fremdes treffen.
         *
         * Betrachtet werden ausserdem nur Knoten, die wir uns beim Loeschen
         * unserer eigenen Leitung gemerkt haben. Was der Nutzer selbst
         * gezogen hat, steht in dieser Liste nie.
         */
        private void RaeumeVerwaisteKnoten()
        {
            if (_leitungsknoten.Count == 0) return;
            var geraeumt = 0;
            foreach (var k in _leitungsknoten)
            {
                if (!EntityManager.Exists(k)) continue;
                if (EntityManager.HasComponent<Deleted>(k)) continue;
                if (EntityManager.HasComponent<Temp>(k)) continue;
                if (!TraegtNichtsMehr(k)) continue;
                EntityManager.AddComponent<Deleted>(k);
                geraeumt++;
            }
            if (geraeumt == 0) return;
            Mod.log.Info("PLT-Leitungsabriss: " + geraeumt + " Leitungs- bzw. "
                + "Anschlussknoten in Modification2 geloescht - Knoten, die "
                + "kein Ende einer Leitung mehr sind.");
            ParkingLotSchrittmarke.Setze(
                "Leitungsabriss: " + geraeumt + " Knoten geloescht");
        }

        /**
         * IST DIESER KNOTEN NOCH DAS ENDE IRGENDEINER LEITUNG?
         *
         * DIE ERSTE FASSUNG WAR ZU ENG. Sie loeschte nur Knoten OHNE jede
         * Kante. Damit blieb genau das stehen, was der Nutzer am 2026-09-14
         * im Bild gezeigt hat - *"so ein Knubbel der einfach nur nach unten
         * geht unter den Strassenleitungen"*.
         *
         * GEMESSEN, direkt nach jenem Versuch:
         *
         *     Ueberlebende: #48867 (1 Kanten), #48989 (1 Kanten)
         *     an Knoten #48867 bleibt stehen: #436904
         *         (waagerecht 168,00 m, senkrecht 0,15 m)
         *
         * Und die Zielknoten des Stadtrohrs hiessen in demselben Lauf
         * #424626 und #424628. Die beiden Ueberlebenden waren also NICHT
         * vorher da - CS2 hat sie angelegt, als unsere Leitung sich seitlich
         * an das Stadtrohr gehaengt hat (`AddNodesForLocalConnect`). Sie
         * haengen beide an derselben 168-Meter-Kante der Stadt und sind kein
         * Ende davon; sie sitzen nur mittendrin darauf.
         *
         * Nach dem Grundsatz des Nutzers - *"das Problem ist ja aber dass du
         * den Knoten erstellt hast also solltest du ihn auch loeschen"* -
         * gehoeren sie uns, also raeumen wir sie weg.
         *
         * DIE NEUE SCHRANKE IST TROTZDEM SICHER, und sie ist es aus einem
         * strukturellen Grund: geprueft wird, ob der Knoten bei irgendeiner
         * lebenden Kante als `m_Start` oder `m_End` eingetragen ist. Ist er
         * das nirgends, traegt er nichts. Ein Rohr der Stadt kann dadurch
         * nicht zerschnitten werden - dafuer muesste der Knoten ein Ende
         * sein, und genau dann bleibt er stehen.
         *
         * "Gar keine Kante" ist darin als Sonderfall enthalten.
         *
         * Betrachtet werden weiterhin nur Knoten, die wir uns beim Loeschen
         * UNSERER eigenen Leitung gemerkt haben.
         */
        private bool TraegtNichtsMehr(Entity knoten)
        {
            if (!EntityManager.HasBuffer<ConnectedEdge>(knoten)) return false;
            foreach (var v in EntityManager.GetBuffer<ConnectedEdge>(knoten, true))
            {
                var e = v.m_Edge;
                if (!EntityManager.Exists(e)) continue;
                if (EntityManager.HasComponent<Deleted>(e)) continue;
                if (!EntityManager.HasComponent<Edge>(e)) continue;
                var kante = EntityManager.GetComponentData<Edge>(e);
                if (kante.m_Start == knoten || kante.m_End == knoten)
                    return false;
            }
            return true;
        }

        /** Merkt beide Endknoten und alle Seitenanschluesse einer Kante. */
        private void MerkeLeitungsknoten(Entity kante)
        {
            _leitungsknotenFrames = 0;
            _leitungsknotenGemeldet = false;
            if (EntityManager.HasComponent<Edge>(kante))
            {
                var e = EntityManager.GetComponentData<Edge>(kante);
                Merke(e.m_Start);
                Merke(e.m_End);
            }
            if (!EntityManager.HasBuffer<ConnectedNode>(kante)) return;
            foreach (var n in EntityManager.GetBuffer<ConnectedNode>(kante, true))
                Merke(n.m_Node);

            void Merke(Entity k)
            {
                if (k != Entity.Null && !_leitungsknoten.Contains(k))
                    _leitungsknoten.Add(k);
            }
        }

        /**
         * Schaut den gemerkten Knoten in die Nachbarliste.
         *
         * Gesucht wird ein Eintrag, dessen Kante es nicht mehr gibt. Gemeldet
         * wird einmal, sobald der erste auftaucht, und ein Abschluss nach
         * 120 Frames - damit das Log nicht volllaeuft.
         */
        private void PruefeLeitungsknoten()
        {
            if (_leitungsknoten.Count == 0) return;
            _leitungsknotenFrames++;

            var leichen = 0;
            var betroffen = 0;
            var lebend = 0;
            var beispiele = new System.Collections.Generic.List<string>();
            foreach (var k in _leitungsknoten)
            {
                if (!EntityManager.Exists(k)) continue;
                lebend++;
                if (!EntityManager.HasBuffer<ConnectedEdge>(k)) continue;
                var tot = 0;
                foreach (var v in EntityManager.GetBuffer<ConnectedEdge>(k, true))
                    if (!EntityManager.Exists(v.m_Edge)) tot++;
                if (tot == 0) continue;
                leichen += tot;
                betroffen++;
                if (beispiele.Count < 4)
                    beispiele.Add($"Knoten #{k.Index}"
                        + (EntityManager.HasComponent<Deleted>(k)
                            ? " (Deleted)" : "")
                        + $": {tot} tote Kante(n)");
            }

            if (leichen > 0 && !_leitungsknotenGemeldet)
            {
                _leitungsknotenGemeldet = true;
                Mod.log.Warn("PLT-Leitungsnachschau BEFUND nach "
                    + _leitungsknotenFrames + " Frames: " + betroffen
                    + " von " + lebend + " noch lebenden Knoten tragen "
                    + leichen + " Verweis(e) auf Kanten, die es nicht mehr "
                    + "gibt. " + string.Join("; ", beispiele)
                    + ". Genau darueber laeuft ReferencesSystem ungeprueft, "
                    + "wenn CS2 den Knoten abraeumt.");
                ParkingLotSchrittmarke.Setze(
                    $"Leitungsnachschau: {leichen} tote Verweise an {betroffen} Knoten");
            }

            if (_leitungsknotenFrames < 120) return;
            if (!_leitungsknotenGemeldet)
            {
                /*
                 * Die Zahl der ueberlebenden Knoten ist kein Nebensatz.
                 * Genau sie hat am 2026-09-14 den naechsten Mangel sichtbar
                 * gemacht: kein toter Verweis, aber alle vier Knoten noch da.
                 * Deshalb steht jetzt dabei, WIE VIELE davon noch Kanten
                 * haben - nur die gehoeren der Stadtstrasse.
                 */
                var mitKanten = 0;
                foreach (var k in _leitungsknoten)
                {
                    if (!EntityManager.Exists(k)) continue;
                    if (!EntityManager.HasBuffer<ConnectedEdge>(k)) continue;
                    if (EntityManager.GetBuffer<ConnectedEdge>(k, true).Length > 0)
                        mitKanten++;
                }
                /*
                 * DIE UEBERLEBENDEN BEIM NAMEN NENNEN.
                 *
                 * Der Nutzer meldete am 2026-09-14, dass an der Strasse ein
                 * Knoten stehen bleibt. Ob das ein Knoten ist, den CS2 beim
                 * Anschluss neu ins Stadtrohr gesetzt hat, oder einer, der
                 * schon vorher da war, entscheidet alles Weitere - im ersten
                 * Fall muessten die beiden Rohrhaelften wieder zu einer Kante
                 * werden, im zweiten ist gar nichts zu tun.
                 *
                 * Die Nummer laesst sich mit der Zeile "ZIELKNOTEN" aus
                 * demselben Log vergleichen: steht sie dort, gab es den
                 * Knoten vorher schon.
                 */
                var namen = new System.Collections.Generic.List<string>();
                foreach (var k in _leitungsknoten)
                {
                    if (!EntityManager.Exists(k)) continue;
                    var n = EntityManager.HasBuffer<ConnectedEdge>(k)
                        ? EntityManager.GetBuffer<ConnectedEdge>(k, true).Length
                        : -1;
                    namen.Add("#" + k.Index + " (" + n + " Kanten)");
                }
                Mod.log.Info("PLT-Leitungsnachschau: nach 120 Frames KEIN "
                    + "toter Verweis an den " + _leitungsknoten.Count
                    + " beruehrten Knoten. Noch vorhanden: " + lebend
                    + ", davon " + mitKanten + " mit Kanten. "
                    + (lebend - mitKanten) + " ohne Kante. Ueberlebende: "
                    + string.Join(", ", namen)
                    + ". Vergleiche mit ZIELKNOTEN weiter oben: steht die "
                    + "Nummer dort, gab es den Knoten schon vor dem Bau.");
            }
            _leitungsknoten.Clear();
            _leitungsknotenFrames = 0;
            _leitungsknotenGemeldet = false;
        }

        [Preserve]
        public ParkingLotLeitungsabrissSystem() { }
    }
}
