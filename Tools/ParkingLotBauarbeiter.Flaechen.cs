using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * NUR DIE FLAECHEN EINES GEBAUTEN PARKPLATZES NEU PLANEN (Sync-Schritt 16).
     *
     * Der volle Hintergrund-Neubau fasst Wege, Knoten und Anschluesse an und
     * scheiterte am 28.10. an Lot 499405 (Besitzerpruefung 19/20 Kurse,
     * Rueckweg 15/18 Originalkanten, Parkplatz gesperrt). Fuer ein
     * Flaechenproblem ist das der falsche Hebel. Hier plant der isolierte
     * Bauarbeiter dieselben Flaechen wie der Bau - Bauzettel, Bruecken,
     * frische Vorflaechen-Messung, dieselben Prefabs und Klone
     * (Flaechengruppen) - und gibt sie nur heraus. Wege bleiben unberuehrt.
     */
    public sealed partial class ParkingLotToolSystem
    {
        internal Task<ParkingLayout> BereiteFlaechen(Entity lot)
        {
            if (!_bauarbeiter) throw new InvalidOperationException("Kein isolierter Bauarbeiter.");
            if (!ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out _baukontext, out var grund))
                throw new InvalidOperationException(grund);
            var ersatz = ParkingLotFehlendeAssetsSystem.ErsatzFuer(lot);
            if (ersatz != null) _baukontext = _baukontext.MitFlaechen(ersatz.Strasse, ersatz.Deko, ersatz.Zoning);
            if (string.IsNullOrEmpty(_baukontext.Zoningstrasse))
                _baukontext = _baukontext.MitZoningstrasse(ZoningstrasseAusBestand(lot));
            _points.Clear(); _worldPoints.Clear(); _entrances.Clear(); _busStops.Clear();
            _points.AddRange(_baukontext.Punkte.Select(p => p.xz));
            _worldPoints.AddRange(_baukontext.Punkte); _entrances.AddRange(_baukontext.Zugaenge);
            _busStops.AddRange(_baukontext.Bushalte);
            _zoningflaechen.Clear(); _zoningflaechen.AddRange(_baukontext.Zonen ?? Array.Empty<ParkingGeometry.Zoningflaeche>());
            _randzoning.Clear(); _randzoning.AddRange(_baukontext.Randzoning);
            _closed = true;
            LadeZoningBedienwerte(_baukontext.Zettel);
            var settings = LayoutEinstellungen(_baukontext);
            var polygon = _points.ToArray();
            settings.Brueckengrundrisse = Brueckengrundrisse(polygon);
            _areaPreviewSettings = settings;
            _areaPreviewLayout = null;
            return Task.Run(() =>
            {
                var layout = ParkingGeometry.Build(polygon, settings);
                // Wie der Hintergrund-Neubau: die Bandwirkung des Aussenbands.
                var mitWirkung = BandwirkungFuerLauf(settings.Zoningflaechen, layout,
                    settings.Ai, settings.Cw, polygon);
                if (mitWirkung != null)
                {
                    settings.Zoningflaechen = mitWirkung;
                    layout = ParkingGeometry.Build(polygon, settings);
                }
                return layout;
            });
        }

        /**
         * Die Flaechengruppen fuer den vorbereiteten Parkplatz - oder null,
         * solange ein Klon noch entsteht (dann im naechsten Bild wieder).
         * `grund` sagt, worauf gewartet wird.
         */
        internal List<(string Art, float2[][] Ringe, Entity Prefab)> PlaneFlaechen(ParkingLayout layout,
            out string grund)
        {
            grund = null;
            if (!ReferenceEquals(_areaPreviewLayout, layout))
            {
                _areaPreviewLayout = layout;
                ErgaenzeVorflaechen(layout, _areaPreviewSettings, _points.ToArray());
            }
            SyncAreaPreview(prefabsOnly: true);
            if (!_areaPreviewPrefabsReady)
            {
                grund = _areaTransferNote;
                return null;
            }
            var strasseAn = _baukontext.Zettel.SurfaceRoadOn;
            var dekoAn = _baukontext.Zettel.SurfaceDecorationOn;
            GeplanteBauflaechen(layout, strasseAn, dekoAn, _vorflaechenPrefab != Entity.Null,
                out var gras, out var asphalt, out var einzelne, out var belag, out var fahrbahn);
            return Flaechengruppen(layout, strasseAn, dekoAn, gras, asphalt, einzelne, belag, fahrbahn,
                _vorflaechenPrefab);
        }
    }
}
