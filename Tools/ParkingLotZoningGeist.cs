using Colossal.Mathematics;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using UnityEngine.InputSystem;
using static ParkingLotTool.Tools.ParkingLotPreviewStyle;
using static ParkingLotTool.Tools.ParkingLotTexte;

namespace ParkingLotTool.Tools
{
    /**
     * DAS GEISTERFELD AM ZEIGER (Nutzer 21.10., Rueckmeldung von Mxdanger:
     * "just show a 1x1 zone ghost square exactly where the mouse is").
     *
     * Solange "Place zoning area" an ist und nichts gezogen wird, zeigt der
     * Zeiger, was entstehen wuerde:
     *
     *   Drag size  - ein 1x1-Feld LINKS OBERHALB des Zeigers, die Spitze an
     *                seiner rechten unteren Ecke, gedreht wie die Parzellen,
     *                dazu zwei Pfeile nach rechts und unten. Gezogen wird ab
     *                diesem Feld: seine linke obere Ecke bleibt fest, der
     *                Zeiger haelt die rechte untere (Nutzer: "Das Zeichnen
     *                des Patches beginnt da wo es gerade liegt").
     *   Fixed size - die eingetragene Breite x Tiefe mittig am Zeiger.
     *
     * "Links oben" ist eine Bildschirmrichtung: welche Ecke am Zeiger haengt,
     * folgt der Kamera, die Ausrichtung bleibt die der Parzellen - so sieht
     * man beim Drehen, wie das Feld liegt. Passt es nicht, ist es rot und
     * der Grund steht am Zeiger.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private ParkingGeometry.Zoningflaeche _zoningGeist;
        private bool _zoningGeistGueltig;
        private int _zoningGeistBild = -1;
        private float2 _zoningGeistRechts, _zoningGeistUnten;
        private float2 _zoningZugMaus;
        private bool _zoningZugBewegt;
        /** Was der Zeiger beim Ziehen wollte, aber nicht passt - rot gezeigt (Codex-Vorschlag 21.10.). */
        private ParkingGeometry.Zoningflaeche _zoningZugVersuch;
        /** Warum der Geist rot ist: Textschluessel, leer wenn er passt. */
        private string _zoningGeistGrund = string.Empty;
        /**
         * WAS DEN GEIST BESTIMMT - nur bei Aenderung neu pruefen (Codex 21.10.:
         * die volle Umriss-/Randpruefung je Bild erzeugte bei ruhender Maus
         * Arbeit, Allokationen und Live-Logzeilen).
         */
        private (float2 Zeiger, float2 Links, float2 Oben, double Winkel, bool Fest, int Breite, int Tiefe,
            int Stand, int Einstellungen, int Anzahl, double Rand) _zoningGeistSchluessel;
        private readonly float2[] _zoningGeistEcken = new float2[4];

        /** Absolute Toleranz an Parzellengrenzen: Float-Rauschen, keine Verkuerzung des Zugs. */
        private const float ZoningRasterToleranz = 0.004f;

        /** Bildschirmabstand, ab dem ein Druck als Zug gilt - darunter ist es ein Klick. */
        private const float ZoningZugSchwellePx = 4f;

        /**
         * Die Parzellenachsen, so vorzeichenrichtig, dass `links` auf dem
         * Bildschirm nach links und `oben` nach oben zeigt. Ohne Kamera gelten
         * die Weltachsen.
         */
        private void ZoningBildschirmachsen(out float2 links, out float2 oben)
        {
            var (laengs, quer) = ParkingGeometry.ZoningRichtungen(ZoningWinkel);
            var rechts = new float2(1f, 0f);
            var vorne = new float2(0f, 1f);
            var kamera = UnityEngine.Camera.main;
            if (kamera != null)
            {
                var r = kamera.transform.right;
                var f = kamera.transform.forward;
                var r2 = new float2(r.x, r.z);
                var f2 = new float2(f.x, f.z);
                if (math.lengthsq(r2) > 1e-6f) rechts = math.normalize(r2);
                if (math.lengthsq(f2) > 1e-6f) vorne = math.normalize(f2);
            }
            // Die Achse, die auf dem Bildschirm waagerechter liegt, wird "links".
            var waagerecht = math.abs(math.dot(laengs, rechts)) >= math.abs(math.dot(quer, rechts))
                ? laengs : quer;
            var senkrecht = waagerecht.Equals(laengs) ? quer : laengs;
            links = math.dot(waagerecht, rechts) <= 0f ? waagerecht : -waagerecht;
            oben = math.dot(senkrecht, vorne) >= 0f ? senkrecht : -senkrecht;
        }

        /** Das 1x1-Feld links oberhalb des Zeigers und seine feste (ferne) Ecke. */
        private ParkingGeometry.Zoningflaeche ZoningEckGeist(float2 zeiger, out float2 anker)
        {
            ZoningBildschirmachsen(out var links, out var oben);
            var kante = (float)ParkingGeometry.Zoningparzelle;
            anker = zeiger + (links + oben) * kante;
            return ParkingGeometry.ZoningAusZug(anker, ZoningZugEnde(anker, zeiger), ZoningWinkel);
        }

        /**
         * Das Zugende, je Achse um wenige Millimeter zum Anker hin: genau 8 m
         * duerfen durch Float-Rauschen nicht als 2 Parzellen zaehlen. Absolut,
         * nicht anteilig - ein langer Zug verliert dadurch keine Parzelle.
         */
        private float2 ZoningZugEnde(float2 anker, float2 zeiger)
        {
            var (laengs, quer) = ParkingGeometry.ZoningRichtungen(ZoningWinkel);
            var d = anker - zeiger;
            return zeiger + laengs * (math.sign(math.dot(d, laengs)) * ZoningRasterToleranz)
                + quer * (math.sign(math.dot(d, quer)) * ZoningRasterToleranz);
        }

        /** Die feste Groesse mittig am Zeiger. */
        private ParkingGeometry.Zoningflaeche ZoningStempel(float2 zeiger, int breite, int tiefe)
        {
            var (laengs, quer) = ParkingGeometry.ZoningRichtungen(ZoningWinkel);
            var kante = (float)ParkingGeometry.Zoningparzelle;
            return new ParkingGeometry.Zoningflaeche
            {
                Ecke = zeiger - laengs * (breite * kante * 0.5f) - quer * (tiefe * kante * 0.5f),
                Spalten = breite,
                Reihen = tiefe,
                Winkel = ZoningWinkel,
            };
        }

        private bool ZoningPasst(ParkingGeometry.Zoningflaeche f)
            => ZoningLiegtImUmriss(f) && !ZoningUeberlapptAndere(f, -1);

        /** Je Bild im Leerlauf des Setzmodus: Geist berechnen, R tauscht bei fester Groesse. */
        private void PflegeZoningGeist(float2 zeiger)
        {
            if (_zoningHover >= 0) return;
            // Ausserhalb des Umrisses kein Geist: dort entsteht ohnehin nichts,
            // und ein rotes Feld bei jeder Mausbewegung waere nur Rauschen.
            if (!Vorschaugriff.Enthaelt(_points, zeiger)) return;
            var fest = ZoningFesteGroesse;
            if (fest)
            {
                var r = Keyboard.current?.rKey;
                if (r != null && r.wasPressedThisFrame)
                {
                    // Nur tauschen, wenn beide Achsen in ihre Grenzen passen - sonst
                    // wuerde still geklemmt und ein zweites R gaebe nicht das alte Feld.
                    if (ZoningFestTiefe <= ParkingGeometry.ZoningMaxBreite
                        && ZoningFestBreite <= ParkingGeometry.ZoningMaxTiefe)
                        SetzeZoningFestGroesse(ZoningFestTiefe, ZoningFestBreite);
                    else _uiSystem?.SetStatus(T("zoningGeist.tauschZuGross"));
                }
            }
            ZoningBildschirmachsen(out var links, out var oben);
            // Auch die Einstellungsrevision: Randgruen oder Fahrgassen aendern den
            // Platz, ohne dass die Geometrierevision steigt (Codex-Nachpruefung).
            var schluessel = (zeiger, links, oben, ZoningWinkel, fest, ZoningFestBreite, ZoningFestTiefe,
                _geometryRevision, _seenSettingsRevision, _zoningflaechen.Count, ZoningRand);
            _zoningGeistBild = UnityEngine.Time.frameCount;
            if (_zoningGeist != null && schluessel.Equals(_zoningGeistSchluessel)) return;
            _zoningGeistSchluessel = schluessel;
            if (fest) _zoningGeist = ZoningStempel(zeiger, ZoningFestBreite, ZoningFestTiefe);
            else
            {
                _zoningGeist = ZoningEckGeist(zeiger, out _);
                _zoningGeistRechts = -links;
                _zoningGeistUnten = -oben;
            }
            _zoningGeistGueltig = ZoningPasst(_zoningGeist);
            // Fest und rot: passt nur diese Groesse nicht, oder gar keine Parzelle?
            _zoningGeistGrund = _zoningGeistGueltig ? string.Empty
                : fest && ZoningPasst(ZoningStempel(zeiger, 1, 1)) ? "zoningGeist.passtNicht"
                : "zoning.noRoomForAParcelHere";
        }

        private bool ZoningGeistSichtbar => ZoningModus && _zoningGeist != null
            && _zoningGeistBild >= UnityEngine.Time.frameCount - 1;

        private static float2 ZoningMausBildschirm()
        {
            var maus = Mouse.current;
            return maus == null ? float2.zero : (float2)maus.position.ReadValue();
        }

        /** Der Zeigertext zum Geist, oder null ohne Geist. */
        private string ZoningGeistText()
        {
            if (!ZoningGeistSichtbar) return null;
            if (!_zoningGeistGueltig)
                return T(_zoningGeistGrund, ("breite", ZoningFestBreite), ("tiefe", ZoningFestTiefe));
            return ZoningFesteGroesse
                ? T("zoningGeist.fest", ("breite", ZoningFestBreite), ("tiefe", ZoningFestTiefe))
                : T("zoningGeist.ziehen");
        }

        private void ZeichneZoningGeist(ParkingLotPreviewBuffer buffer)
        {
            ZeichneZoningVersuch(buffer);
            if (!ZoningGeistSichtbar) return;
            var f = _zoningGeist;
            var (laengs, quer) = ParkingGeometry.ZoningRichtungen(f.Winkel);
            var kante = (float)ParkingGeometry.Zoningparzelle;
            if (!ZoningFesteGroesse) ZeichneZoningOrtsraster(buffer, f);
            var lang = laengs * (f.Spalten * kante);
            var tief = quer * (f.Reihen * kante);
            var y = _hoverPosition.y;
            float3 W(float2 p) => new float3(p.x, y, p.y);
            var farbe = _zoningGeistGueltig ? ZoningColor : Negative;
            var fuellung = farbe;
            fuellung.a = 0.22f;
            // Die Flaeche als eine breite Linie durch die Mitte, quer so breit wie tief.
            var mitteLinks = f.Ecke + tief * 0.5f;
            buffer.DrawLine(fuellung, fuellung, 0f, Game.Rendering.OverlayRenderSystem.StyleFlags.Projected,
                new Line3.Segment(W(mitteLinks), W(mitteLinks + lang)), f.Reihen * kante, float2.zero);
            var ecken = _zoningGeistEcken;
            ecken[0] = f.Ecke; ecken[1] = f.Ecke + lang; ecken[2] = f.Ecke + lang + tief; ecken[3] = f.Ecke + tief;
            for (var i = 0; i < 4; i++)
                buffer.DrawLine(farbe, new Line3.Segment(W(ecken[i]), W(ecken[(i + 1) % 4])), PolygonLineWidth);
            if (ZoningFesteGroesse || !_zoningGeistGueltig) return;
            // Zwei Pfeile: hier wird gezogen, nach rechts und nach unten.
            var zeiger = new float2(_hoverPosition.x, _hoverPosition.z);
            void Pfeil(float2 richtung, float2 ab)
            {
                var spitze = ab + richtung * (kante * 0.8f);
                var quer2 = new float2(-richtung.y, richtung.x);
                buffer.DrawLine(farbe, new Line3.Segment(W(ab + richtung * 1.2f), W(spitze)), PreviewLineWidth);
                buffer.DrawLine(farbe, new Line3.Segment(W(spitze), W(spitze - richtung * 1.6f + quer2 * 1.2f)), PreviewLineWidth);
                buffer.DrawLine(farbe, new Line3.Segment(W(spitze), W(spitze - richtung * 1.6f - quer2 * 1.2f)), PreviewLineWidth);
            }
            Pfeil(_zoningGeistRechts, zeiger + _zoningGeistUnten * (kante * -0.5f));
            Pfeil(_zoningGeistUnten, zeiger + _zoningGeistRechts * (kante * -0.5f));
        }

        /**
         * EIN KLEINES RASTER UM DEN GEIST (gemeinsame Empfehlung mit Codex):
         * eine Parzelle rundherum, blass, damit man die Teilung sieht, in die
         * das Feld beim Ziehen waechst - kein flaechendeckendes Gitter.
         */
        private void ZeichneZoningOrtsraster(ParkingLotPreviewBuffer buffer, ParkingGeometry.Zoningflaeche f)
        {
            var (laengs, quer) = ParkingGeometry.ZoningRichtungen(f.Winkel);
            var kante = (float)ParkingGeometry.Zoningparzelle;
            var y = _hoverPosition.y;
            float3 W(float2 p) => new float3(p.x, y, p.y);
            var farbe = ZoningColor;
            farbe.a = 0.22f;
            var start = f.Ecke - (laengs + quer) * kante;
            for (var i = 0; i <= 3; i++)
            {
                buffer.DrawLine(farbe, new Line3.Segment(W(start + laengs * (i * kante)),
                    W(start + laengs * (i * kante) + quer * (3 * kante))), PreviewLineWidth * 0.6f);
                buffer.DrawLine(farbe, new Line3.Segment(W(start + quer * (i * kante)),
                    W(start + quer * (i * kante) + laengs * (3 * kante))), PreviewLineWidth * 0.6f);
            }
        }

        /** Der rote Umriss dessen, was beim Ziehen gewollt war und nicht passt. */
        private void ZeichneZoningVersuch(ParkingLotPreviewBuffer buffer)
        {
            var f = _zoningZugVersuch;
            if (f == null || _zoningZug == null || !ZoningModus) return;
            var (laengs, quer) = ParkingGeometry.ZoningRichtungen(f.Winkel);
            var kante = (float)ParkingGeometry.Zoningparzelle;
            var lang = laengs * (f.Spalten * kante);
            var tief = quer * (f.Reihen * kante);
            var y = _hoverPosition.y;
            float3 W(float2 p) => new float3(p.x, y, p.y);
            var ecken = new[] { f.Ecke, f.Ecke + lang, f.Ecke + lang + tief, f.Ecke + tief };
            for (var i = 0; i < 4; i++)
                buffer.DrawDashedLine(Negative, new Line3.Segment(W(ecken[i]), W(ecken[(i + 1) % 4])),
                    PreviewLineWidth, 1.2f, 0.8f);
        }
    }
}
