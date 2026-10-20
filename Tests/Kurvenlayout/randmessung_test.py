"""Prueft die Welt-float-Rundreise des unabhaengigen Messgeraets."""
import sys
from shapely.geometry import LineString, Point
from randmessung import welt_float, pruefe_abnahme, graskeil


def main():
    # Originale float32 liegen exakt auf einer Geraden. Ihre kuerzesten
    # JSON-Dezimaldarstellungen sind als double nicht kollinear.
    p = [[1300, 4600], [1300.0004, 4600.0015], [1300.0001, 4600.0005]]
    assert LineString(p[:2]).distance(Point(p[2])) > 0.00001
    q = welt_float(dict(Polygon=p))["Polygon"]
    assert LineString(q[:2]).distance(Point(q[2])) == 0
    assert q[1] == [1300.0003662109375, 4600.00146484375]
    assert welt_float(dict(Polygon=q))["Polygon"] == q
    alt = dict(Strasse=False, Fehler="bekannter Abbruch", AltAbdruck="gleich", NeuAbdruck="gleich")
    assert not pruefe_abnahme(alt)["Fehler"]
    assert pruefe_abnahme(alt)["AltpfadBefunde"]
    assert pruefe_abnahme(dict(alt, NeuAbdruck="anders"))["Fehler"]
    assert pruefe_abnahme(dict(alt, AltAbdruck=None))["Fehler"]
    assert pruefe_abnahme(dict(alt, Strasse=True))["Fehler"]
    assert not graskeil(1, 0.999)
    assert graskeil(1, 1.0)
    assert graskeil(1, 1.001)
    assert not graskeil(-1, 2)
    assert not graskeil(0, 2)
    assert not graskeil(1, 0)
    print("Randmessgeraet: 15 Pruefungen, 0 Fehler; Float-Rundreise und Keilmaterial geprueft.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:
        print(e, file=sys.stderr)
        sys.exit(1)
