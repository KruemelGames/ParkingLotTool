using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private sealed class Gassenkurs
        {
            internal int Index;
            internal NetSegment Piece;
            internal Entity Prefab, Strasse;
            internal Anschluss Anschluss;
            internal float2 Mitte, Ende, Von, Nach;
            internal float T, HalbeBreite, Vorflaechenbreite, Laenge, Abstand, Hoehe;
        }

        /**
         * STUMMEL STATT NICHTS (Nutzer 20.10.).
         *
         * Findet eine Gasse in ihrer Richtung keine Stadtstrasse, entstand
         * bisher gar nichts - auch nicht das Stueck im Parkplatz, denn die
         * Gasse ersetzt die Zufahrt. Jetzt laeuft sie 8 m ueber den
         * Polygonrand hinaus und endet frei: ein Ankerpunkt, an den der
         * Spieler spaeter eine Strasse anschliessen kann.
         */
        private const float Stummellaenge = 8f;

        private bool PlaneGassenkurs(
            NetSegment piece,
            int index,
            float vorflaechenbreite,
            Dictionary<(long, long), float> heights,
            List<string> bericht, out Gassenkurs plan)
        {
            plan = null;
            if (!TryResolveZufahrtsgasse(piece.Art, out var gasse))
            {
                bericht.Add($"Zufahrt {index}: Gassenklon noch nicht bereit");
                return false;
            }
            // piece.A liegt am Polygonrand, piece.B innen; nach aussen ist
            // also A - B. Das ist die Fangachse der Zufahrt.
            if (!SucheStadtstrasseFuerGasse(piece.A, piece.A - piece.B,
                    out var mitte, out var halbeBreite, out var halbeImStrahl,
                    out var strasse, out var t, out var strassenhoehe))
            {
                var aussen = math.normalizesafe(piece.A - piece.B);
                if (math.lengthsq(aussen) < 0.5f)
                {
                    bericht.Add($"Zufahrt {index}: keine Stadtstrasse und keine Richtung");
                    return false;
                }
                var stummel = piece.A + aussen * Stummellaenge;
                var raus = Zufahrtsarten.FaehrtHinaus(piece.Art);
                plan = new Gassenkurs { Index = index, Piece = piece, Prefab = gasse,
                    Mitte = stummel, Ende = piece.B, Strasse = Entity.Null,
                    Vorflaechenbreite = vorflaechenbreite,
                    Von = raus ? piece.B : stummel, Nach = raus ? stummel : piece.B,
                    Laenge = math.distance(stummel, piece.B), Abstand = Stummellaenge,
                    Hoehe = float.NaN };
                bericht.Add($"Zufahrt {index}: keine Stadtstrasse in "
                    + "Fangrichtung, Bordstein hoechstens "
                    + $"{Gassenreichweite.Reichweite:F0} m ab Polygonrand - "
                    + $"Stummel {Stummellaenge:F0} m ueber den Rand zum spaeteren Anschliessen");
                return true;
            }

            var richtung = piece.A - mitte;
            var abstand = math.length(richtung);
            if (!(abstand > 0.5f))
            {
                bericht.Add($"Zufahrt {index}: liegt auf der Strassenmitte");
                return false;
            }
            richtung /= abstand;

            /*
             * DIE GASSE GEHT DURCH BIS ZUR FAHRGASSE.
             *
             * Bis zum 2026-09-18 endete sie 2 m hinter dem Bordstein, und
             * der unsichtbare Weg begann am Polygonrand - die beiden lagen
             * zwei Meter uebereinander. Autos fuhren herein, wendeten und
             * fuhren wieder hinaus.
             *
             * `piece.B` IST der Punkt, an dem der Weg bisher an die
             * Fahrgasse stiess, und er wird UNVERAENDERT uebernommen: innen
             * verbindet CS2 nur ueber einen identischen Endpunkt, nicht ueber
             * LocalConnect. Ein neu gerechneter Punkt laege um Float-Reste
             * daneben und verbaende nichts.
             */
            var ende = piece.B;
            var laenge = math.distance(mitte, ende);
            // Schraeg gemessen ist die halbe Breite laenger als senkrecht.
            if (!(laenge > halbeImStrahl))
            {
                bericht.Add($"Zufahrt {index}: zu kurz - {laenge:F2} m ab "
                    + $"Strassenmitte, Fahrbahnrand bei {halbeBreite:F2} m");
                return false;
            }

            /*
             * DER ENDPUNKT AN DER STRASSE BEKOMMT DIE HOEHE DER STRASSE.
             *
             * `SampleCourseHeight` tastet das GELAENDE ab, und unter einer
             * Strasse ist das weggeschnitten und liegt tiefer als der
             * Asphalt. Ohne diese Zeile setzt die Gasse dort unter der
             * Fahrbahn an und graebt sich ein - im Bild des Nutzers vom
             * 2026-09-18 ein Loch mitten in der Einmuendung.
             *
             * Der Hoehenspeicher wird VOR der Abtastung befragt; ihn zu
             * fuellen genuegt.
             */
            MerkeHoehe(mitte, strassenhoehe, heights);

            /*
             * DIE RICHTUNG STECKT IN DER REIHENFOLGE DER ENDPUNKTE.
             *
             * `mitte` liegt auf der Strasse, `ende` im Parkplatz. Eine
             * Gasse HINAUS faehrt also von `ende` nach `mitte` - dieselbe
             * Regel wie bei `Ausfahrt`, wo `piece.B` vor `piece.A` kommt.
             * Bei der zweispurigen Gasse ist die Reihenfolge gleichgueltig.
             */
            var hinaus = Zufahrtsarten.FaehrtHinaus(piece.Art);
            var kursVon = hinaus ? ende : mitte;
            var kursNach = hinaus ? mitte : ende;

            /*
             * EIN KURS, NICHT STUECKE.
             *
             * Vom 2026-09-23 bis 24 wurde die Gasse hier in Stuecke von
             * hoechstens 16 m geteilt - ein Missverstaendnis: gemeint war die
             * REICHWEITE bis zur Strasse (siehe `Gassenreichweite`). Die
             * Stuecke kamen im Spiel als zwei Gassen an, die sich in der
             * Mitte nicht verbanden; der Nutzer konnte beide einzeln mit dem
             * Bulldozer anwaehlen.
             */
            /*
             * DAS STRASSENENDE DOCKT AN WIE BEIM STRASSENWERKZEUG.
             *
             * Bis zum 2026-09-24 ging nur eine Koordinate an CS2, und das
             * Spiel suchte sich die Strasse daneben selbst. Der Gassenbefund
             * zeigte, was dabei herauskam: Einmuendungen 0,10 und 0,26 m
             * neben dem geplanten Punkt, und nahe am Kantenende an einem
             * anderen Knoten. Jetzt bekommt der Kurs die Kante und die
             * Teilungsstelle - oder den Knoten, wenn er dort schon steht.
             */
            var strassenpunkt = new float3(mitte.x, strassenhoehe, mitte.y);
            var anschluss = AnschlussAnStrasse(strasse, t, ref strassenpunkt);
            if (anschluss.Entity != Entity.Null)
            {
                // Der berechnete Anschlusspunkt gilt IMMER - am Knoten dessen
                // echte Lage samt Y (auch bei 0 m XZ-Abstand; Permanent nutzt
                // den Knoten unveraendert, GenerateEdges 1236ff), an der Kante
                // die Teilungsstelle (Fix 2026-09-24 gegen 0,1-0,26 m daneben).
                mitte = strassenpunkt.xz;
                strassenhoehe = strassenpunkt.y;
                MerkeHoehe(mitte, strassenhoehe, heights);
                kursVon = hinaus ? ende : mitte;
                kursNach = hinaus ? mitte : ende;
            }
            plan = new Gassenkurs { Index = index, Piece = piece, Prefab = gasse,
                Mitte = mitte, Ende = ende, Strasse = strasse, T = t,
                HalbeBreite = halbeBreite, Vorflaechenbreite = vorflaechenbreite,
                Anschluss = anschluss, Von = kursVon, Nach = kursNach,
                Laenge = laenge, Abstand = abstand, Hoehe = strassenhoehe };
            return true;
        }

    }
}
