using System.Collections.Generic;
using System.Linq;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * ZAEHLT NACH, WAS CS2 AUS UNSEREM ZONING-RING GEMACHT HAT.
     *
     * Befund des Nutzers am 2026-09-02: *"Panel zeigt 6x6, Flaeche zeigt
     * 6x6 und Ingame kommen 5x6."* Panel und Geometrie sind sich also
     * einig - die Kachel geht in CS2s Blockbildung verloren, nicht bei uns.
     *
     * Damit ist Raten sinnlos: gebraucht wird, was `BlockSystem` wirklich
     * angelegt hat. Diese Messung laeuft deshalb automatisch nach jedem Bau
     * mit Zoning-Strasse, ohne dass jemand einen Knopf druecken muss.
     *
     * WARUM VERZOEGERT: `BlockSystem` laeuft erst, nachdem unsere Kanten
     * dauerhaft geworden sind, und `ValidArea` fuellt `ValidAreaSystem`
     * noch spaeter. Sofort nach dem Bau gemessen saehe man leere Puffer und
     * wuerde daraus den falschen Schluss ziehen.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Frames zwischen Bau und Messung. Die Sonde hat mit derselben
         * Groessenordnung stabile Werte geliefert; kuerzer misst man die
         * Blockbildung beim Entstehen statt fertig.
         */
        private const int ZoningBlockWartezeit = 24;

        /**
         * ZWEI STUFEN, UND DIE REIHENFOLGE IST DER SINN DER SACHE.
         *
         * Zuerst werden die Seitenschalter gesetzt (`Upgraded` +
         * `Updated`), dann gemessen. Andersherum protokollierte die Messung
         * den Zustand VOR der eigenen Aenderung, und man haette aus einer
         * richtigen Zahl den falschen Schluss gezogen.
         */
        private const int ZoningSeitenWartezeit = 12;

        private int _zoningBlockFrames;
        private int _zoningSeitenFrames;
        private Entity _zoningBlockTraeger = Entity.Null;

        /**
         * DER NAME BRAUCHT EINE WACHE, KEINEN ZWEITEN VERSUCH.
         *
         * Bisher wurde zweimal benannt - einmal nach 12 Frames, einmal nach
         * 36. Der Nutzer meldete am 2026-09-03 trotzdem: *"`<nbsp>`
         * funktioniert nicht, Strassen haben weiterhin random Namen."*
         *
         * Der Grund ist die Ursache, die im Kommentar unten schon steht:
         * CS2 bildet den Strassenzug NEU, sobald sich an den Kanten etwas
         * aendert, und der neue Zug traegt wieder den vom Spiel vergebenen
         * Namen. Wann das letzte Mal neu gebildet wird, laesst sich nicht
         * ausrechnen - also wird nicht geraten, sondern nachgesehen.
         *
         * Zwoelf Durchgaenge im Abstand von 30 Frames decken rund zehn
         * Sekunden ab. Das Benennen selbst kostet nichts: es sind eine
         * Handvoll Aggregate.
         */
        private const int ZoningNamenAbstand = 30;
        private const int ZoningNamenDurchgaenge = 12;
        /** Wie viele Durchgaenge hintereinander nichts mehr zu tun war. */
        private int _zoningNamenRuhig;
        private int _zoningNamenFrames;
        private int _zoningNamenRest;
        private Entity _zoningNamenTraeger = Entity.Null;

        /**
         * Die Zoning-Flaechen und die Seitenwahl werden HIER festgehalten,
         * nicht erst spaeter gelesen.
         *
         * `_buildSettings` wird auf null gesetzt, sobald der Bauauftrag
         * fertig ist - also lange bevor die verzoegerte Seitenwahl laeuft.
         * Beim ersten Anlauf las sie deshalb eine leere Liste und stieg
         * STUMM aus: im Log stand nichts, im Spiel zonte es weiter nach
         * aussen, und nichts sagte warum.
         */
        private ParkingGeometry.Zoningflaeche[] _zoningSeitenFlaechen;
        private ParkingGeometry.Zoningseite _zoningSeitenWahl;
        private ParkingGeometry.RandzoningLinie[] _zoningSeitenRandzoning;
        /** Die Mitte des Parkplatzes - `_points` ist spaeter schon leer. */
        private float2 _zoningSeitenLotmitte;
        /**
         * Die GEPLANTEN Achsen der Randzoning-Strassen.
         *
         * Aus demselben Grund gemerkt wie die Lotmitte: die Seitenwahl laeuft
         * zwoelf Frames nach dem Bau, und bis dahin ist das Vorschaulayout
         * weg. Ohne sie muesste die Seitenwahl den Kurs wieder an seiner Lage
         * erraten - und genau daran scheiterte sie, seit die RZ-Strasse eine
         * Fahrgasse ist (siehe `ParkingLayout.RandzoningRoad`).
         */
        private (float2 A, float2 B, float2 Innen)[] _zoningSeitenRandachsen;
        private (float2 A, float2 B, bool Links, bool Aus)[]
            _zoningSeitenHandschaltungen;

        /**
         * Benennt so lange nach, bis CS2 den Strassenzug in Ruhe laesst.
         *
         * Ein Durchgang ist billig und wirkt nur dort, wo der Name fehlt.
         * Der letzte Durchgang meldet, was am Ende steht - ohne diese Zeile
         * wuesste niemand, ob die Wache ihr Ziel erreicht hat.
         */
        private void PflegeZoningNamenswache()
        {
            if (_zoningNamenRest <= 0) return;
            if (--_zoningNamenFrames > 0) return;

            _zoningNamenFrames = ZoningNamenAbstand;
            _zoningNamenRest--;
            var offen = BenenneZoningstrassen(_zoningNamenTraeger);

            /*
             * EIN LEERER DURCHGANG IST NOCH KEIN ERFOLG.
             *
             * Bis zum 2026-09-15 war er einer, und die Wache schaltete sich
             * nach dem ersten ab. Das war doppelt falsch: der Name kam
             * ueberhaupt nicht an (siehe Unsichtbarername), und selbst wenn
             * er angekommen waere, bildet CS2 den Strassenzug spaeter neu -
             * dann entsteht eine NEUE Entity ohne Namen, und niemand schaut
             * mehr hin.
             *
             * Jetzt zaehlt `offen` echte Rueckfragen: Strassenzuege ohne
             * `CustomName`. Erst wenn zwei Durchgaenge hintereinander nichts
             * mehr zu tun hatten, ist es stabil - einer koennte zufaellig in
             * eine Neubildung gefallen sein.
             */
            if (offen == 0 && ++_zoningNamenRuhig < 2) return;
            if (offen > 0) _zoningNamenRuhig = 0;

            if (offen == 0)
            {
                Mod.log.Info("PLT-Zoningstrasse: alle Strassenzuege ohne "
                    + "sichtbaren Namen (Wache nach "
                    + (ZoningNamenDurchgaenge - _zoningNamenRest)
                    + " Durchgang/Durchgaengen fertig).");
                _zoningNamenRest = 0;
                _zoningNamenRuhig = 0;
                _zoningNamenTraeger = Entity.Null;
                return;
            }
            if (_zoningNamenRest == 0)
                Mod.log.Warn($"PLT-Zoningstrasse: nach "
                    + $"{ZoningNamenDurchgaenge} Durchgaengen tragen noch "
                    + $"{offen} Strassenzug/Strassenzuege einen sichtbaren "
                    + "Namen. CS2 bildet sie offenbar weiter neu.");
        }

        /** Nach dem Bau aufrufen; Seitenwahl und Messung folgen von selbst. */
        private void PlaneZoningBlockmessung(Entity traeger)
        {
            if (traeger == Entity.Null) return;
            _zoningBlockTraeger = traeger;
            /*
             * AUS DEN GEZEICHNETEN FLAECHEN, NICHT AUS DEN BAUEINSTELLUNGEN.
             *
             * `_buildSettings` ist schon null, wenn der Traeger entsteht -
             * frueher gemerkt reichte nicht, die Warnung im Log hat es
             * gezeigt: *"keine gemerkte Zoning-Flaeche"*. Die vom Nutzer
             * gezeichneten Flaechen bleiben dagegen bestehen, und fuer die
             * Innenseite braucht es nur ihre Mitte.
             */
            _zoningSeitenFlaechen = _zoningflaechen.Count > 0
                ? _zoningflaechen.ToArray()
                : _buildSettings?.Zoningflaechen;
            /*
             * DAS RANDZONING GEHOERT MITGEMERKT - aus genau demselben Grund
             * wie die Flaechen eine Zeile darueber.
             *
             * Beim Zuruecksetzen nach dem Bau wird `_randzoning` geleert.
             * Die Seitenwahl laeuft aber erst zwoelf Frames spaeter und sah
             * dann eine leere Liste: kein Abschnitt erkannt, also auch nicht
             * "nur nach aussen" geschaltet. Der Nutzer hat es zweimal
             * gemeldet - *"Zoning an der RZ ist weiterhin nach innen"* -, und
             * beim ersten Mal habe ich die falsche Ursache behoben.
             */
            _zoningSeitenRandzoning = _randzoning
                .Select(l => l.Clone()).ToArray();
            _zoningSeitenRandachsen = _areaPreviewLayout?.RandzoningRoad;
            _zoningSeitenLotmitte = float2.zero;
            foreach (var p in _points) _zoningSeitenLotmitte += p;
            if (_points.Count > 0) _zoningSeitenLotmitte /= _points.Count;
            /*
             * DIE HANDSCHALTUNGEN GEHOEREN GENAUSO MITGEMERKT.
             *
             * Der Nutzer am 2026-09-04: *"Wenn ich das Road Side im Edit-Modus
             * aendere, wird es nicht gespeichert. Gehe ich wieder in Edit, ist
             * alles wie vor dem Edit."*
             *
             * Zwei Ursachen auf einmal, und beide sind hier zu sehen:
             * `_zoningSeitenPlan` wird von `VergissZoningflaechen` geleert,
             * bevor die Seitenwahl zwoelf Frames spaeter laeuft - dieselbe
             * Falle wie beim Randzoning eine Zeile darueber. UND
             * `SetzeZoningSeiten` hat den Plan noch nie gelesen; die
             * Handschaltung wirkte ausschliesslich in der Vorschau.
             *
             * Nach dem Bau liest die Vorschau den ECHTEN Zustand der Strassen
             * (`SucheGebauteSeite`). Der trug die Handschaltung nie - deshalb
             * stand dort wieder die Panelwahl, und es sah aus wie "nicht
             * gespeichert".
             */
            _zoningSeitenHandschaltungen = _zoningSeitenPlan.ToArray();
            _zoningSeitenWahl = ZoningSeite;
            _zoningSeitenFrames = ZoningSeitenWartezeit;
            _zoningBlockFrames = ZoningSeitenWartezeit + ZoningBlockWartezeit;
            _zoningNamenTraeger = traeger;
            _zoningNamenRest = ZoningNamenDurchgaenge;
            _zoningNamenRuhig = 0;
            _zoningNamenFrames = ZoningSeitenWartezeit + ZoningNamenAbstand;
        }

        /** Je Frame aufrufen. */
        private void PflegeZoningBlockmessung()
        {
            PflegeZoningNamenswache();

            if (_zoningSeitenFrames > 0 && --_zoningSeitenFrames == 0)
            {
                SetzeZoningSeiten(_zoningBlockTraeger);
                // NACH der automatischen Wahl - sonst ueberschriebe diese die
                // Handschaltung gleich wieder.
                WendeGemerkteZoningSeitenAn(_zoningBlockTraeger);
                BenenneZoningstrassen(_zoningBlockTraeger);
            }

            if (_zoningBlockFrames <= 0) return;
            if (--_zoningBlockFrames > 0) return;

            var traeger = _zoningBlockTraeger;
            _zoningBlockTraeger = Entity.Null;

            /*
             * NOCH EINMAL BENENNEN - der erste Versuch verpufft.
             *
             * Die Seitenschalter markieren die Kanten als `Updated`, und
             * daraufhin bildet CS2 den Strassenzug NEU. Der Name hing am
             * alten Aggregat und ist damit weg; der neue traegt wieder den
             * vom Spiel vergebenen. Der Nutzer sah genau das: *"Es hat bis
             * jetzt nicht geklappt und ein Random-Name stand da."*
             *
             * Hier, 24 Frames spaeter, ist die Neubildung durch.
             */
            BenenneZoningstrassen(traeger);
            if (traeger == Entity.Null || !EntityManager.Exists(traeger)) return;
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(traeger)) return;

            var zoningPrefabs = SammleZoningPrefabs();
            if (zoningPrefabs.Count == 0) return;

            var kanten = 0;
            var bloecke = 0;
            var zellen = 0;
            var groessen = new List<string>();
            var laengen = new List<string>();
            var knotenpaare = new List<(Entity Start, Entity Ende)>();
            var knotenlos = 0;
            var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(traeger, true);
            for (var i = 0; i < subNets.Length; i++)
            {
                var kante = subNets[i].m_SubNet;
                if (!EntityManager.Exists(kante)) continue;
                if (!EntityManager.HasComponent<PrefabRef>(kante)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab;
                if (!zoningPrefabs.Contains(prefab)) continue;
                kanten++;
                /*
                 * DIE KNOTEN MITSCHREIBEN - fuer die ECHTE Zusammenhangs-
                 * pruefung weiter unten.
                 */
                if (EntityManager.HasComponent<Game.Net.Edge>(kante))
                {
                    var e = EntityManager.GetComponentData<Game.Net.Edge>(kante);
                    knotenpaare.Add((e.m_Start, e.m_End));
                }
                else knotenlos++;
                /*
                 * DIE KANTENLAENGE IST DER SCHLUESSEL.
                 *
                 * Codex hat die Rechnung belegt (bericht-kacheln.md, 4.2):
                 * eine 6er-Seite muss als 56-m-Achse ankommen, CS2 haengt je
                 * Ende 4 m an, ergibt 64 m und damit 8 nominelle Zellen. Die
                 * beiden Eckstrassen belegen davon zwei - bleiben genau 6.
                 *
                 * Gemessen wurden aber Bloecke mit 8 UND mit 7 Zellen. Ein
                 * 7er beweist, dass die fertige Kante die Bedingung nicht
                 * erfuellt. Ob sie zu kurz ankam oder ob eine Halbphase die
                 * Enderweiterung um 4 m verkuerzt hat, unterscheidet nur die
                 * echte Laenge - deshalb steht sie ab jetzt im Log.
                 */
                var laenge = 0f;
                if (EntityManager.HasComponent<Curve>(kante))
                    laenge = EntityManager.GetComponentData<Curve>(kante)
                        .m_Length;
                laengen.Add($"{laenge:F2} m");
                if (!EntityManager.HasBuffer<SubBlock>(kante)) continue;
                var subBlocks = EntityManager.GetBuffer<SubBlock>(kante, true);
                for (var b = 0; b < subBlocks.Length; b++)
                {
                    var block = subBlocks[b].m_SubBlock;
                    if (!EntityManager.Exists(block)) continue;
                    if (!EntityManager.HasComponent<Block>(block)) continue;
                    bloecke++;
                    var daten = EntityManager.GetComponentData<Block>(block);
                    var gueltig = EntityManager.HasComponent<ValidArea>(block)
                        ? EntityManager.GetComponentData<ValidArea>(block).m_Area
                        : new int4(-1, -1, -1, -1);
                    // Die GUELTIGE Flaeche zaehlt, nicht die nominelle Groesse:
                    // genau dort verliert der Nutzer seine Kachelreihe.
                    var breite = math.max(0, gueltig.y - gueltig.x);
                    var tiefe = math.max(0, gueltig.w - gueltig.z);
                    zellen += breite * tiefe;
                    groessen.Add($"{daten.m_Size.x}x{daten.m_Size.y}"
                        + $" gueltig {breite}x{tiefe}");
                }
            }

            MeldeZoningKnotennetz(knotenpaare, knotenlos);

            Mod.log.Info($"PLT-Zoningbloecke: {kanten} Zoning-Kante(n), "
                + $"{bloecke} Block/Bloecke, {zellen} gueltige Zelle(n). "
                + (laengen.Count > 0
                    ? "Kantenlaengen: " + string.Join(" | ", laengen) + ". "
                    : string.Empty)
                + (groessen.Count == 0
                    ? "Keine Bloecke - CS2 hat an unserer Strasse nichts angelegt."
                    : "Je Block: " + string.Join(" | ", groessen)));
        }

        /** Die Prefab-Entities unserer unsichtbaren Zoning-Strassen. */
        private HashSet<Entity> SammleZoningPrefabs()
        {
            var ergebnis = new HashSet<Entity>();
            if (_zoningRoadPrefabSystem == null) return ergebnis;
            using var kandidaten = _zoningRoadQuery.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < kandidaten.Length; i++)
            {
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(kandidaten[i],
                        out var prefab) || prefab == null) continue;
                if (!prefab.name.StartsWith("PLT Zoningstrasse")) continue;
                ergebnis.Add(kandidaten[i]);
            }
            return ergebnis;
        }

        /**
         * DIE ECHTE ZUSAMMENHANGSPRUEFUNG - ueber KNOTEN, nicht ueber
         * Koordinaten.
         *
         * `MeldeZoningZusammenhang` in `ParkingLotNetBuilder` rechnet mit den
         * geplanten Punkten: beruehren sich zwei Strecken geometrisch, gilt das
         * dort als verbunden. CS2 sieht das anders. Zwei Kanten haengen nur
         * zusammen, wenn sie denselben KNOTEN-ENTITY teilen - zwei getrennte
         * Knoten an derselben Stelle sind zwei Netze, und durch zwei Netze
         * fliesst weder Strom noch Wasser.
         *
         * Der Nutzer am 2026-09-04, nachdem mein Log laengst "EINEN
         * zusammenhaengenden Strassenzug" meldete:
         *
         *   *"Ich verbinde die ZF-Strasse mit der Hauptstrasse per Strom und
         *   Wasserleitung. Die Gebaeude an der RZ-Strasse bekommen kein Strom
         *   und Wasser, daraus ist zu schliessen, dass die RZ-Strasse nicht
         *   richtig mit der ZF-Strasse verbunden wurde."*
         *
         * Er hat es am Spiel abgelesen, ich hatte nur meine Geometrie. Diese
         * Zeile misst deshalb das, was zaehlt: die Knoten der fertigen Kanten.
         *
         * Sie ersetzt die geometrische Meldung nicht, sie ergaenzt sie - erst
         * der Unterschied zwischen beiden sagt, ob eine Luecke in der PLANUNG
         * liegt oder erst beim Bauen entsteht.
         */
        private void MeldeZoningKnotennetz(
            List<(Entity Start, Entity Ende)> paare, int knotenlos)
        {
            if (paare == null || paare.Count == 0)
            {
                if (knotenlos > 0)
                    Mod.log.Warn($"PLT-Zoningknoten: {knotenlos} Kante(n) ohne "
                        + "Edge-Komponente - Zusammenhang nicht pruefbar.");
                return;
            }

            var wurzel = new Dictionary<Entity, Entity>();
            Entity Finde(Entity e)
            {
                if (!wurzel.TryGetValue(e, out var v)) { wurzel[e] = e; return e; }
                while (!v.Equals(wurzel[v])) v = wurzel[v] = wurzel[wurzel[v]];
                return v;
            }
            foreach (var paar in paare)
            {
                var a = Finde(paar.Start);
                var b = Finde(paar.Ende);
                if (!a.Equals(b)) wurzel[a] = b;
            }

            var netze = new HashSet<Entity>();
            foreach (var paar in paare)
            {
                netze.Add(Finde(paar.Start));
                netze.Add(Finde(paar.Ende));
            }

            // Wie oft kommt ein Knoten vor? Genau einmal heisst: freies Ende.
            var haeufigkeit = new Dictionary<Entity, int>();
            foreach (var paar in paare)
            foreach (var knoten in new[] { paar.Start, paar.Ende })
                haeufigkeit[knoten] = haeufigkeit.TryGetValue(knoten, out var n)
                    ? n + 1 : 1;
            var freie = 0;
            foreach (var eintrag in haeufigkeit)
                if (eintrag.Value == 1) freie++;

            var text = $"PLT-Zoningknoten: {paare.Count} Kante(n), "
                + $"{haeufigkeit.Count} Knoten, {netze.Count} getrennte(s) "
                + $"Netz(e), {freie} freie(s) Ende(n)"
                + (knotenlos > 0 ? $", {knotenlos} ohne Edge-Komponente" : string.Empty)
                + ".";
            if (netze.Count > 1)
                Mod.log.Warn(text + " GETRENNTE NETZE - dort fliessen Strom, "
                    + "Wasser und Abwasser NICHT hinueber, auch wenn die "
                    + "Geometrie zusammenhaengt.");
            else Mod.log.Info(text + " Ein Netz.");
        }

    }
}
