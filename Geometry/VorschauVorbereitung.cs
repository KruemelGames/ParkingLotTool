using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;

namespace ParkingLotTool.Geometry
{
    // Ein Speicher je Werkzeug, keine globale oder gespeicherte Bauabsicht.
    // Nur reine Geometrie wird geteilt. Zell-/Materialplaene haengen schon
    // vor dem Zufahrtsbauer von den Zufahrten ab und bleiben pro Lauf neu.
    public sealed class VorschauVorbereitung
    {
        private readonly object _sperre = new object();
        internal long WiederverwendeteZerlegungen { get; private set; }
        internal long WiederverwendeteVersaetze { get; private set; }
        private Punkt[] _punkte;
        private Polygon _areal;
        private List<Polygon> _teile;
        private int _knoten, _linien;
        private readonly Dictionary<long, IReadOnlyList<Punkt>> _innenraender
            = new Dictionary<long, IReadOnlyList<Punkt>>();

        internal void Zerlege(Punkt[] punkte, Knotenfabrik knoten, Linienregister linien,
            out Polygon areal, out List<Polygon> teile)
        {
            lock (_sperre)
            {
                bool gleich = _punkte != null && _punkte.Length == punkte.Length;
                for (int i = 0; gleich && i < punkte.Length; i++)
                    gleich = BitConverter.DoubleToInt64Bits(_punkte[i].X) == BitConverter.DoubleToInt64Bits(punkte[i].X)
                        && BitConverter.DoubleToInt64Bits(_punkte[i].Y) == BitConverter.DoubleToInt64Bits(punkte[i].Y);
                if (!gleich)
                {
                    var a = Polygonfabrik.Areal(punkte, knoten, linien);
                    var t = new Konvexzerlegung(knoten, linien).Zerlege(a);
                    // Erst nach erfolgreicher Rechnung veroeffentlichen.
                    _punkte = punkte.ToArray();
                    _areal = a; _teile = t;
                    _knoten = knoten.Anzahl; _linien = linien.Anzahl;
                    _innenraender.Clear();
                }
                else
                {
                    WiederverwendeteZerlegungen++;
                    knoten.Fortsetzen(_knoten);
                    linien.Fortsetzen(_linien);
                }
                // Nachbarkanten werden spaeter ergaenzt: jede Polygonliste
                // gehoert dem aktuellen Lauf. Knoten/Linien sind unveraenderlich.
                var kopie = new Polygon(_areal.Ecken);
                areal = kopie;
                teile = _teile.Select(p => ReferenceEquals(p, _areal) ? kopie : new Polygon(p.Ecken)).ToList();
            }
        }

        internal IReadOnlyList<Punkt> Innenrand(Punkt[] punkte, double abstand)
        {
            lock (_sperre)
            {
                // Ein anderer paralleler Lauf darf den letzten Stand ersetzen;
                // dann rechnen wir lokal weiter, ohne fremde Ringe zu benutzen.
                bool gleich = _punkte != null && _punkte.Length == punkte.Length;
                for (int i = 0; gleich && i < punkte.Length; i++)
                    gleich = BitConverter.DoubleToInt64Bits(_punkte[i].X) == BitConverter.DoubleToInt64Bits(punkte[i].X)
                        && BitConverter.DoubleToInt64Bits(_punkte[i].Y) == BitConverter.DoubleToInt64Bits(punkte[i].Y);
                if (!gleich) return Layoutplanung.Innenrand(punkte, abstand);
                long schluessel = BitConverter.DoubleToInt64Bits(abstand);
                if (!_innenraender.TryGetValue(schluessel, out var innen))
                {
                    innen = Layoutplanung.Innenrand(punkte, abstand).ToArray();
                    // Reglerbewegungen duerfen keinen unbegrenzten Speicher bilden.
                    if (_innenraender.Count == 16) _innenraender.Clear();
                    _innenraender.Add(schluessel, innen);
                }
                else WiederverwendeteVersaetze++;
                return innen.ToArray();
            }
        }
    }
}
