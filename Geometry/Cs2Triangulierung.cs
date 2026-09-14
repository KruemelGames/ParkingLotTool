using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{

/**
 * CS2s EIGENE TRIANGULIERUNG, ZEILE FUER ZEILE NACHGEBAUT.
 *
 * HERKUNFT, weil sie nicht unsere ist: Vorlage sind
 * `Game.Areas.GeometrySystem.Triangulate`, `Snip` und
 * `Colossal.Mathematics.MathUtils.TriangleIntersectHelper` aus dem Dekompilat
 * des Spiels. Das ist Code von Colossal Order - weder MIT noch GPL, sondern
 * proprietaer. Er steht hier, weil ein Mod das Verhalten des Spiels
 * vorhersagen muss und es dafuer keine Beschreibung gibt, nur das Verhalten
 * selbst. Der Rest dieser Datei ist eigener Code. Siehe AGENTS.md, Abschnitt
 * "Kein fremder Code ohne Herkunft".
 *
 * Am 2026-08-24 hat der Sondentest im Spiel gezeigt, dass unser bisheriges
 * Kriterium falsch war. Wir haben eine Flaeche als "wird verworfen" bewertet,
 * sobald eine Kante oder ein Hals unter 0,375 m lag. Gemessen nimmt CS2 aber
 * einen Hals von 0,0008 m klaglos an - und verwirft dafuer ein Viereck, dessen
 * kuerzeste Kante 0,14 m ist. Keine Laenge, keine Flaeche und kein Winkel
 * erklaerte beides.
 *
 * Das Dekompilat erklaert es: `Game.Areas.GeometrySystem.Triangulate` ist ein
 * EAR-CLIPPING ohne jede Groessenregel. Es gibt genau zwei Arten zu scheitern:
 *
 *   1. `Snip` findet kein abschneidbares Ohr mehr, das Versuchsbudget
 *      (2 x verbleibende Knoten) laeuft leer -> `triangles.Clear()`
 *   2. am Ende sind es nicht genau `n - 2` Dreiecke -> `triangles.Clear()`
 *
 * Beide Male fliegt die GANZE Flaeche weg, nicht nur ein Dreieck. Genau das
 * sieht man im Spiel: nackter Boden statt Gras.
 *
 * Und der Test, an dem ein Ohr scheitert, ist das Kreuzprodukt zweier
 * Kantenvektoren in **float**:
 *
 *     f = (b - a) * (c - a).yx;   if (f.x - f.y < float.Epsilon) return false;
 *
 * Bei einem haarfeinen Ohr loeschen sich die Summanden gegenseitig aus. Weil
 * mit WELTKOORDINATEN gerechnet wird (der Nutzer baut bei x rund -1500), haengt
 * die Ausloeschung von der Entfernung zum Nullpunkt ab - deshalb gibt es keine
 * feste Grenze, und deshalb muss dieser Nachbau mit denselben Koordinaten und
 * demselben `float` rechnen wie das Spiel. Eine Rechnung in `double` waere
 * bequemer und wuerde die Frage nicht beantworten.
 */
public static class Cs2Triangulierung
{
    /**
     * Baut CS2s Dreiecke. Gibt zurueck, wieviele entstanden sind - 0 heisst
     * verworfen, also im Spiel unsichtbar.
     *
     * `edgeBounds` benutzt das Spiel nur als Beschleuniger fuer grosse
     * Polygone; ohne ihn laeuft derselbe Test gegen alle uebrigen Knoten.
     * Dieser Nachbau nimmt bewusst immer den langsamen Zweig: er liefert
     * dasselbe Ergebnis und hat keine Baumtiefe zu raten.
     */
    public static int Dreiecke(float2[] ring, bool gegenUhrzeiger = true)
    {
        if (ring == null || ring.Length < 3) return 0;

        // CS2 speichert den Ring OHNE doppelten Schlusspunkt - im Spiel
        // gemessen: ein 200-Punkte-Kreis kam als 200 Knoten an, obwohl der Mod
        // 201 gesendet hat. Ein Duplikat hier wuerde ein entartetes Ohr
        // erzeugen, das es im Spiel gar nicht gibt.
        var n = ring.Length;
        while (n > 1 && math.all(ring[n - 1] == ring[0])) n--;
        if (n < 3) return 0;

        /*
         * DER SCHRITT, DEN ICH BEIM ERSTEN VERSUCH UEBERSEHEN HABE.
         *
         * `GeometrySystem` triangliert NICHT die gespeicherten Knoten. Es
         * versetzt vorher jeden Knoten um -0,1 m nach INNEN:
         *
         *     nativeArray[i] = AreaUtils.GetExpandedNode(nodes, i, -0.1f, ...)
         *
         * Das Polygon schrumpft also um 10 cm, bevor die Ohren geschnitten
         * werden. Genau deshalb lagen die im Spiel gemessenen Grenzen bei
         * 0,14 und 0,16 m - in der Groessenordnung dieses Versatzes - und
         * genau deshalb hat mein erster Nachbau ohne diesen Schritt alles bis
         * 0,0005 m angenommen und die Gegenprobe nicht bestanden.
         *
         * Fuer uns ist das die eigentliche Konstruktionsregel: eine Flaeche
         * muss einen Versatz von 0,1 m nach innen ueberleben, ohne sich selbst
         * zu durchdringen. Nicht "Kante ueber X Meter".
         */
        var knoten = new float2[n];
        for (var i = 0; i < n; i++)
            knoten[i] = VersetzterKnoten(ring, n, i, -0.1f, gegenUhrzeiger);

        var soll = n - 2;
        var index = new Eintrag[n];
        if (gegenUhrzeiger)
        {
            for (var i = 0; i < n; i++)
            {
                var vor = i == 0 ? n - 1 : i - 1;
                var nach = i + 1 == n ? 0 : i + 1;
                index[i] = new Eintrag
                {
                    Knoten = i, Vorher = vor, Nachher = nach, Ueberspringen = nach,
                };
            }
        }
        else
        {
            for (var j = 0; j < n; j++)
            {
                var vor = j == 0 ? n - 1 : j - 1;
                var nach = j + 1 == n ? 0 : j + 1;
                index[j] = new Eintrag
                {
                    Knoten = n - 1 - j, Vorher = vor, Nachher = nach,
                    Ueberspringen = nach,
                };
            }
        }

        var uebrig = n;
        var budget = 2 * uebrig;
        var vorletzter = uebrig - 2;
        var aktuell = uebrig - 1;
        var gebaut = 0;

        while (uebrig > 2)
        {
            if (0 >= budget--) return 0;      // Budget leer -> alles verworfen

            var e0 = index[aktuell];
            var e1 = index[e0.Nachher];
            var e2 = index[e1.Nachher];

            if (Schneidbar(knoten, e0, e1, e2, uebrig, index))
            {
                if (gebaut == soll) return 0; // mehr Dreiecke als moeglich
                gebaut++;
                e0.Ueberspringen = e0.Ueberspringen == e0.Nachher
                    ? e1.Ueberspringen : e0.Ueberspringen;
                e0.Nachher = e1.Nachher;
                e2.Vorher = aktuell;
                index[aktuell] = e0;
                index[e1.Nachher] = e2;
                if (vorletzter != e0.Vorher)
                {
                    var a = index[vorletzter];
                    var b = index[e0.Vorher];
                    a.Ueberspringen = e0.Vorher;
                    b.Ueberspringen = aktuell;
                    index[vorletzter] = a;
                    index[e0.Vorher] = b;
                }
                vorletzter = aktuell;
                budget = 2 * --uebrig;
            }
            aktuell = e0.Ueberspringen;
        }

        // Nicht genau n-2 Dreiecke heisst bei CS2 ebenfalls: alles verwerfen.
        return gebaut == soll ? gebaut : 0;
    }

    /**
     * `AreaUtils.GetExpandedNode` fuer den geschlossenen Ring (isComplete).
     *
     * Winkelhalbierende an der Ecke, laenger gemacht um 1/tan(halber Winkel) -
     * das ist die uebliche Gehrung. Bei fast gestreckten Ecken (tan unter
     * 0,001) laesst CS2 die Verlaengerung weg, sonst liefe sie ins Unendliche.
     */
    /**
     * Der Ring, wie CS2 ihn TATSAECHLICH trianguliert - 0,1 m nach innen.
     *
     * Wer wissen will, WARUM eine Flaeche verworfen wird, muss diesen Ring
     * ansehen, nicht den gesendeten. Am 2026-09-01 lag ein Belagring mit
     * 306 m2, sechs Punkten, ohne Einschnuerung und ohne Selbstschnitt vor -
     * und CS2 warf ihn weg. Erst der versetzte Ring zeigt weshalb.
     */
    public static float2[] VersetzterRing(float2[] ring,
                                          bool gegenUhrzeiger = true)
    {
        if (ring == null || ring.Length < 3) return ring;
        var n = ring.Length;
        while (n > 1 && math.all(ring[n - 1] == ring[0])) n--;
        if (n < 3) return ring;
        var ausgabe = new float2[n];
        for (var i = 0; i < n; i++)
            ausgabe[i] = VersetzterKnoten(ring, n, i, -0.1f, gegenUhrzeiger);
        return ausgabe;
    }

    private static float2 VersetzterKnoten(float2[] ring, int n, int i,
                                           float betrag, bool gegenUhrzeiger)
    {
        var vorher = ring[i == 0 ? n - 1 : i - 1];
        var nachher = ring[i + 1 == n ? 0 : i + 1];
        var hier = ring[i];
        var a = math.normalizesafe(vorher - hier);
        var b = math.normalizesafe(nachher - hier);
        // MathUtils.Right(v) = (v.y, -v.x); Left(v) = (-v.y, v.x)
        var senkrecht = gegenUhrzeiger
            ? new float2(-a.y, a.x)
            : new float2(a.y, -a.x);
        var winkel = math.acos(math.clamp(math.dot(a, b), -1f, 1f));
        var zeichen = math.sign(math.dot(senkrecht, b));
        var tan = math.tan(winkel * 0.5f);
        senkrecht += a * (tan < 0.001f ? 0f : zeichen / tan);
        return hier + senkrecht * betrag;
    }

    private struct Eintrag
    {
        internal int Knoten;
        internal int Vorher;
        internal int Nachher;
        internal int Ueberspringen;
    }

    /**
     * `GeometrySystem.Snip`, ohne den edgeBounds-Beschleuniger.
     *
     * Der erste Test ist der entscheidende: das Kreuzprodukt in float. Der
     * zweite prueft, ob ein anderer Knoten im Ohr liegt - dann waere es keins.
     */
    private static bool Schneidbar(float2[] knoten, Eintrag e0, Eintrag e1,
                                   Eintrag e2, int uebrig, Eintrag[] index)
    {
        var a = knoten[e0.Knoten];
        var b = knoten[e1.Knoten];
        var c = knoten[e2.Knoten];

        // Bewusst dieselbe Schreibweise wie im Dekompilat: die Reihenfolge der
        // Multiplikationen entscheidet in float ueber das letzte Bit, und
        // genau dieses Bit ist hier die ganze Frage.
        var f = (b - a) * new float2((c - a).y, (c - a).x);
        if (f.x - f.y < float.Epsilon) return false;

        var lauf = index[e2.Nachher];
        for (var j = 3; j < uebrig; j++)
        {
            if (ImDreieck(a, b, c, knoten[lauf.Knoten])) return false;
            lauf = index[lauf.Nachher];
        }
        return true;
    }

    /** `MathUtils.TriangleIntersectHelper`, unveraendert uebernommen. */
    private static bool ImDreieck(float2 a, float2 b, float2 c, float2 p)
    {
        var x = new float3(a.x, b.x, c.x);
        var y = new float3(a.y, b.y, c.y);
        var f = (p.x - x) * (new float3(y.y, y.z, y.x) - y)
              + (p.y - y) * (x - new float3(x.y, x.z, x.x));
        return (math.all(f >= 0f) || math.all(f <= 0f))
            && math.any(p.x <= x) && math.any(p.y <= y)
            && math.any(p.x >= x) && math.any(p.y >= y);
    }
}
}
