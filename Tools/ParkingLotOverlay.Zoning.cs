using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Rendering;
using Game.Simulation;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using UnityEngine;
using static ParkingLotTool.Tools.ParkingLotPreviewStyle;

namespace ParkingLotTool.Tools
{
    internal sealed partial class ParkingLotOverlay
    {
        private static void ZeichneZoning(
            ParkingLotPreviewBuffer buffer, float hoehe,
            IReadOnlyList<ParkingGeometry.Zoningflaeche> flaechen,
            ParkingGeometry.Zoningflaeche vorschau, int hover, int auswahl,
            ParkingGeometry.Zoningseite seite,
            IReadOnlyList<(float2 A, float2 B, bool LinksAn, bool RechtsAn)>
                strassen,
            IReadOnlyList<float3> umriss,
            IReadOnlyList<(float2 A, float2 B)> randzoningstrassen)
        {
            var kante = (float)ParkingGeometry.Zoningparzelle;
            var strasse = (float)ParkingGeometry.ZoningStrassenbreite;

            /*
             * JEDE ZELLE NUR EINMAL - egal, wer sie findet.
             *
             * Jede Flaeche rastert ihre Umgebung im EIGENEN Rahmen ab und
             * findet dabei auch Zellen, die zur Nachbarin gehoeren. Stehen
             * beide auf demselben Gitter, liegen die doppelten Zellen exakt
             * uebereinander und niemand sieht es. Sind sie gegeneinander
             * versetzt - zwei Flaechen verschiedener Groesse, wie die 6x6 und
             * die 7x2 des Nutzers -, entsteht ein Gewirr aus halb
             * ueberlappenden Kaestchen. Genau das hat er gemeldet: *"Die ZF
             * hat innen mehrere Tiles, die sich auch manchmal zu Rechtecken
             * schliessen. Auffaellig, wenn mehrere ZFs nebeneinander
             * liegen."*
             *
             * DER GERUNDETE SCHLUESSEL REICHTE NICHT.
             *
             * Er war die Zellenmitte auf einen halben Meter gerundet. Zwei
             * fast gleiche Mitten koennen aber beiderseits einer
             * Rundungsgrenze liegen und bekommen dann VERSCHIEDENE
             * Schluessel - dieselbe Kachel wird zweimal gezeichnet, und der
             * Nutzer sah das Gewirr weiter.
             *
             * GEMESSEN am 2026-09-04 an seinen Protokollen: die Rasterphasen
             * benachbarter Flaechen weichen um bis zu 5,164 mm voneinander
             * ab, und im Fall PLT-A950B8E0 liegen acht Mittenpaare naeher
             * als 0,1 m beieinander - eines davon in zwei verschiedenen
             * Schluesseln.
             *
             * Jetzt wird nicht mehr gerundet, sondern gemessen: die Mitten
             * liegen in 1-m-Faechern, und eine Kandidatin sucht in ihren
             * neun Nachbarfaechern nach einer schon gezeichneten Mitte naeher
             * als 0,5 m. Eine Rundungsgrenze gibt es damit nicht mehr, und
             * echte Nachbarzellen sind acht Meter auseinander - die Regel
             * kann sie nicht verwechseln.
             */
            const float dedupAbstand = 0.5f;
            var gezeichnet = new Dictionary<long, List<float2>>();
            long Fach(int x, int y) => (long)x * 1000000L + y;
            bool ZuerstGesehen(float2 mitte)
            {
                var fx = (int)math.floor(mitte.x);
                var fy = (int)math.floor(mitte.y);
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (!gezeichnet.TryGetValue(Fach(fx + dx, fy + dy),
                            out var liste)) continue;
                    foreach (var alt in liste)
                        if (math.distance(alt, mitte) <= dedupAbstand)
                            return false;
                }
                var schluessel = Fach(fx, fy);
                if (!gezeichnet.TryGetValue(schluessel, out var eigene))
                {
                    eigene = new List<float2>();
                    gezeichnet[schluessel] = eigene;
                }
                eigene.Add(mitte);
                return true;
            }


            void Linie(float2 a, float2 b, UnityEngine.Color farbe, float dick)
                => buffer.DrawLine(farbe, new Line3.Segment(
                    new float3(a.x, hoehe, a.y),
                    new float3(b.x, hoehe, b.y)), dick, false);

            void Umriss(float2[] ecken, UnityEngine.Color farbe, float dick)
            {
                for (var k = 0; k < 4; k++)
                    Linie(ecken[k], ecken[(k + 1) % 4], farbe, dick);
            }

            void Eine(ParkingGeometry.Zoningflaeche f, bool hervor, bool zug,
                      bool gewaehlt)
            {
                var (laengs, quer) = ParkingGeometry.ZoningRichtungen(f.Winkel);
                var laenge = (float)(f.Spalten * ParkingGeometry.Zoningparzelle);
                var tiefe = (float)(f.Reihen * ParkingGeometry.Zoningparzelle);
                if (laenge < 0.01f || tiefe < 0.01f) return;

                var wach = hervor || gewaehlt || zug;

                /*
                 * JEDE KACHEL WIRD EINZELN GEFRAGT: ERREICHT MICH EINE
                 * EINGESCHALTETE STRASSENSEITE?
                 *
                 * Vorher zeichnete diese Stelle zwei fertige Gitter - eins
                 * ueber dem gezogenen Rechteck, eins als Band aussen herum -
                 * und liess sich vom Panel sagen, welches davon sichtbar
                 * ist. Das log, sobald der Nutzer eine einzelne Seite
                 * abschaltete, und es log auch in die andere Richtung: in
                 * der Mitte einer grossen Flaeche zeichnete es Kacheln, die
                 * CS2 nie anlegt, weil dort keine Strasse mehr hinreicht.
                 *
                 * Jetzt entscheidet die Zelle. Damit erfuellt sich die
                 * Bedingung des Nutzers von selbst: *"Tiles duerfen nicht
                 * verschwinden, wenn eine andere Strasse welche erzeugt."*
                 * Es wird nach ALLEN Strassen gefragt, und die erste, die
                 * passt, genuegt - eine gegenueberliegende Strasse oder eine
                 * an einer Innenecke haelt dieselbe Kachel am Leben.
                 */
                var fuellung = ZoningColor;
                fuellung.a = hervor ? ZoningFuellungHover
                    : gewaehlt || zug ? ZoningFuellung
                    : ZoningFuellungRuhe;
                var randfarbe = ZoningColor;
                randfarbe.a = wach ? 0.55f : 0.28f;

                /*
                 * SO WEIT NACH AUSSEN, WIE DER RAND REICHT - NICHT WEITER.
                 *
                 * Hier standen sechs Ringe, weil CS2 sechs Zellen tief
                 * zonen kann. Das war die falsche Zahl fuer eine ANZEIGE:
                 * der Nutzer stellt eine Aussentiefe ein und will sehen, was
                 * er eingestellt hat. Er hat es sofort gemerkt - statt zwei
                 * Reihen standen vier da, die letzte davon ausgefranst.
                 *
                 * `Rand` ist Fahrbahnbreite plus Aussentiefe, also genau die
                 * Zahl, die er kennt.
                 */
                var ringe = math.max(1,
                    (int)math.round((float)f.Rand / kante));
                // Ein schmaler Spalt zwischen den Kacheln - so liest man sie
                // als Kacheln und nicht als eine grosse Flaeche.
                var fuellbreite = kante - 0.6f;

                for (var i = -ringe; i < f.Spalten + ringe; i++)
                for (var j = -ringe; j < f.Reihen + ringe; j++)
                {
                    var zellmitte = f.Ecke
                        + laengs * ((i + 0.5f) * kante)
                        + quer * ((j + 0.5f) * kante);

                    /*
                     * AUF DER FAHRBAHN LIEGT KEINE KACHEL.
                     *
                     * Vorher entschied das die Zellennummer: der Ring um die
                     * eigenen Parzellen galt als Fahrbahn. Das trifft die
                     * falschen Zellen, sobald sich zwei Flaechen eine
                     * Strasse teilen - dann gehoert die Fahrbahn keiner von
                     * beiden allein. Die Strasse selbst zu fragen, ist
                     * unabhaengig davon, wem sie gehoert.
                     */
                    /*
                     * AUSSERHALB DES PARKPLATZES GIBT ES KEINE KACHEL.
                     *
                     * Befund des Nutzers: bei einer 3x10-Flaeche stand
                     * ausserhalb der Polygongrenze noch eine Reihe 1x10.
                     *
                     * Die Ursache liegt eine Stufe frueher: liegt die
                     * Flaeche am Rand, faellt die Zoning-Strasse DORT weg -
                     * sie passt nicht mehr ins Polygon. Damit fehlt aber
                     * auch das Hindernis, das die gegenueberliegende Strasse
                     * bisher aufgehalten hat, und die reicht sechs Zellen
                     * weit. Bei drei Zellen Breite langt sie also hinaus.
                     *
                     * Deshalb hier die Grenze, die der Nutzer meint: der
                     * gezogene Umriss. Was ausserhalb liegt, ist nicht mehr
                     * sein Parkplatz.
                     */
                    if (!ZoningZelleImUmriss(umriss, zellmitte)) continue;
                    if (ParkingLotToolSystem.ZoningZelleAufStrasse(
                            strassen, zellmitte)) continue;
                    /*
                     * UNSICHERE KACHELN BLASSER.
                     *
                     * Bedienen ZWEI Strassen dieselbe Kachel, entscheidet
                     * CS2 erst nach dem Bauen, welcher Block sie bekommt -
                     * und zwar nach dem Alter der Strassen. Vorher steht das
                     * nicht fest. Eine Zahl zu behaupten, die hinterher nicht
                     * stimmt, waere das Schlechteste; also wird sie gezeigt,
                     * aber erkennbar anders.
                     */
                    var stand = ParkingLotToolSystem.ZoningZellenstand(
                        strassen, zellmitte);
                    if (stand == ParkingLotToolSystem.Kachelstand.Keine) continue;
                    if (!ZuerstGesehen(zellmitte)) continue;
                    var kachelfarbe = stand
                        == ParkingLotToolSystem.Kachelstand.Unsicher
                        ? Alpha(fuellung, fuellung.a * 0.35f) : fuellung;

                    var von = zellmitte - laengs * (fuellbreite * 0.5f);
                    var bis = zellmitte + laengs * (fuellbreite * 0.5f);
                    buffer.DrawLine(kachelfarbe, kachelfarbe, 0f,
                        OverlayRenderSystem.StyleFlags.Projected,
                        new Line3.Segment(
                            new float3(von.x, hoehe, von.y),
                            new float3(bis.x, hoehe, bis.y)),
                        fuellbreite, default);

                    // Die vier Kanten einzeln - ein Eckenfeld je Zelle waere
                    // bei einigen hundert Zellen je Frame reiner Muell fuer
                    // den Speicherbereiniger.
                    var lh = laengs * (fuellbreite * 0.5f);
                    var qh = quer * (fuellbreite * 0.5f);
                    var e0 = zellmitte - lh - qh;
                    var e1 = zellmitte + lh - qh;
                    var e2 = zellmitte + lh + qh;
                    var e3 = zellmitte - lh + qh;
                    Linie(e0, e1, randfarbe, GridLineWidth);
                    Linie(e1, e2, randfarbe, GridLineWidth);
                    Linie(e2, e3, randfarbe, GridLineWidth);
                    Linie(e3, e0, randfarbe, GridLineWidth);
                }

                /*
                 * DER STAERKSTE STRICH LIEGT AUF DEM GEZOGENEN RECHTECK.
                 *
                 * Es ist der Griff: was der Nutzer zieht, dreht und
                 * verschiebt. Die gewaehlte Flaeche traegt den dicksten Rand,
                 * sonst muesste man raten, welche gerade gemeint ist.
                 */
                var dick = gewaehlt || zug ? SelectedLineWidth
                    : hervor ? HoverLineWidth
                    : PolygonLineWidth;
                Umriss(ParkingGeometry.ZoningEcken(f), ZoningColor, dick);
            }

            if (flaechen != null)
                for (var i = 0; i < flaechen.Count; i++)
                    Eine(flaechen[i], i == hover, false, i == auswahl);
            if (vorschau != null) Eine(vorschau, true, true, false);

            /*
             * DER KORRIDOR: NUR DA, WO WIRKLICH EINE STRASSE HINKOMMT.
             *
             * Hier stand ein voller Umriss "gezogenes Rechteck plus 8 m" JE
             * FLAECHE, unbedingt gezeichnet. Der folgt aber nicht den
             * geschnittenen und verschmolzenen Strassenstuecken: liegen
             * mehrere Flaechen nebeneinander, zeichnete jede ihren ganzen
             * Ring - auch entlang der Kanten, an denen der Bau gar keine
             * Strasse anlegt. Das waren die duennen Zusatzrechtecke, die der
             * Nutzer gemeldet hat.
             *
             * Jetzt kommt der Umriss aus demselben Plan, aus dem auch die
             * Kacheln kommen: je gebautem Strassenstueck ein Rechteck seiner
             * Fahrbahnbreite. Was der Bau nicht anlegt, zeigt die Vorschau
             * auch nicht mehr - dieselbe Regel, die schon fuer die
             * Strassenlinien selbst gilt.
             */
            if (strassen != null)
            {
                var korridor = ZoningColor;
                korridor.a = 0.3f;
                foreach (var st in strassen)
                {
                    var spanne = st.B - st.A;
                    var laenge = math.length(spanne);
                    if (laenge < 0.01f) continue;
                    var r = spanne / laenge;
                    var q = new float2(-r.y, r.x) * (strasse * 0.5f);
                    Umriss(new[]
                    {
                        st.A - q, st.B - q, st.B + q, st.A + q,
                    }, korridor, PreviewLineWidth);
                }
            }

            /*
             * DIE KACHELN DES RANDZONINGS - eigener Durchgang.
             *
             * Sie haengen an keiner gezogenen Flaeche, also findet sie die
             * Schleife darueber nicht: der Nutzer sah beim Erstellen einer RZ
             * gar keine Kacheln. Und sie liegen AUSSERHALB des Polygons,
             * duerfen also nicht durch die Umrisspruefung - die ist fuer die
             * inneren Flaechen da, deren Kacheln im Parkplatz bleiben
             * muessen.
             *
             * Gerastert wird im Rahmen der Strasse selbst: entlang ihrer
             * Laenge in 8-m-Schritten, quer nach aussen bis zur Reichweite.
             * Welche Zelle wirklich entsteht, entscheidet danach dieselbe
             * Frage wie ueberall - erreicht sie eine eingeschaltete
             * Strassenseite?
             */
            if (randzoningstrassen != null)
            {
                var fuellungRz = ZoningColor;
                fuellungRz.a = ZoningFuellung;
                var randRz = ZoningColor;
                randRz.a = 0.4f;
                var fuellbreiteRz = kante - 0.6f;

                /*
                 * DIE MITTE DES PARKPLATZES - sie sagt, wo "aussen" ist.
                 *
                 * Ohne Umriss gibt es keinen Parkplatz und damit auch kein
                 * Randzoning; dann bleibt die Richtung offen und es wird
                 * gezeichnet wie bisher.
                 */
                var hatMitte = umriss != null && umriss.Count >= 3;
                var lotmitte = float2.zero;
                if (hatMitte)
                {
                    foreach (var punkt in umriss)
                        lotmitte += new float2(punkt.x, punkt.z);
                    lotmitte /= umriss.Count;
                }

                foreach (var rz in randzoningstrassen)
                {
                    var spanne = rz.B - rz.A;
                    var laenge = math.length(spanne);
                    if (laenge < 0.01f) continue;
                    var laengsRz = spanne / laenge;
                    var querRz = new float2(-laengsRz.y, laengsRz.x);

                    /*
                     * AN EINER RZ-STRASSE GIBT ES NUR AUSSEN.
                     *
                     * Hier lief die Schleife ueber BEIDE Seiten und liess nur
                     * die Fahrbahn selbst aus. Nach innen entstanden dadurch
                     * Kacheln auf dem Raster der RZ-STRASSE, waehrend dort
                     * schon die Kacheln der Zoningflaeche auf IHREM Raster
                     * liegen. Zwei Raster uebereinander - das Gewirr, das der
                     * Nutzer gemeldet hat: *"RZ erzeugt im Preview noch nach
                     * innen Kacheln, die durch die Innenflaeche von ZF
                     * angezeigt werden."*
                     *
                     * Seine Ansage zum Randzoning ist eindeutig: *"Wenn ich
                     * auf die Linie vom Polygon klicke, soll es kein Innen
                     * oder Aussen geben, sondern NUR aussen."* Innen gehoert
                     * den Zoningflaechen, die er selbst zieht - dort zeichnet
                     * ihr eigener Durchgang.
                     */
                    /*
                     * DIESELBE EINE REGEL - DAS DRITTE VORKOMMEN.
                     *
                     * Hier stand `Kantenmitte - Schwerpunkt des Polygons`.
                     * Dieselbe Frage wird an drei Stellen gestellt: beim
                     * Bauen, beim Planen der Seiten, und hier beim ZEICHNEN
                     * der Kacheln. Ich habe am 2026-09-16 die ersten beiden
                     * berichtigt - diese blieb auf dem Schwerpunkt stehen,
                     * und weil sie die sichtbaren Kacheln macht, sah der
                     * Nutzer keinerlei Aenderung: *"Die Anzeige ist immer
                     * noch komplett falsch. In der oberen Ecke gehen die
                     * Tiles immer noch ins Polygon rein."*
                     *
                     * Der Schwerpunkt ist bei einer L-Form oder einer Treppe
                     * die falsche Auskunft: eine weit innen liegende Kante
                     * hat ihn auf ihrer anderen Seite. Aussen ist die
                     * Richtung zum naechsten Punkt des Umrisses.
                     */
                    var aussenVorzeichen = 0;
                    if (hatMitte)
                    {
                        var punkte = new float2[umriss.Count];
                        for (var u = 0; u < umriss.Count; u++)
                            punkte[u] = new float2(umriss[u].x, umriss[u].z);
                        var innen = ParkingLotToolSystem.InnenAusUmriss(
                            (rz.A + rz.B) * 0.5f, laengsRz, punkte);
                        if (math.lengthsq(innen) > 1e-6f)
                            aussenVorzeichen =
                                math.dot(querRz, innen) >= 0f ? -1 : 1;
                        else
                        {
                            var nachAussen = (rz.A + rz.B) * 0.5f - lotmitte;
                            aussenVorzeichen =
                                math.dot(querRz, nachAussen) >= 0f ? 1 : -1;
                        }
                    }

                    // Eine Kachel Ueberstand an jedem Ende - dieselbe
                    // Rechnung wie bei der Kachelzahl, `Laenge/8 + 1`.
                    var von = -1;
                    var bis = (int)math.round(laenge / kante) + 1;
                    for (var i = von; i <= bis; i++)
                    for (var j = -RingTiefeRz; j <= RingTiefeRz; j++)
                    {
                        if (j == 0) continue;
                        if (aussenVorzeichen != 0
                            && math.sign(j) != aussenVorzeichen) continue;
                        var zellmitte = rz.A
                            + laengsRz * ((i + 0.5f) * kante)
                            + querRz * ((j > 0 ? j - 0.5f : j + 0.5f) * kante
                                + math.sign(j) * (strasse * 0.5f));
                        if (ParkingLotToolSystem.ZoningZelleAufStrasse(
                                strassen, zellmitte)) continue;
                        var standRz = ParkingLotToolSystem.ZoningZellenstand(
                            strassen, zellmitte);
                        if (standRz == ParkingLotToolSystem.Kachelstand.Keine)
                            continue;
                        if (!ZuerstGesehen(zellmitte)) continue;
                        var farbeRz = standRz
                            == ParkingLotToolSystem.Kachelstand.Unsicher
                            ? Alpha(fuellungRz, fuellungRz.a * 0.35f)
                            : fuellungRz;

                        var lh = laengsRz * (fuellbreiteRz * 0.5f);
                        var qh = querRz * (fuellbreiteRz * 0.5f);
                        var von2 = zellmitte - lh;
                        var bis2 = zellmitte + lh;
                        buffer.DrawLine(farbeRz, farbeRz, 0f,
                            OverlayRenderSystem.StyleFlags.Projected,
                            new Line3.Segment(
                                new float3(von2.x, hoehe, von2.y),
                                new float3(bis2.x, hoehe, bis2.y)),
                            fuellbreiteRz, default);
                        var e0 = zellmitte - lh - qh;
                        var e1 = zellmitte + lh - qh;
                        var e2 = zellmitte + lh + qh;
                        var e3 = zellmitte - lh + qh;
                        Linie(e0, e1, randRz, GridLineWidth);
                        Linie(e1, e2, randRz, GridLineWidth);
                        Linie(e2, e3, randRz, GridLineWidth);
                        Linie(e3, e0, randRz, GridLineWidth);
                    }
                }
            }
        }

        /** Wie viele Kachelreihen neben einer RZ-Strasse geprueft werden. */
        private const int RingTiefeRz = 6;

        /**
         * Liegt die Zellenmitte im gezogenen Umriss?
         *
         * Ohne Umriss - also solange das Polygon noch offen ist - gilt
         * alles als drin. Eine Grenze, die es noch nicht gibt, darf nichts
         * verbieten.
         */
        private static bool ZoningZelleImUmriss(
            IReadOnlyList<float3> umriss, float2 punkt)
        {
            if (umriss == null || umriss.Count < 3) return true;
            var drin = false;
            for (int i = 0, k = umriss.Count - 1; i < umriss.Count; k = i++)
            {
                var a = new float2(umriss[i].x, umriss[i].z);
                var b = new float2(umriss[k].x, umriss[k].z);
                if (a.y > punkt.y != b.y > punkt.y
                    && punkt.x < (b.x - a.x) * (punkt.y - a.y) / (b.y - a.y) + a.x)
                    drin = !drin;
            }
            return drin;
        }

    }
}
