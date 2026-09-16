using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Tools;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ParkingLotTool.Geometry;
using static ParkingLotTool.Tools.ParkingLotTexte;

namespace ParkingLotTool.Tools
{
    /**
     * SCHALTET DAS ZONING AN EINER EINZELNEN STRASSENSEITE UM.
     *
     * Ansage des Nutzers am 2026-09-03: *"Wir brauchen noch ein Tool bzw.
     * Button, der uns erlaubt, bestimmte Strassen fuer Zoning an und aus zu
     * schalten. Und hier muss die SEITE entscheidend sein."*
     *
     * ER ARBEITET AM PLAN, NICHT AN DER GEBAUTEN STRASSE.
     *
     * Der erste Anlauf schaltete die Flagge an der fertigen Kante um und
     * setzte damit voraus, dass schon gebaut ist. Der Nutzer hat das mit
     * einem Satz erledigt: *"Wie soll ich das erst nach dem Bauen machen,
     * wenn ich nach dem Bauen nicht mehr auf Zoning zugreifen kann?"* Er hat
     * recht - der Zoning-Reiter gehoert zum Entwurf, und wer dort etwas
     * einstellt, muss es dort auch sehen.
     *
     * Deshalb merkt sich das Werkzeug die Umschaltungen als Liste von
     * Ueberschreibungen zum PLAN. Sie wirken sofort in der Vorschau, gehen
     * beim Bauen in den Bauzettel und werden von dort auf die echten Kanten
     * uebertragen. Steht die Strasse schon, wird sie zusaetzlich sofort
     * umgeschaltet - dann sieht man es auch im Spiel.
     *
     * DIE SEITE WIRD NICHT GEWAEHLT, SONDERN GEKLICKT. Zwei Knoepfe fuer
     * links und rechts waeren eine Uebersetzungsaufgabe: links wovon? Der
     * Zeiger sagt es unmissverstaendlich.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Wie weit vom Zeiger eine Strasse noch angefasst werden kann.
         *
         * 24 m sind drei Kacheln. Gemessen wird ab der FAHRBAHNMITTE, und
         * der Nutzer zielt auf die Kacheln neben der Strasse, nicht auf den
         * Asphalt - mit 12 m endete die Reichweite eine Kachel daneben.
         */
        private const float ZoningSeitenreichweite = 24f;

        private EntityQuery _zoningKanten;

        /**
         * Die Umschaltungen des Nutzers, als Abweichung vom Plan.
         *
         * Gemerkt wird die KANTE ueber ihre beiden Endpunkte, nicht ueber
         * eine laufende Nummer. Der Plan wird jeden Frame neu gerechnet, und
         * eine Nummer darin haelt keine einzige Flaechenverschiebung aus -
         * siehe [[plt-zuordnung-ueber-merkmal]].
         */
        private readonly List<(float2 A, float2 B, bool Links, bool Aus)>
            _zoningSeitenPlan = new List<(float2, float2, bool, bool)>();

        /** Der Plan dieses Frames - einmal gerechnet, zweimal gebraucht. */
        private List<(float2 A, float2 B, bool LinksAn, bool RechtsAn)>
            _zoningStrassenAktuell;

        private int _zoningSeitenZiel = -1;
        private bool _zoningSeitenLinks;

        /** Was die Suche zuletzt ergeben hat - Text fuer den Zeiger. */
        private string _zoningSeitenBefund;

        internal string ZoningSeitenBefund => _zoningSeitenBefund;

        /**
         * Der Schalter braucht Zoning-Flaechen, sonst gibt es keine Strassen
         * zum Schalten. Mehr nicht - GEBAUT muss nichts sein.
         */
        /**
         * Zoning-Flaechen ODER ein geschlossener Umriss.
         *
         * Seit das Werkzeug zwei Aufgaben hat, reicht der Umriss allein: auch
         * ohne eine einzige Flaeche kann der Nutzer Randzoning an eine Linie
         * legen.
         */
        internal bool ZoningSeitenMoeglich => _zoningflaechen.Count > 0
            || (_closed && _points.Count >= 3);

        /** Der Modus selbst - vom Panel geschaltet. */
        internal bool ZoningSeitenModus { get; private set; }

        internal void SetzeZoningSeitenModus(bool an)
        {
            if (an && !ZoningSeitenMoeglich) return;
            if (ZoningSeitenModus == an) return;
            ZoningSeitenModus = an;
            _zoningSeitenZiel = -1;
            /*
             * DAS SETZEN GEHT AUS, WENN DER SEITENSCHALTER ANGEHT.
             *
             * Beide brauchen denselben Linksklick. Ansage des Nutzers:
             * *"'Place patches' sollte aus, wenn 'Toggle road side'
             * eingestellt wird - das sollte nicht verhindern, dass Toggle
             * road side funktioniert. Ich sollte nicht Knoepfe druecken
             * muessen, um eine Funktion zu nutzen."*
             *
             * Also schaltet der eine den anderen ab, statt dass der Nutzer
             * es tut. Am Vorrang aendert sich nichts: `HandleZoningSeiten`
             * wird ohnehin zuerst gefragt.
             */
            if (an) SetzeZoningModus(false);
            _uiSystem?.SetZoningSeitenModus(an);
        }

        private void InitialisiereZoningSeiten()
        {
            _zoningKanten = GetEntityQuery(
                ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
        }

        /** Je Frame, VOR der Klickverteilung und vor dem Zeichnen. */
        private void PflegeZoningStrassenplan()
        {
            _zoningStrassenAktuell = ZoningStrassenMitSeiten();
            _uiSystem?.SetZoningSeitenMoeglich(ZoningSeitenMoeglich);
            if (ZoningSeitenModus && !ZoningSeitenMoeglich)
                SetzeZoningSeitenModus(false);
        }

        internal IReadOnlyList<(float2 A, float2 B, bool LinksAn, bool RechtsAn)>
            ZoningStrassenplan => _zoningStrassenAktuell;

        /**
         * Traegt eine gemerkte Umschaltung auf eine geplante Kante auf.
         * Wird aus `ZoningStrassenMitSeiten` gerufen, nachdem Panelwahl oder
         * gebauter Zustand die Grundlage gelegt haben.
         */
        private void WendeZoningSeitenplanAn(float2 a, float2 b,
            ref bool linksAn, ref bool rechtsAn)
        {
            for (var i = 0; i < _zoningSeitenPlan.Count; i++)
            {
                var eintrag = _zoningSeitenPlan[i];
                if (!ZoningSelbeKante(eintrag.A, eintrag.B, a, b)) continue;

                /*
                 * ACHTUNG, RICHTUNG: die gemerkte Kante kann andersherum
                 * gespeichert sein als die geplante. Dann ist ihr "links"
                 * unser "rechts" - sonst schaltete ein Neuaufbau der Flaeche
                 * die falsche Seite um.
                 */
                var gedreht = math.distance(eintrag.A, b) < 0.5f
                    && math.distance(eintrag.B, a) < 0.5f;
                var links = gedreht ? !eintrag.Links : eintrag.Links;
                if (links) linksAn = !eintrag.Aus;
                else rechtsAn = !eintrag.Aus;
            }
        }

        /** Wahr, wenn dieser Modus den Klick verbraucht hat. */
        private bool HandleZoningSeiten(bool primaerGedrueckt,
            bool sekundaerGedrueckt, bool escGedrueckt)
        {
            if (!ZoningSeitenModus) return false;

            if (escGedrueckt)
            {
                SetzeZoningSeitenModus(false);
                return true;
            }

            /*
             * ZWEI AUFGABEN, EIN WERKZEUG - der Zeiger entscheidet.
             *
             * Ansage des Nutzers: *"Zum Erstellen von RZ soll bitte das
             * gleiche Tool genommen werden, womit wir derzeit die Road Side
             * einstellen."* Also wird beides gesucht, und was NAEHER liegt,
             * bekommt den Klick. Ein zusaetzlicher Schalter waere eine
             * Bedienung, die dem Nutzer nichts einbringt.
             */
            var seitenAbstand = SucheZoningSeite();
            _randzoningZiel = SucheUmrisslinie(out var linienAbstand);
            var randzoningNaeher = _randzoningZiel >= 0
                && linienAbstand < seitenAbstand;

            /*
             * RECHTSKLICK ENTFERNT, WAS UNTER DEM ZEIGER LIEGT.
             *
             * Hier stand er neben Escape: er verliess schlicht den Modus.
             * Befund des Nutzers am 2026-09-09: *"Rechtsklick beim Zoning …
             * anstatt z.B. ein Side-Toggle zu entfernen."* Dieselbe Regel wie
             * beim Linksklick - der Zeiger entscheidet -, nur umgekehrt: was
             * an ist, geht aus; eine gesetzte Randzoninglinie verschwindet.
             * Liegt nichts darunter, verlaesst er den Modus wie bisher.
             *
             * Er wird IMMER verbraucht. Faellt er durch, landet er beim
             * Umriss und loest ihn auf - genau das war die Beschwerde.
             */
            if (sekundaerGedrueckt)
            {
                if (randzoningNaeher && RandzoningLiegtAn(_randzoningZiel))
                {
                    SchalteRandzoning(_randzoningZiel);
                    return true;
                }
                if (!randzoningNaeher && ZoningSeiteIstAn())
                {
                    SchalteZoningSeite();
                    return true;
                }
                SetzeZoningSeitenModus(false);
                return true;
            }

            if (randzoningNaeher)
            {
                _zoningSeitenZiel = -1;
                MeldeRandzoningZeiger();
                if (primaerGedrueckt) SchalteRandzoning(_randzoningZiel);
                return true;
            }
            _randzoningZiel = -1;

            if (!primaerGedrueckt || _zoningSeitenZiel < 0) return true;

            SchalteZoningSeite();
            return true;
        }

        /** Der Zeigertext, wenn eine Umrisslinie gemeint ist. */
        private void MeldeRandzoningZeiger()
        {
            if (_randzoningZiel < 0 || _randzoningZiel >= _points.Count) return;
            var a = _points[_randzoningZiel];
            var b = _points[(_randzoningZiel + 1) % _points.Count];
            if (RandzoningIndex(a, b) >= 0)
            {
                _zoningSeitenBefund = T(
                    "Randzoning an dieser Linie — Klick nimmt es weg",
                    "Edge zoning on this line — click removes it");
                return;
            }
            // Die Absage kommt VOR dem Klick, nicht danach.
            _zoningSeitenBefund = PruefeRandzoning(a, b, out _)
                ? T("Umrisslinie — Klick macht Randzoning daraus",
                    "Outline edge — click turns it into edge zoning")
                : T("Kein Platz: eine Zoning-Fläche steht hier und kann "
                        + "nicht ausweichen",
                    "No room: a zoning patch sits here and cannot move aside");
        }

        /**
         * Sucht die naechste geplante Zoning-Strasse und die gemeinte Seite.
         *
         * Die Seite kommt aus dem Kreuzprodukt von Fahrtrichtung und dem
         * Vektor zum Zeiger - dieselbe Rechnung wie bei der automatischen
         * Seitenwahl, damit beide dasselbe "links" meinen.
         *
         * JEDE ABBRUCHSTELLE SAGT, WARUM. Befund des Nutzers: *"Toggle road
         * sides wird beim Klicken nicht erkannt oder per User-Feedback nicht
         * mitgeteilt."* Genau diese zwei Faelle waren vorher nicht
         * auseinanderzuhalten, weil die Suche stumm ausstieg.
         */
        /** Rueckgabe: der Abstand zur naechsten Seite, oder unendlich. */
        private float SucheZoningSeite()
        {
            _zoningSeitenZiel = -1;

            if (!_letzteWeltpositionGueltig)
            {
                _zoningSeitenBefund = T(
                    "Kein Punkt unter dem Zeiger — auf den Boden zeigen.",
                    "No point under the cursor — point at the ground.");
                return float.PositiveInfinity;
            }

            var plan = _zoningStrassenAktuell;
            if (plan == null || plan.Count == 0)
            {
                _zoningSeitenBefund = T(
                    "Keine Zoning-Straße geplant — erst eine Fläche ziehen.",
                    "No zoning road planned — drag an area first.");
                return float.PositiveInfinity;
            }

            var welt = _letzteWeltposition;
            var zeiger = new float2(welt.x, welt.z);
            var bester = float.PositiveInfinity;
            var trefferLinks = false;
            var treffer = -1;

            var rzAchsen = RandzoningStrassenAchsen;
            // Wie nah die naechste Randzoning-Linie liegt - nur fuer die
            // Meldung, ausgewaehlt wird sie nie.
            var rzAbstand = float.PositiveInfinity;
            for (var i = 0; i < plan.Count; i++)
            {
                var a = plan[i].A;
                var b = plan[i].B;
                var richtung = b - a;
                var laenge = math.lengthsq(richtung);
                if (laenge < 1e-6f) continue;

                /*
                 * EINE RZ-STRASSE HAT KEINE WAEHLBARE SEITE.
                 *
                 * Sie zont nach aussen, weil nach innen der Parkplatz liegt -
                 * das ist keine Einstellung, sondern die Bauart. Sie hier
                 * anzubieten hiesse, einen Schalter zu zeigen, der nur falsch
                 * bedient werden kann.
                 */
                var istRandzoning = false;
                foreach (var achse in rzAchsen)
                    if (ParkingGeometry.RandzoningSelbeLinie(
                            achse.A, achse.B, a, b))
                    { istRandzoning = true; break; }
                if (istRandzoning)
                {
                    var rzT = math.clamp(math.dot(zeiger - a, richtung) / laenge, 0f, 1f);
                    rzAbstand = math.min(rzAbstand,
                        math.distance(zeiger, a + richtung * rzT));
                    continue;
                }

                var t = math.clamp(math.dot(zeiger - a, richtung) / laenge, 0f, 1f);
                var naechster = a + richtung * t;
                var abstand = math.distance(zeiger, naechster);
                if (abstand >= bester) continue;

                bester = abstand;
                treffer = i;
                var zumZeiger = zeiger - naechster;
                trefferLinks =
                    richtung.x * zumZeiger.y - richtung.y * zumZeiger.x > 0f;
            }

            /*
             * DIE RANDZONING-LINIE BEIM NAMEN NENNEN.
             *
             * Sie wird oben uebersprungen, weil sie keine waehlbare Seite hat.
             * Ist sie das Naechste am Zeiger, waere "keine Strasse gefunden"
             * schlicht falsch - der Nutzer steht ja davor.
             */
            var nurRandzoning = rzAbstand <= ZoningSeitenreichweite
                && (treffer < 0 || rzAbstand < bester);
            if (nurRandzoning)
            {
                _zoningSeitenBefund = T(
                    "Randzoning-Straße — sie zont immer nach außen, hier gibt "
                        + "es keine Seite zu wählen.",
                    "Edge-zoning road — it always zones outwards, there is no "
                        + "side to choose here.");
                return float.PositiveInfinity;
            }
            if (treffer < 0)
            {
                _zoningSeitenBefund = T(
                    "Keine Zoning-Straße gefunden.",
                    "No zoning road found.");
                return float.PositiveInfinity;
            }
            if (bester > ZoningSeitenreichweite)
            {
                _zoningSeitenBefund = T(
                    $"Nächste Zoning-Straße {bester:F0} m entfernt — näher heran.",
                    $"Nearest zoning road is {bester:F0} m away — move closer.");
                return float.PositiveInfinity;
            }

            _zoningSeitenZiel = treffer;
            _zoningSeitenLinks = trefferLinks;
            var an = trefferLinks
                ? plan[treffer].LinksAn : plan[treffer].RechtsAn;
            var seitenwort = trefferLinks
                ? T("linke", "left") : T("rechte", "right");
            _zoningSeitenBefund = an
                ? T($"{seitenwort} Seite: Zoning AN — Klick schaltet aus",
                    $"{seitenwort} side: zoning ON — click turns it off")
                : T($"{seitenwort} Seite: Zoning AUS — Klick schaltet ein",
                    $"{seitenwort} side: zoning OFF — click turns it on");
            return bester;
        }

        /**
         * Kehrt die gemeinte Seite um - im Plan, und wenn die Strasse schon
         * steht, auch an ihr.
         */
        /** Ist die Seite unter dem Zeiger ueberhaupt an? */
        private bool ZoningSeiteIstAn()
        {
            var plan = _zoningStrassenAktuell;
            if (plan == null || _zoningSeitenZiel < 0
                || _zoningSeitenZiel >= plan.Count) return false;
            var kante = plan[_zoningSeitenZiel];
            return _zoningSeitenLinks ? kante.LinksAn : kante.RechtsAn;
        }

        private void SchalteZoningSeite()
        {
            var plan = _zoningStrassenAktuell;
            if (plan == null || _zoningSeitenZiel < 0
                || _zoningSeitenZiel >= plan.Count) return;

            var kante = plan[_zoningSeitenZiel];
            var warAn = _zoningSeitenLinks ? kante.LinksAn : kante.RechtsAn;
            MerkeZoningSeitenplan(kante.A, kante.B, _zoningSeitenLinks, warAn);
            SchalteGebauteZoningSeite(kante.A, kante.B, _zoningSeitenLinks,
                !warAn);

            // Sofort neu rechnen, sonst zeigte der Zeigertext bis zum
            // naechsten Frame noch den alten Zustand an.
            _zoningStrassenAktuell = ZoningStrassenMitSeiten();
            Mod.log.Info("PLT-Zoningseite von Hand: "
                + (_zoningSeitenLinks ? "links" : "rechts")
                + " an der geplanten Kante ist jetzt "
                + (warAn ? "AUS" : "AN") + ".");
        }

        /** Legt die Umschaltung in die Merkliste oder aendert sie dort. */
        private void MerkeZoningSeitenplan(float2 a, float2 b, bool links,
            bool aus)
        {
            for (var i = 0; i < _zoningSeitenPlan.Count; i++)
            {
                var eintrag = _zoningSeitenPlan[i];
                if (eintrag.Links != links) continue;
                if (!ZoningSelbeKante(eintrag.A, eintrag.B, a, b)) continue;
                _zoningSeitenPlan[i] = (eintrag.A, eintrag.B, links, aus);
                return;
            }
            _zoningSeitenPlan.Add((a, b, links, aus));
        }

        /**
         * Schaltet die Flagge an einer schon gebauten Kante mit denselben
         * Endpunkten. Gibt es sie nicht, passiert nichts - dann wirkt die
         * Umschaltung erst beim Bauen, und das ist der Normalfall.
         */
        private void SchalteGebauteZoningSeite(float2 a, float2 b, bool links,
            bool aus)
        {
            if (_lotOwner == Entity.Null
                || !EntityManager.Exists(_lotOwner)) return;
            if (!EntityManager.HasComponent<ParkingLotCarrierReference>(_lotOwner))
                return;
            var traeger = EntityManager
                .GetComponentData<ParkingLotCarrierReference>(_lotOwner).Carrier;
            if (traeger == Entity.Null || !EntityManager.Exists(traeger)) return;
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(traeger)) return;

            var prefabs = SammleZoningPrefabs();
            if (prefabs.Count == 0) return;

            var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(traeger, true);
            for (var i = 0; i < subNets.Length; i++)
            {
                var kante = subNets[i].m_SubNet;
                if (!EntityManager.Exists(kante)) continue;
                if (!EntityManager.HasComponent<PrefabRef>(kante)) continue;
                if (!prefabs.Contains(
                        EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab))
                    continue;
                if (!EntityManager.HasComponent<Curve>(kante)) continue;

                var kurve = EntityManager.GetComponentData<Curve>(kante).m_Bezier;
                var ka = new float2(kurve.a.x, kurve.a.z);
                var kb = new float2(kurve.d.x, kurve.d.z);
                // TEILSTUECK GENUEGT, und ALLE Teilstuecke werden geschaltet:
                // CS2 teilt an jedem Knoten, aus einem geplanten Stueck
                // koennen also mehrere Kanten geworden sein.
                if (!ZoningTeilstueckVon(a, b, ka, kb, out var gedreht)) continue;

                var echteSeite = gedreht ? !links : links;
                var linksAus = echteSeite ? aus : LiestSeite(kante, true);
                var rechtsAus = echteSeite ? LiestSeite(kante, false) : aus;
                SetzeSeitenflaggen(kante, linksAus, rechtsAus);
            }
        }

        /**
         * Traegt die gemerkten Umschaltungen beim Bauen in den Bauzettel.
         *
         * Der Zettel ist das, woraus derselbe Parkplatz wieder entsteht -
         * dort gehoeren sie hin, und von dort holt sie
         * `WendeGemerkteZoningSeitenAn` nach dem Bau auf die echten Kanten.
         */
        internal void SchreibeZoningSeitenplan(
            DynamicBuffer<ParkingLotBuildZoningSeite> puffer)
        {
            puffer.Clear();
            for (var i = 0; i < _zoningSeitenPlan.Count; i++)
                puffer.Add(new ParkingLotBuildZoningSeite
                {
                    Version = ParkingLotBuildZoningSeite.CurrentVersion,
                    A = _zoningSeitenPlan[i].A,
                    B = _zoningSeitenPlan[i].B,
                    Links = _zoningSeitenPlan[i].Links,
                    Aus = _zoningSeitenPlan[i].Aus,
                });
        }

        /** Beim Laden eines Parkplatzes: die gemerkten Seiten uebernehmen. */
        internal void LadeZoningSeitenplan(
            IReadOnlyList<(float2 A, float2 B, bool Links, bool Aus)> eintraege)
        {
            _zoningSeitenPlan.Clear();
            if (eintraege == null) return;
            for (var i = 0; i < eintraege.Count; i++)
                _zoningSeitenPlan.Add(eintraege[i]);
        }

        /** Fuer das Overlay: welche Seite gerade gemeint ist. */
        internal bool ZoningSeitenVorschau(out float2 a, out float2 b,
            out bool links)
        {
            a = default;
            b = default;
            links = false;
            var plan = _zoningStrassenAktuell;
            if (!ZoningSeitenModus || plan == null) return false;
            if (_zoningSeitenZiel < 0 || _zoningSeitenZiel >= plan.Count)
                return false;
            a = plan[_zoningSeitenZiel].A;
            b = plan[_zoningSeitenZiel].B;
            links = _zoningSeitenLinks;
            return true;
        }
    }
}
