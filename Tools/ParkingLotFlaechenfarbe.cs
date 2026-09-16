using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /**
     * WELCHE FARBE HAT EINE FLAECHE?
     *
     * WOZU. Die Vorschau fuellt ihre Flaechen wirklich, seit sie ein eigenes
     * Dreiecksnetz zeichnet - bis zum 2026-09-16 aber in festen Farben: Gras
     * gruen, Belag grau. Waehlt der Nutzer als Dekoflaeche Sand, sah die
     * Vorschau trotzdem gruen aus, und das Ergebnis war dann ein anderes als
     * das Bild davor. Seine Ansage: *"so dass sich die Preview direkt dem
     * anpasst was in der Surface-Auswahl eingestellt wurde. Natuerlich auch
     * vorher schon bevor gebaut wird."*
     *
     * DREI QUELLEN, IN DIESER REIHENFOLGE:
     *
     *   1. DIE TABELLE unten. Ausgemessen aus den Icons der Asset Icon
     *      Library: fuer jedes der 17 Flaechen-Prefabs des Grundspiels die
     *      Farbe mit der groessten gewichteten Praesenz in der SVG - wie oft
     *      sie vorkommt, mal der Laenge des Pfades, in dem sie steht. Ein
     *      grosser Fuellpfad zaehlt damit mehr als ein Zierstrich. Kein
     *      Mittelwert: ein Mittel aus Gruen und Hellblau ist grau und sagt
     *      nichts.
     *
     *   2. WAS DIE OBERFLAECHE MELDET. Fuer Flaechen aus fremden Mods steht
     *      nichts in der Tabelle. Das Panel kennt die Adresse ihres Icons,
     *      kann es lesen und dieselbe Rechnung anstellen; was dabei
     *      herauskommt, landet ueber `Merke` hier.
     *
     *   3. DIE BASISTEXTUR des Prefabs. Der letzte Rueckfall, wenn es kein
     *      Icon gibt oder die Bibliothek gar nicht installiert ist.
     *
     * Und wenn alles fehlschlaegt, bleibt die Vorgabe des Aufrufers.
     */
    internal static class ParkingLotFlaechenfarbe
    {
        /**
         * DIE AUSGEMESSENEN ICONFARBEN.
         *
         * Gewonnen am 2026-09-16 aus
         * `.cache/Mods/mods_subscribed/79634_22/.Thumbnails/Pack.zip`,
         * darin `Pack_11.zip`, darin die 17 Dateien
         * `SurfacePrefab.<Name>.svg`. Ausgeschlossen wurde `#d1f8fd` - das
         * steckt in ueber 60 % aller Icons und ist Zierwerk des Stils, nicht
         * die Flaeche.
         *
         * Vier Werte liegen dicht beieinander: Oil und Ore sind beide fast
         * schwarz, Concrete 01, Landfill und die hellen Tiles alle nahe
         * weiss. Das ist keine Schwaeche der Messung - die sehen im Spiel
         * genauso aehnlich aus.
         */
        private static readonly Dictionary<string, Color> Tabelle =
            new Dictionary<string, Color>
            {
                { "Agriculture Surface 01", Aus("b97a48") },
                { "Clip Surface",           Aus("ab4835") },
                { "Concrete Surface 01",    Aus("ebebeb") },
                { "Concrete Surface 02",    Aus("778496") },
                { "Forestry Surface 01",    Aus("265b2a") },
                { "Grass Surface 01",       Aus("2e9141") },
                { "Grass Surface 02",       Aus("9ad760") },
                { "Landfill Surface 01",    Aus("e7e6e6") },
                { "Oil Surface 01",         Aus("101317") },
                { "Ore Surface 01",         Aus("101213") },
                { "Pavement Surface 01",    Aus("292e36") },
                { "Pavement Surface 02",    Aus("8e8e8e") },
                { "Sand Surface 01",        Aus("dcad52") },
                /*
                 * NICHT DIE ICONFARBE - das Icon zeigt Sand, die Flaeche ist
                 * KIES. Der Nutzer am 2026-09-16: *"Sand Surface 02 muesste
                 * grau sein weil es eigentlich Gravel ist."* Aus dem Icon
                 * kaeme `#f3c56c`, und das waere im Bild schlicht falsch.
                 *
                 * Das ist die Grenze des Icon-Wegs: das Symbol muss nicht
                 * zeigen, was am Boden liegt. Wo beides auseinanderfaellt,
                 * gilt der Boden - die Texturmessung wird den Wert
                 * ueberschreiben, sobald sie greift.
                 */
                { "Sand Surface 02",        Aus("8d8a82") },
                { "Tiles Surface 01",       Aus("eceff1") },
                { "Tiles Surface 02",       Aus("b0bec5") },
                { "Tiles Surface 03",       Aus("e0e0e0") },
            };

        /** Was die Oberflaeche aus fremden Icons gelesen hat. */
        private static readonly Dictionary<string, Color> Gemeldet =
            new Dictionary<string, Color>();

        /** Was aus der Basistextur gemessen wurde. */
        private static readonly Dictionary<string, Color> Gemessen =
            new Dictionary<string, Color>();

        private static Color Aus(string hex)
            => new Color(
                int.Parse(hex.Substring(0, 2), System.Globalization
                    .NumberStyles.HexNumber) / 255f,
                int.Parse(hex.Substring(2, 2), System.Globalization
                    .NumberStyles.HexNumber) / 255f,
                int.Parse(hex.Substring(4, 2), System.Globalization
                    .NumberStyles.HexNumber) / 255f,
                1f);

        /** Vom Panel: die aus einem Icon gelesene Farbe einer Flaeche. */
        internal static void Merke(string name, string hex)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(hex)) return;
            var sauber = hex.TrimStart('#').ToLowerInvariant();
            if (sauber.Length != 6) return;
            try
            {
                Gemeldet[name] = Aus(sauber);
                Mod.log.Info("PLT-Flaechenfarbe: '" + name
                    + "' aus dem Icon = #" + sauber);
            }
            catch (System.Exception)
            {
                // Ein unlesbarer Wert aus der Oberflaeche ist kein Grund,
                // etwas abzubrechen - dann gilt eben die Tabelle.
            }
        }

        /**
         * Die Farbe einer Flaeche, oder `vorgabe`, wenn nichts zu holen ist.
         *
         * `alpha` kommt vom Aufrufer: die Vorschau faerbt den Boden ein, sie
         * verdeckt ihn nicht. Der Farbton entscheidet ueber das Aussehen, die
         * Deckung ueber die Lesbarkeit - und die soll nicht davon abhaengen,
         * welche Flaeche gerade gewaehlt ist.
         */
        internal static Color Hole(string name, Color vorgabe, float alpha)
        {
            var farbe = vorgabe;
            if (!string.IsNullOrEmpty(name))
            {
                if (Gemeldet.TryGetValue(name, out var ausIcon)) farbe = ausIcon;
                else if (Tabelle.TryGetValue(name, out var ausTabelle))
                    farbe = ausTabelle;
                else if (Gemessen.TryGetValue(name, out var ausTextur))
                    farbe = ausTextur;
            }
            farbe = Sichtbar(farbe);
            farbe.a = Deckung(farbe, alpha);
            return farbe;
        }

        /**
         * FARBLOSE FLAECHEN BRAUCHEN MEHR DECKUNG ALS BUNTE.
         *
         * Eine gruene Fuellung auf Gras erkennt man bei 20 % sofort - der
         * Farbton allein sagt schon alles. Eine graue Fuellung auf grauem
         * Boden sagt bei 20 % gar nichts: der Nutzer am 2026-09-16 hielt
         * Pavement 01 fuer ABGESCHALTET, weil man es nicht sah.
         *
         * Die Helligkeit anzuheben half dagegen nicht, und das war
         * absehbar - ein helleres Grau auf Grau ist immer noch Grau. Was
         * fehlt, ist Kontrast, und den gibt es hier nur ueber die Deckung.
         *
         * Also haengt sie an der SAETTIGUNG: was Farbe hat, bleibt bei dem
         * Wert, den der Nutzer fuer das Gruen ausdruecklich als angenehm
         * bezeichnet hat. Je farbloser, desto mehr Deckung, bis knapp unter
         * das Doppelte. Ein Wert fuer alle waere entweder fuer das Gruen zu
         * laut oder fuer den Belag zu leise.
         */
        private static float Deckung(Color farbe, float grundwert)
        {
            var max = Mathf.Max(farbe.r, Mathf.Max(farbe.g, farbe.b));
            var min = Mathf.Min(farbe.r, Mathf.Min(farbe.g, farbe.b));
            var saettigung = max < 1e-4f ? 0f : (max - min) / max;
            // Ab 0,45 Saettigung traegt der Farbton allein; darunter wird
            // linear zugegeben.
            var zugabe = Mathf.Max(0f, 0.45f - saettigung) * 0.5f;
            return Mathf.Clamp01(grundwert + zugabe);
        }

        /**
         * HEBT SEHR DUNKLE FARBEN AUF EINE SICHTBARE HELLIGKEIT.
         *
         * Eine Fuellung mit 22 % Deckung faerbt den Boden ein. Ist die Farbe
         * fast schwarz, faerbt sie ihn um fast nichts - und die Flaeche sieht
         * aus, als waere sie gar nicht da. Genau das hat der Nutzer am
         * 2026-09-16 gemeldet: Pavement 01, Forestry, Oil und Ore wurden
         * nicht angezeigt. Das sind die vier dunkelsten Werte der Tabelle.
         *
         * Angehoben wird die HELLIGKEIT, nicht die Deckung: die Deckung ist
         * fuer alle Ebenen gleich, damit keine Sorte lauter ist als die
         * andere. Und angehoben wird der Farbton als Ganzes, damit Oil
         * blaeulich und Forestry gruen bleibt - eine Vorschau soll die Sorte
         * erkennen lassen, nicht die Leuchtdichte nachstellen.
         */
        private static Color Sichtbar(Color farbe)
        {
            /*
             * DIE GRENZE GILT IN sRGB, GEBRAUCHT WIRD SIE LINEAR.
             *
             * Der Shader bekommt die Farbe als LINEAREN Wert - die Vorschau
             * rechnet sie vorher um. Und diese Umrechnung drueckt dunkle
             * Toene weit staerker als helle: sRGB 0,38 sind linear nur noch
             * 0,12, waehrend das Gruen, das der Nutzer als angenehm bezeichnet
             * hat, in seinem Hauptkanal bei 0,27 liegt.
             *
             * Meine erste Grenze von 0,30 und auch die 0,38 waren deshalb
             * viel zu niedrig: Pavement 01 kam als grauer Hauch auf gruenem
             * Boden heraus, und der Nutzer hielt die Flaeche fuer
             * abgeschaltet - *"gefuehlt sah es aus wie 5 % Transparenz."*
             *
             * 0,50 in sRGB sind rund 0,21 linear und liegen damit in
             * derselben Groessenordnung wie das Gruen. Das ist der Wert, auf
             * den es ankommt.
             */
            const float untergrenze = 0.50f;
            var helligkeit = 0.2126f * farbe.r + 0.7152f * farbe.g
                + 0.0722f * farbe.b;
            if (helligkeit >= untergrenze) return farbe;
            if (helligkeit < 1e-4f)
                return new Color(untergrenze, untergrenze, untergrenze, farbe.a);
            var faktor = untergrenze / helligkeit;
            return new Color(
                Mathf.Min(1f, farbe.r * faktor),
                Mathf.Min(1f, farbe.g * faktor),
                Mathf.Min(1f, farbe.b * faktor),
                farbe.a);
        }

        /** Kennen wir diese Flaeche schon? Dann lohnt keine Texturmessung. */
        internal static bool Bekannt(string name)
            => !string.IsNullOrEmpty(name)
               && (Gemeldet.ContainsKey(name) || Tabelle.ContainsKey(name)
                   || Gemessen.ContainsKey(name));

        /**
         * Misst die Basistextur - der letzte Rueckfall.
         *
         * `false` heisst: nicht messbar, weil die Textur nicht geladen ist.
         * `AssetReference.Load()` wuerde sie von der Platte holen; das waere
         * fuer siebzehn und mehr Flaechen Speicher fuer nichts. Beim
         * naechsten Durchgang ist sie vielleicht da.
         */
        internal static bool MissTextur(PrefabBase prefab, string name)
        {
            if (prefab == null || string.IsNullOrEmpty(name)) return false;
            if (Gemessen.ContainsKey(name)) return true;

            var gerendert = prefab.GetComponent<RenderedArea>();
            if (gerendert == null) return false;
            var textur = LadeTextur(gerendert);
            if (textur == null) return false;

            var ton = gerendert.m_BaseColor;
            var mittel = Mittelwert(textur);
            Gemessen[name] = new Color(mittel.r * ton.r, mittel.g * ton.g,
                mittel.b * ton.b, 1f);
            Mod.log.Info("PLT-Flaechenfarbe: '" + name + "' aus der Textur = "
                + Hex(Gemessen[name]));
            return true;
        }

        private static Texture LadeTextur(RenderedArea gerendert)
        {
            try
            {
                /*
                 * DER VERWEIS WIRD ZUM ASSET - implizit.
                 *
                 * `AssetReference<TextureAsset>` selbst kennt weder
                 * `isObjectLoaded` noch `Load`; beides sitzt am
                 * `TextureAsset`, in das der Verweis sich umwandelt. So macht
                 * es auch `AreaBatchSystem.Initialize` im Dekompilat.
                 */
                TextureAsset asset = gerendert.m_BaseColorMap;
                if (asset == null || !asset.isObjectLoaded) return null;
                return asset.Load();
            }
            catch (System.Exception e)
            {
                Mod.log.Warn("PLT-Flaechenfarbe: Textur nicht lesbar - "
                    + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        /**
         * Mittelt eine Textur ueber den Umweg einer RenderTexture.
         *
         * 8x8 ist Absicht: gross genug, dass ein Muster nicht ein einzelnes
         * Texel zum Ergebnis macht, klein genug, dass das Zurueckholen aus
         * dem Grafikspeicher nicht auffaellt. Der Umweg ist noetig, weil
         * Spieltexturen nicht CPU-lesbar sind.
         */
        private static Color Mittelwert(Texture textur)
        {
            const int kante = 8;
            RenderTexture ziel = null;
            Texture2D lese = null;
            var vorher = RenderTexture.active;
            try
            {
                ziel = RenderTexture.GetTemporary(kante, kante, 0,
                    RenderTextureFormat.ARGB32);
                Graphics.Blit(textur, ziel);
                RenderTexture.active = ziel;
                lese = new Texture2D(kante, kante, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                lese.ReadPixels(new Rect(0, 0, kante, kante), 0, 0);
                lese.Apply(false, false);

                var summe = Color.black;
                var pixel = lese.GetPixels();
                foreach (var p in pixel) summe += p;
                return summe / Mathf.Max(1, pixel.Length);
            }
            catch (System.Exception e)
            {
                Mod.log.Warn("PLT-Flaechenfarbe: Mittelwert fehlgeschlagen - "
                    + e.GetType().Name + ": " + e.Message);
                return Color.white;
            }
            finally
            {
                RenderTexture.active = vorher;
                if (lese != null) Object.Destroy(lese);
                if (ziel != null) RenderTexture.ReleaseTemporary(ziel);
            }
        }

        /** `#rrggbb` - zum Ablesen im Log. */
        internal static string Hex(Color farbe)
            => "#" + ColorUtility.ToHtmlStringRGB(farbe).ToLowerInvariant();
    }
}
