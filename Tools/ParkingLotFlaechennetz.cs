using System.Collections.Generic;
using Game;
using Game.Simulation;
using Unity.Entities;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * DIE GEFUELLTEN FLAECHEN DER VORSCHAU.
     *
     * WARUM ES UEBERHAUPT SEIN MUSS. Der Nutzer am 2026-09-15: *"Ich bin
     * naemlich allgemein immer noch mit der Preview unzufrieden dass alles nur
     * Rechtecke ist."* Er hat recht, und es liegt nicht an uns:
     *
     *   - `OverlayRenderSystem.Buffer` kennt Kreis, Strich, Kurve, Text und
     *     `CustomMesh` mit Cylinder/Arrow/Plane. Keine Flaechenfuellung.
     *   - `AreaBatchSystem`, das CS2s gefuellte Flaechen baut, schliesst
     *     `Temp` in BEIDEN Abfragen aus (Zeilen 1529 und 1534).
     *     `AreaBorderRenderSystem` nimmt `Temp` dagegen mit (Zeile 643).
     *
     * Deshalb ist jede Vorschauflaeche im Spiel ein Rand und sonst nichts.
     * Das ist die Bauart des Spiels, keine Einstellung.
     *
     * WAS VORHER DA WAR UND WARUM ES WEG IST. `ParkingSurfaceStrips` hat jede
     * Flaeche in 4 m breite Abtaststreifen zerlegt und diese als projizierte
     * Striche gezeichnet. Gemessen (`--flaechenannahme` mit `PLT_STREIFEN=1`)
     * deckt das die Flaeche exakt ab - 278 Entwurfsteile, 278 Streifen,
     * 0,0 % Fehler. Nur besteht das Bild eben aus 278 einzelnen
     * durchscheinenden Rechtecken, und jede Kante zwischen zweien ist zu
     * sehen. Genau das ist der Eindruck "alles nur Rechtecke".
     *
     * Ein Dreiecksnetz hat diese Naehte nicht. Zerlegt wird mit
     * `Cs2Triangulierung` - demselben Nachbau von CS2s Ear-Clipping, mit dem
     * wir schon vorhersagen, ob das Spiel eine Flaeche annimmt. Die Vorschau
     * zeigt damit nicht irgendeine Fuellung, sondern GENAU die Dreiecke, die
     * nach dem Bauen dort liegen werden.
     *
     * KEIN SCHALTER. Der Vorversuch hing an Alt+F; seit dem 2026-09-15 laeuft
     * das Netz immer mit, sobald eine Vorschau steht. Ansage des Nutzers:
     * *"gerne so einbauen dass es direkt drin ist ohne alt f."*
     */
    public sealed partial class ParkingLotFlaechennetzSystem : GameSystemBase
    {
        /**
         * WELCHES MATERIAL - DAS WAR DIE EIGENTLICHE FRAGE.
         *
         * CS2 laeuft auf HDRP. Ob ein bestimmter Shader im ausgelieferten
         * Spiel ueberhaupt enthalten ist, sieht man von aussen nicht - also
         * wird der Reihe nach probiert und JEDER Versuch gemeldet. Die Liste
         * ist die Antwort auf die Frage, nicht eine Vermutung darueber.
         * Gemessen im Spiel: `HDRP/Unlit` ist da.
         *
         * Reihenfolge: erst das, was in HDRP gedacht ist, dann die
         * eingebauten Notnaegel, die Unity fast immer mitliefert.
         */
        private static readonly string[] Kandidaten =
        {
            "HDRP/Unlit",
            "Shader Graphs/Unlit",
            "Unlit/Transparent",
            "Unlit/Color",
            "Sprites/Default",
            "UI/Default",
            "Hidden/Internal-Colored",
        };

        private Material _material;
        private string _materialname = "(keins)";

        /**
         * CS2s EIGENES OVERLAY-MATERIAL - der Weg, der ohne Tricks auskommt.
         *
         * Unser eigenes HDRP-Material hat jede gesaettigte Farbe auf einen
         * reinen Grundton gezogen; im Spiel gemessen kam aus `#4ca64c`
         * `#00c400`, waehrend neutrales Grau sauber ankam. Das Spiel selbst
         * hat dieses Problem nicht - also nehmen wir sein Material.
         *
         * Die Farbe steht dort nicht am Material, sondern in einem Puffer je
         * gezeichnetem Koerper. Ein Eintrag reicht: unser Netz liegt bereits
         * in Weltkoordinaten, die Matrix ist also die Einheitsmatrix.
         */
        private Material _cs2Material;
        private ComputeBuffer _cs2Puffer;
        private ComputeBuffer _cs2Argumente;
        private readonly uint[] _cs2Args = new uint[5];
        private int _cs2Versuche;
        private bool _cs2Gemeldet;
        private Color _farbe = new Color(-1f, -1f, -1f, -1f);
        private Mesh _netz;
        private TerrainSystem _terrain;

        /** Knapp ueber dem Boden - zu wenig flimmert, zu viel schwebt. */
        private const float Schwebe = 0.15f;

        /**
         * LAENGSTE KANTE, BEVOR EIN DREIECK GETEILT WIRD.
         *
         * Ein Dreieck ist flach, das Gelaende ist es nicht. Ohne Unterteilung
         * schneidet eine grosse Flaeche am Hang in den Boden - im Vorversuch
         * stand das bewusst als bekannte Grenze im Log. 8 m ist der Wert, bei
         * dem eine Boeschung noch sauber aussieht und die Dreieckszahl im
         * niedrigen Tausenderbereich bleibt.
         */
        private const float MaxKante = 8f;

        /** Notbremse, damit eine entartete Flaeche nicht den Bildaufbau frisst. */
        private const int MaxDreiecke = 60000;

        /**
         * Kann das Netz ueberhaupt zeichnen?
         *
         * Die Vorschau fragt das, BEVOR sie ihre eigene Streifenfuellung
         * weglaesst. Findet sich kein Shader, bleiben die Streifen - ein
         * Werkzeug, das nichts anzeigt, waere schlimmer als eines mit Naehten.
         */
        internal bool Einsatzbereit => _material != null;

        private readonly struct Dreieck
        {
            internal Dreieck(float2 a, float2 b, float2 c) { A = a; B = b; C = c; }
            internal float2 A { get; }
            internal float2 B { get; }
            internal float2 C { get; }
        }

        private readonly List<Dreieck> _stapel = new List<Dreieck>();

        private int _letzteDreiecke = -1;
        private int _letzteFlaechen = -1;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            HoleMaterial();
        }

        /**
         * Sucht EINMAL ein brauchbares Material und sagt im Log, welches.
         *
         * Alle Kandidaten werden geprueft, nicht nur bis zum ersten Treffer:
         * als sich `HDRP/Unlit` im ersten Anlauf als stur weiss herausstellte,
         * fehlte genau die Auskunft, welche Ausweiche es im Spiel ueberhaupt
         * gibt. Die vollstaendige Liste kostet Millisekunden, einmal.
         */
        private void HoleMaterial()
        {
            if (_material != null) return;
            var versuche = new List<string>();
            Shader gewaehlt = null;
            foreach (var name in Kandidaten)
            {
                var shader = Shader.Find(name);
                versuche.Add(name + (shader == null ? "=nein" : "=JA"));
                if (shader == null || gewaehlt != null) continue;
                gewaehlt = shader;
                _materialname = name;
            }
            Mod.log.Info("PLT-Flaechennetz MATERIAL: " + _materialname
                + "; geprueft: " + string.Join(", ", versuche.ToArray()));
            MeldeShaderAuswahl();
            if (gewaehlt == null)
            {
                Mod.log.Warn("PLT-Flaechennetz: KEIN brauchbarer Shader im "
                    + "Spiel gefunden. Die Vorschau bleibt bei ihrer "
                    + "Streifenfuellung - sichtbar, aber mit Naehten.");
                return;
            }
            _material = new Material(gewaehlt)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        /**
         * Holt CS2s Overlay-Material und haengt unseren Datenpuffer daran.
         *
         * Lazy, nicht in `OnCreate`: die Prefabs sind beim Anlegen des
         * Systems noch nicht geladen. Nach ein paar erfolglosen Versuchen
         * wird aufgegeben und bei unserem eigenen Material geblieben - sonst
         * fragt man in jedem Bild eine Abfrage an, die nie etwas liefert.
         */
        private bool HoleCs2Material(Color farbe)
        {
            if (_cs2Material == null)
            {
                if (_cs2Versuche > 20) return false;
                _cs2Versuche++;
                try
                {
                    var prefabs = World
                        .GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>();
                    var abfrage = GetEntityQuery(ComponentType.ReadOnly<
                        Game.Prefabs.OverlayConfigurationData>());
                    if (abfrage.IsEmptyIgnoreFilter) return false;
                    var konfig = prefabs.GetSingletonPrefab<
                        Game.Prefabs.OverlayConfigurationPrefab>(abfrage);
                    if (konfig == null || konfig.m_SolidObjectMaterial == null)
                        return false;
                    _cs2Material = new Material(konfig.m_SolidObjectMaterial)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        name = "PLT Flaechennetz",
                    };
                }
                catch (System.Exception e)
                {
                    if (!_cs2Gemeldet)
                    {
                        _cs2Gemeldet = true;
                        Mod.log.Warn("PLT-Flaechennetz: CS2s Overlay-Material "
                            + "nicht erreichbar (" + e.GetType().Name + ": "
                            + e.Message + "). Es bleibt beim eigenen.");
                    }
                    _cs2Versuche = 999;
                    return false;
                }
            }

            /*
             * DER PUFFER TRAEGT DIE FARBE.
             *
             * Genau die Felder, die `OverlayRenderSystem` fuellt. `Plane` ist
             * die Sorte, mit der das Spiel flache Koerper zeichnet; die
             * Groesse bleibt 1, weil unser Netz keine Einheitsform ist,
             * sondern schon die fertige Flaeche.
             */
            var daten = new Game.Rendering.OverlayRenderSystem.CustomMeshdData
            {
                m_Matrix = Matrix4x4.identity,
                m_InverseMatrix = Matrix4x4.identity,
                /*
                 * LINEAR - AM BILDSCHIRM AUSGERECHNET, NICHT GERATEN.
                 *
                 * Messung des Nutzers am 2026-09-15 mit CS2s Overlay-Material:
                 * geschrieben (0,298 / 0,651 / 0,298), gemessen
                 * (0,584 / 0,827 / 0,580). Das ist Zeichen fuer Zeichen die
                 * sRGB-Umrechnung der geschriebenen Zahlen. Der Shader liest
                 * die Farbe also als linearen Wert und das Bild wandelt sie
                 * beim Anzeigen um.
                 *
                 * Damit `#4ca64c` auch als `#4ca64c` ankommt, muss der
                 * LINEARE Wert hinein. Beim eigenen HDRP-Material war es
                 * umgekehrt - deshalb hat dieselbe Umrechnung dort alles
                 * verdunkelt und hier ist sie richtig.
                 */
                m_FillColor = farbe.linear,
                m_Size = new float2(1f, 1f),
                m_CustomMeshType = (int)Game.Rendering.OverlayRenderSystem
                    .CustomMeshType.Plane,
            };
            if (_cs2Puffer == null)
                _cs2Puffer = new ComputeBuffer(1, System.Runtime.InteropServices
                    .Marshal.SizeOf(typeof(Game.Rendering.OverlayRenderSystem
                        .CustomMeshdData)));
            _cs2Puffer.SetData(new[] { daten });
            _cs2Material.SetBuffer(
                Shader.PropertyToID("colossal_OverlayCustomMeshBuffer"),
                _cs2Puffer);
            _cs2Material.SetFloat("_TransparentSortPriority", 0f);
            if (!_cs2Gemeldet)
            {
                _cs2Gemeldet = true;
                Mod.log.Info("PLT-Flaechennetz: zeichnet mit CS2s eigenem "
                    + "Overlay-Material (" + _cs2Material.shader.name
                    + "), Farbe ueber den Datenpuffer: " + farbe + ".");
            }
            return true;
        }

        /**
         * EINE EINGEFAERBTE TEILFLAECHE.
         *
         * Eigene Gruppe je Flaeche, weil jede ihre eigene Farbe hat und CS2s
         * Overlay-Shader die Farbe aus dem Puffer der GEZEICHNETEN INSTANZ
         * holt. Ein Puffer mit mehreren Eintraegen wuerde nicht helfen - jede
         * Instanz zeichnet das ganze Netz.
         */
        private sealed class Teilnetz
        {
            internal Mesh Netz;
            internal Material Material;
            internal ComputeBuffer Puffer;
            internal ComputeBuffer Argumente;
            internal readonly uint[] Args = new uint[5];
            internal long Signatur;
            internal Color Farbe;
        }

        private readonly List<Teilnetz> _teilnetze = new List<Teilnetz>();
        private int _teilAktiv;

        /** Anfang eines Bildes: ab hier wird neu gezaehlt. */
        internal void BeginneTeilflaechen() => _teilAktiv = 0;

        /**
         * Faerbt eine Teilflaeche in ihrer ECHTEN Form ein.
         *
         * Vorher lag hier ein Rechteck um die Form herum (`teil.Min` bis
         * `teil.Max`). Bei einem schraegen oder L-foermigen Teilstueck hat das
         * die Nachbarflaechen mitgefaerbt - der erste Punkt, den der Nutzer am
         * 2026-09-15 gemeldet hat.
         */
        internal void ZeichneTeilflaeche(float2[] umriss, Color farbe)
        {
            if (_cs2Material == null || umriss == null || umriss.Length < 3)
                return;
            while (_teilnetze.Count <= _teilAktiv)
                _teilnetze.Add(new Teilnetz());
            var gruppe = _teilnetze[_teilAktiv++];

            var signatur = Unterschrift(umriss);
            if (gruppe.Netz == null || gruppe.Signatur != signatur)
            {
                gruppe.Signatur = signatur;
                BaueTeilnetz(gruppe, umriss);
            }
            if (gruppe.Netz == null) return;

            if (gruppe.Material == null)
                gruppe.Material = new Material(_cs2Material)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "PLT Teilflaeche",
                };
            if (!gruppe.Farbe.Equals(farbe) || gruppe.Puffer == null)
            {
                gruppe.Farbe = farbe;
                FuelleFarbpuffer(ref gruppe.Puffer, gruppe.Material, farbe);
            }
        }

        /** Ende eines Bildes: was nicht angefasst wurde, verschwindet. */
        internal void SchliesseTeilflaechen()
        {
            for (var i = _teilAktiv; i < _teilnetze.Count; i++)
                _teilnetze[i].Netz = null;
        }

        /**
         * Erkennungszeichen eines Umrisses - millimetergenau.
         *
         * Nur dafuer da, ein unveraendertes Vieleck wiederzuerkennen. Aendert
         * sich nichts, bleibt das Netz stehen und es wird nur die Farbe neu
         * in den Puffer geschrieben.
         */
        private static long Unterschrift(float2[] umriss)
        {
            long h = umriss.Length;
            foreach (var p in umriss)
            {
                h = h * 31 + (long)math.round(p.x * 1000f);
                h = h * 31 + (long)math.round(p.y * 1000f);
            }
            return h;
        }

        private void BaueTeilnetz(Teilnetz gruppe, float2[] umriss)
        {
            var netz = Cs2Triangulierung.Netz(umriss);
            if (netz == null && umriss.Length == 4)
                netz = new[] { 0, 1, 2, 0, 2, 3 };
            if (netz == null) { gruppe.Netz = null; return; }

            var hoehen = _terrain.GetHeightData(waitForPending: true);
            var punkte = new List<Vector3>();
            var dreiecke = new List<int>();
            for (var i = 0; i + 2 < netz.Length; i += 3)
                Unterteile(umriss[netz[i]], umriss[netz[i + 1]],
                    umriss[netz[i + 2]], ref hoehen, punkte, dreiecke);
            if (punkte.Count == 0) { gruppe.Netz = null; return; }

            gruppe.Netz ??= new Mesh { hideFlags = HideFlags.HideAndDontSave };
            gruppe.Netz.Clear();
            gruppe.Netz.indexFormat = punkte.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            gruppe.Netz.SetVertices(punkte);
            gruppe.Netz.SetTriangles(dreiecke, 0);
            gruppe.Netz.RecalculateBounds();
        }

        /** Ein Eintrag, eine Farbe - dieselbe Struktur wie beim Hauptnetz. */
        private void FuelleFarbpuffer(ref ComputeBuffer puffer,
                                      Material material, Color farbe)
        {
            var daten = new Game.Rendering.OverlayRenderSystem.CustomMeshdData
            {
                m_Matrix = Matrix4x4.identity,
                m_InverseMatrix = Matrix4x4.identity,
                m_FillColor = farbe.linear,
                m_Size = new float2(1f, 1f),
                m_CustomMeshType = (int)Game.Rendering.OverlayRenderSystem
                    .CustomMeshType.Plane,
            };
            puffer ??= new ComputeBuffer(1, System.Runtime.InteropServices
                .Marshal.SizeOf(typeof(Game.Rendering.OverlayRenderSystem
                    .CustomMeshdData)));
            puffer.SetData(new[] { daten });
            material.SetBuffer(
                Shader.PropertyToID("colossal_OverlayCustomMeshBuffer"), puffer);
            material.SetFloat("_TransparentSortPriority", 1f);
        }

        private static void ZeichneIndirekt(Mesh netz, Material material,
                                            ComputeBuffer argumente, uint[] args)
        {
            args[0] = netz.GetIndexCount(0);
            args[1] = 1;
            args[2] = netz.GetIndexStart(0);
            args[3] = netz.GetBaseVertex(0);
            args[4] = 0;
            argumente.SetData(args);
            Graphics.DrawMeshInstancedIndirect(netz, 0, material, netz.bounds,
                argumente, 0, null,
                UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows: false, 0, null);
        }

        /**
         * WAS DAS SPIEL WIRKLICH AN SHADERN GELADEN HAT.
         *
         * `Shader.Find` beantwortet nur geratene Namen mit ja oder nein. Wenn
         * der eingeschlagene Weg nicht traegt, will man keine weitere
         * Vermutung, sondern die Liste. Einmal beim Start, gefiltert auf das,
         * was fuer eine durchscheinende Flaeche in Frage kommt.
         */
        private static void MeldeShaderAuswahl()
        {
            try
            {
                var namen = new List<string>();
                foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
                {
                    if (shader == null) continue;
                    var n = shader.name;
                    if (string.IsNullOrEmpty(n)) continue;
                    var klein = n.ToLowerInvariant();
                    if (klein.Contains("unlit") || klein.Contains("overlay")
                        || klein.Contains("decal") || klein.Contains("transparent"))
                        namen.Add(n);
                }
                namen.Sort();
                Mod.log.Info("PLT-Flaechennetz SHADER im Spiel (" + namen.Count
                    + "): " + string.Join(" | ", namen.ToArray()));
            }
            catch (System.Exception e)
            {
                Mod.log.Warn("PLT-Flaechennetz: Shaderliste nicht lesbar - "
                    + e.GetType().Name);
            }
        }

        /**
         * HDRP NIMMT DIE FARBE NICHT VOM ECKPUNKT.
         *
         * Der erste Versuch hat die Farbe je Eckpunkt ins Netz geschrieben -
         * `HDRP/Unlit` liest die gar nicht, sondern nimmt seine eigene. Die
         * ist weiss und vollkommen deckend, und genau so sah es im Spiel aus.
         *
         * Beim HDRP-Unlit gehoert die Farbe ans MATERIAL, und Durchsicht ist
         * kein Alphawert, sondern ein Satz Schalter: Oberflaechenart,
         * Mischung, Tiefenschreiben, Warteschlange. Welche dieser Regler der
         * Shader wirklich hat, wird GEPRUEFT und gemeldet - `SetFloat` auf
         * einen Regler, den es nicht gibt, verschluckt Unity stumm, und stumm
         * ist hier genau das, was wir nicht gebrauchen koennen.
         *
         * Beidseitig, weil die Umlaufrichtung der Ringe von der Vorschau
         * kommt und nicht garantiert ist. Eine Flaeche, die von oben
         * verschwindet, waere ein Raetsel ohne Wert.
         */
        private void FaerbeMaterial(Color farbe)
        {
            // CS2s Material braucht die Farbe in jedem Fall neu im Puffer -
            // das kostet nichts und faellt nicht unter die Aenderungsprobe.
            var ueberCs2 = HoleCs2Material(farbe);
            if (_material == null || ueberCs2) return;
            if (_farbe.Equals(farbe)) return;
            _farbe = farbe;

            /*
             * HDRP RECHNET LINEAR.
             *
             * `GreenColor` ist ein sRGB-Wert, so wie ihn das Overlay benutzt.
             * Schreibt man ihn unveraendert in ein HDRP-Material, wird er als
             * LINEARER Wert gelesen und kommt deutlich heller und satter
             * heraus. Genau das hat der Nutzer am 2026-09-15 gemeldet: *"Das
             * Gruen ist jetzt ein komplett anderes als bei der ehemaligen
             * Preview."* `.linear` rechnet um; Alpha bleibt unberuehrt.
             */
            var linear = farbe.linear;
            var farbregler = new List<string>();

            /*
             * DIE BELICHTUNG WAR DAS PROBLEM, NICHT DIE FARBE.
             *
             * Gemessener Zeichenzustand am 2026-09-15 nach der HDRP-Pruefung:
             * `SurfaceType=1 Blend=0 Src=1 Dst=10 ZWrite=0 Queue=3000`. Das
             * ist korrektes Alphamischen mit Vormultiplikation im Shader -
             * technisch genau richtig. Mit Alpha 0,20 und der linearen Farbe
             * (0,072 / 0,381 / 0,072) haette ein zarter Hauch herauskommen
             * muessen. Der Nutzer sah das Gegenteil: *"Es ist immer noch
             * extrem gesaettigt und hell."*
             *
             * Das kann dann nur noch eine Ursache haben. HDRP rendert die
             * ganze Szene in echten Leuchtdichten und teilt erst am Ende
             * durch die Belichtung. Bei Tageslicht ist dieser Teiler sehr
             * gross. Eine UNBELEUCHTETE Grundfarbe geht daran vorbei - sie
             * wird nicht mitgeteilt und steht deshalb um Groessenordnungen zu
             * hell im fertigen Bild. Genau so sah es aus.
             *
             * Der von HDRP dafuer vorgesehene Weg ist die EMISSION mit
             * Belichtungsgewicht 1: dann wird der Wert wie beleuchtete Flaeche
             * behandelt und landet in derselben Helligkeit wie alles andere.
             * Die Grundfarbe wird schwarz und traegt nur noch das Alpha.
             */
            // Die Farben stehen weiter unten, NACH der HDRP-Pruefung. Hier
            // wird nur der Zeichenzustand eingestellt.
            _material.SetFloat("_SurfaceType", 1f);   // 1 = durchscheinend
            _material.SetFloat("_BlendMode", 0f);     // 0 = Alpha
            _material.SetFloat("_SrcBlend",
                (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _material.SetFloat("_DstBlend",
                (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _material.SetFloat("_AlphaSrcBlend",
                (float)UnityEngine.Rendering.BlendMode.One);
            _material.SetFloat("_AlphaDstBlend",
                (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _material.SetFloat("_ZWrite", 0f);
            _material.SetFloat("_AlphaCutoffEnable", 0f);
            _material.SetFloat("_DoubleSidedEnable", 1f);
            _material.SetFloat("_CullMode", (float)UnityEngine.Rendering.CullMode.Off);
            _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _material.EnableKeyword("_BLENDMODE_ALPHA");
            _material.EnableKeyword("_DOUBLESIDED_ON");
            _material.DisableKeyword("_ALPHATEST_ON");
            _material.SetOverrideTag("RenderType", "Transparent");
            _material.renderQueue =
                (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _material.doubleSidedGI = true;

            /*
             * DER SCHRITT, OHNE DEN DAS ALLES WIRKUNGSLOS IST.
             *
             * Bei HDRP legen die Regler oben nur Werte ab. Den tatsaechlichen
             * Zeichenzustand - Mischung, Tiefenschreiben, Stencil, Keywords -
             * setzt der Shader erst, wenn das Material neu geprueft wird.
             * Ohne diesen Aufruf bleibt es beim undurchsichtigen Zustand, und
             * genau so sah die Flaeche am 2026-09-15 aus: volles Gruen, vom
             * Boden darunter nichts zu sehen, obwohl Alpha 0,20 im Log stand.
             *
             * Der Aufruf steht in einem try: schlaegt die HDRP-Fassung ihn
             * uns eines Tages weg, soll die Vorschau weiterlaufen - deckend
             * ist haesslich, aber unsichtbar waere schlimmer.
             */
            var geprueft = "nein";
            try
            {
                // HDRP leitet die Warteschlange aus DIESEM Regler ab und
                // ueberschreibt dabei `renderQueue`. Ohne ihn landet das
                // Material nach der Pruefung wieder in der undurchsichtigen
                // Reihe - und dann ist alles andere umsonst.
                // 4 ist `HDRenderQueue.RenderQueueType.Transparent`. Der Typ
                // ist in HDRP `internal`, also steht hier die Zahl - und
                // darunter wird die Warteschlange zur Sicherheit noch einmal
                // von Hand gesetzt, falls sich die Reihenfolge des Enums
                // eines Tages aendert. Was am Ende gilt, steht im Log.
                _material.SetFloat("_RenderQueueType", 4f);
                UnityEngine.Rendering.HighDefinition.HDMaterial
                    .ValidateMaterial(_material);
                _material.renderQueue =
                    (int)UnityEngine.Rendering.RenderQueue.Transparent;
                geprueft = "JA";
            }
            catch (System.Exception e)
            {
                geprueft = "FEHLER " + e.GetType().Name + " " + e.Message;
            }

            /*
             * NACHLESEN STATT ANNEHMEN.
             *
             * Der Aufruf oben kann durchlaufen und trotzdem nichts bewirken.
             * Was zaehlt, sind die Werte danach: Mischfaktoren, Tiefenschreiben
             * und die Warteschlange. Steht dort Quelle=1 und Ziel=0, wird
             * gedeckt gezeichnet, egal welches Alpha in der Farbe steht.
             */
            /*
             * JETZT ERST DIE FARBEN - UND ZWAR AUS DIESEM GRUND.
             *
             * Im Anlauf davor standen sie VOR der Pruefung, und das Gras kam
             * schwarz heraus, obwohl das Log `_EmissiveColor=JA` meldete.
             * `ValidateMaterial` rechnet die Emission naemlich neu aus
             * `_EmissiveColorLDR` mal `_EmissiveIntensity`; beides steht
             * voreingestellt auf Schwarz beziehungsweise Null. Unsere Farbe
             * lag davor und wurde stillschweigend ueberschrieben.
             *
             * Deshalb: Pruefung stellt den Zeichenzustand ein, Farben kommen
             * danach. Und gesetzt wird BEIDES - der LDR-Wert samt Staerke und
             * der fertige HDR-Wert -, damit es gleich bleibt, egal welchen
             * der beiden Wege der Shader nimmt.
             */
            /*
             * VORMULTIPLIZIERT - DAS WAR DIE GANZE SACHE.
             *
             * Gemessener Zeichenzustand: `Src=One, Dst=OneMinusSrcAlpha`.
             * Diese Mischung erwartet eine Farbe, die BEREITS mit ihrem Alpha
             * multipliziert ist. HDRP/Unlit tut das nicht selbst. Die Farbe
             * wurde deshalb in voller Staerke aufaddiert und nur der
             * Hintergrund abgedunkelt - daher "extrem gesaettigt und hell"
             * trotz 20 % im Log, und daher auch das strahlende Weiss im
             * allerersten Versuch.
             *
             * Der Umweg ueber die Emission war der falsche Schluss aus
             * derselben Beobachtung: Emission mit Belichtungsgewicht 1 wird
             * mit dem Belichtungsfaktor multipliziert, und der ist bei
             * Tageslicht winzig. Ergebnis war schwarzes Gras. Die Grundfarbe
             * geht diesen Weg nicht - sie landet unveraendert im fertigen
             * Bild, genau wie das weisse Ergebnis von Anfang an zeigte.
             *
             * Also Grundfarbe, selbst vormultipliziert, Emission aus.
             */
            /*
             * DOCH NICHT LINEAR - GEMESSEN AM BILDSCHIRM.
             *
             * Der Nutzer hat am 2026-09-15 die Flaeche im Spiel ausgemessen:
             * `#005a00` statt der bestellten `#4ca64c`. Geschrieben war der
             * LINEARE Wert (0,072 / 0,381 / 0,072). Auf dem Schirm kam davon
             * Gruen 0,353 an - fast unveraendert - und Rot und Blau gar
             * nicht, weil 0,072 unter die Schwelle faellt, ab der die
             * Bildaufbereitung noch etwas stehen laesst.
             *
             * Der Wert wird also im Wesentlichen so angezeigt, wie er
             * geschrieben wird. Die Umrechnung ins lineare Modell war damit
             * der Fehler, nicht die Loesung: sie hat die Farbe gedrittelt und
             * die schwachen Kanaele weggeschnitten. Geschrieben wird deshalb
             * der Wert, wie er bestellt ist.
             *
             * Der lineare Wert bleibt im Log, damit der naechste Vergleich
             * beide Zahlen nebeneinander hat.
             */
            var gesetzt = farbe;
            foreach (var name in new[] { "_UnlitColor", "_BaseColor", "_Color" })
            {
                if (!_material.HasProperty(name))
                {
                    farbregler.Add(name + "=nein");
                    continue;
                }
                _material.SetColor(name, gesetzt);
                farbregler.Add(name + "=JA");
            }
            /*
             * KLASSISCHES MISCHEN, NACH der HDRP-Pruefung.
             *
             * Die Pruefung hatte `Src = One` eingestellt - das erwartet eine
             * vormultiplizierte Farbe. Vormultipliziert kam schwarz heraus,
             * unvormultipliziert kam es viel zu hell heraus. Beides erklaert
             * sich, wenn der Shader sein Alpha nicht ausgibt, sondern 1
             * schreibt: dann loescht `OneMinusSrcAlpha` den Hintergrund
             * vollstaendig, und stehen bleibt allein unsere Farbe.
             *
             * `Src = SrcAlpha` faengt beide Faelle ab. Gibt der Shader das
             * Alpha doch aus, wird sauber zu 20 % eingefaerbt. Schreibt er 1,
             * steht genau #4ca64c da - deckend, aber die Farbe, die bestellt
             * war. Schwarz kann nicht mehr herauskommen.
             */
            _material.SetFloat("_SrcBlend",
                (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _material.SetFloat("_DstBlend",
                (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            foreach (var name in new[] { "_EmissiveColorLDR", "_EmissiveColor",
                                         "_EmissionColor" })
            {
                if (!_material.HasProperty(name)) continue;
                _material.SetColor(name, new Color(0f, 0f, 0f, 0f));
            }
            if (_material.HasProperty("_EmissiveExposureWeight"))
                _material.SetFloat("_EmissiveExposureWeight", 0f);

            string Regler(string name) => _material.HasProperty(name)
                ? _material.GetFloat(name).ToString("F0") : "-";
            var zustand = "SurfaceType=" + Regler("_SurfaceType")
                + " Blend=" + Regler("_BlendMode")
                + " Src=" + Regler("_SrcBlend")
                + " Dst=" + Regler("_DstBlend")
                + " ZWrite=" + Regler("_ZWrite")
                + " Queue=" + _material.renderQueue
                + " EmissiveColor=" + (_material.HasProperty("_EmissiveColor")
                    ? _material.GetColor("_EmissiveColor").ToString() : "-")
                /*
                 * DIE SCHLUESSELWOERTER SIND DIE LETZTE UNBEKANNTE.
                 *
                 * Sie entscheiden, welche Variante des Shaders ueberhaupt
                 * uebersetzt wird - und damit, ob das Alpha im Bild ankommt.
                 * Alles andere ist inzwischen gemessen.
                 */
                + " Keywords=" + string.Join("+", _material.shaderKeywords);

            Mod.log.Info("PLT-Flaechennetz FARBE: " + farbe
                + " (linear " + linear + ", gesetzt " + gesetzt
                + "); HDRP-Pruefung " + geprueft
                + "; Farbregler: " + string.Join(", ", farbregler.ToArray())
                + "; Zeichenzustand danach: " + zustand + ".");
        }

        /** Nichts mehr zu zeichnen - Vorschau weg, Werkzeug aus. */
        internal void Leere()
        {
            _netz = null;
            _letzteDreiecke = -1;
            _letzteFlaechen = -1;
        }

        /**
         * Baut aus den uebergebenen Vielecken ein Netz.
         *
         * WELCHE VIELECKE - DAS WAR DER FEHLER AM 2026-09-15.
         *
         * Im ersten Anlauf bekam das Netz die ENTWURFSTEILE: Mittelstreifen,
         * Kappen, Restfuellung. Der Gedanke war, die Abdeckung genau so zu
         * lassen wie bei den Streifen davor. Nur sind diese Teile allesamt
         * Vierecke - das Ergebnis war wieder ein Rechteckteppich, nur ohne
         * Naht. Der Nutzer, sofort: *"Nein die gruenen Rechtecke sind kein
         * Asphalt sondern Dekoflaeche."*
         *
         * Gefuettert werden deshalb die VERSCHMOLZENEN Ringe - dieselben, die
         * beim Bauen zu `Grass Surface 01` werden. Die sind unfoermig, und
         * genau darum geht es: die Vorschau zeigt die Form, die entsteht,
         * nicht die Bausteine, aus denen wir sie rechnen.
         */
        internal void SetzeFlaechen(float2[][] flaechen, Color farbe)
        {
            if (_material == null) return;
            if (flaechen == null || flaechen.Length == 0) { Leere(); return; }

            FaerbeMaterial(farbe);

            var uhr = System.Diagnostics.Stopwatch.StartNew();
            var hoehen = _terrain.GetHeightData(waitForPending: true);

            var punkte = new List<Vector3>();
            var dreiecke = new List<int>();
            var verworfen = 0;
            var cs2Verworfen = 0;
            var ersatzVierecke = 0;

            foreach (var ring in flaechen)
            {
                if (ring == null || ring.Length < 3) { verworfen++; continue; }
                /*
                 * ZERLEGT WIRD MIT CS2s EIGENEM VERFAHREN.
                 *
                 * Der Vorversuch hat hier einen Faecher vom ersten Eckpunkt
                 * aus gelegt. Das stimmt nur bei Formen ohne Einbuchtung -
                 * und unsere Flaechen haben ueberall Einbuchtungen. Im Spiel
                 * standen deshalb grosse Keile quer ueber die Luecken;
                 * gemessen deckte der Faecher auf einem einzigen Ring
                 * 50.822 m2 statt 3.340 m2 ab.
                 *
                 * Kommt `null` zurueck, wuerde CS2 den Ring verwerfen - dort
                 * waere nach dem Bauen nackter Boden. Dann faellt die Flaeche
                 * auch in der Vorschau weg, statt eine Fuellung vorzuspielen,
                 * die es nicht geben wird.
                 */
                var netz = Cs2Triangulierung.Netz(ring);
                if (netz == null && ring.Length == 4)
                {
                    /*
                     * VIERECKE GEHEN IMMER.
                     *
                     * CS2s Ear-Clipping versetzt den Ring vorher um 0,1 m
                     * nach innen; ein schmales Teil ueberlebt das nicht und
                     * kommt als "verworfen" zurueck. Fuer die fertigen Ringe
                     * ist das die richtige Auskunft - fuer ein Entwurfsteil
                     * nicht, das geht nie an CS2. Und ein Viereck laesst sich
                     * immer in zwei Dreiecke teilen.
                     */
                    netz = new[] { 0, 1, 2, 0, 2, 3 };
                    ersatzVierecke++;
                }
                if (netz == null) { cs2Verworfen++; continue; }
                for (var i = 0; i + 2 < netz.Length; i += 3)
                {
                    if (dreiecke.Count / 3 >= MaxDreiecke) break;
                    Unterteile(ring[netz[i]], ring[netz[i + 1]],
                        ring[netz[i + 2]], ref hoehen, punkte, dreiecke);
                }
            }

            if (punkte.Count == 0)
            {
                Leere();
                if (cs2Verworfen > 0 || verworfen > 0)
                    Mod.log.Info("PLT-Flaechennetz: " + flaechen.Length
                        + " Ring(e) angeboten, KEINER uebrig - " + verworfen
                        + " zu klein, " + cs2Verworfen + " wuerde CS2 verwerfen.");
                return;
            }

            if (_netz == null)
                _netz = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _netz.Clear();
            _netz.indexFormat = punkte.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _netz.SetVertices(punkte);
            _netz.SetTriangles(dreiecke, 0);
            _netz.RecalculateBounds();

            uhr.Stop();
            /*
             * NUR MELDEN, WENN SICH ETWAS AENDERT.
             *
             * Die Vorschau rechnet beim Ziehen viele Male je Sekunde neu. Eine
             * Zeile je Durchgang macht das Log unlesbar, und unlesbare Logs
             * sind der Grund, aus dem man spaeter Befunde uebersieht.
             */
            if (dreiecke.Count / 3 != _letzteDreiecke
                || flaechen.Length != _letzteFlaechen)
            {
                _letzteDreiecke = dreiecke.Count / 3;
                _letzteFlaechen = flaechen.Length;
                Mod.log.Info("PLT-Flaechennetz: " + flaechen.Length
                    + " Ring(e), " + verworfen + " zu klein, " + cs2Verworfen
                    + " unzerlegbar, " + ersatzVierecke
                    + " als Viereck geteilt, " + punkte.Count + " Punkte, "
                    + _letzteDreiecke + " Dreiecke (Unterteilung ab "
                    + MaxKante + " m), "
                    + uhr.Elapsed.TotalMilliseconds.ToString("F1") + " ms, "
                    + "Material " + _materialname + ".");
            }
        }

        /**
         * Teilt ein Dreieck, bis keine Kante mehr laenger als `MaxKante` ist.
         *
         * Geteilt wird immer die LAENGSTE Kante in ihrer Mitte. Das ist die
         * uebliche Langkanten-Halbierung: sie erzeugt deutlich weniger
         * Dreiecke als ein Vierer-Split und laesst schmale Streifen schmal,
         * statt sie quer zu zerhacken.
         *
         * Die Hoehe wird an JEDEM entstehenden Punkt neu am Gelaende
         * abgetastet - das ist der ganze Zweck der Uebung.
         */
        private void Unterteile(float2 a, float2 b, float2 c,
                                ref TerrainHeightData hoehen,
                                List<Vector3> punkte, List<int> dreiecke)
        {
            // Eine Liste als Stapel. `Stack<T>` gibt es in dieser Umgebung
            // doppelt (System und mscorlib) und der Name ist deshalb
            // mehrdeutig - das kostet nur einen Uebersetzungsfehler, keine
            // Ueberlegung wert.
            _stapel.Clear();
            _stapel.Add(new Dreieck(a, b, c));
            while (_stapel.Count > 0)
            {
                var oben = _stapel[_stapel.Count - 1];
                _stapel.RemoveAt(_stapel.Count - 1);
                float2 p = oben.A, q = oben.B, r = oben.C;
                if (dreiecke.Count / 3 >= MaxDreiecke) return;

                var pq = math.distance(p, q);
                var qr = math.distance(q, r);
                var rp = math.distance(r, p);
                var laengste = math.max(pq, math.max(qr, rp));
                if (laengste > MaxKante)
                {
                    if (pq >= qr && pq >= rp)
                    {
                        var m = (p + q) * 0.5f;
                        _stapel.Add(new Dreieck(p, m, r));
                        _stapel.Add(new Dreieck(m, q, r));
                    }
                    else if (qr >= rp)
                    {
                        var m = (q + r) * 0.5f;
                        _stapel.Add(new Dreieck(p, q, m));
                        _stapel.Add(new Dreieck(p, m, r));
                    }
                    else
                    {
                        var m = (r + p) * 0.5f;
                        _stapel.Add(new Dreieck(p, q, m));
                        _stapel.Add(new Dreieck(m, q, r));
                    }
                    continue;
                }

                var basis = punkte.Count;
                punkte.Add(AufsGelaende(p, ref hoehen));
                punkte.Add(AufsGelaende(q, ref hoehen));
                punkte.Add(AufsGelaende(r, ref hoehen));
                dreiecke.Add(basis);
                dreiecke.Add(basis + 1);
                dreiecke.Add(basis + 2);
            }
        }

        private static Vector3 AufsGelaende(float2 punkt,
                                            ref TerrainHeightData hoehen)
        {
            var welt = new float3(punkt.x, 0f, punkt.y);
            welt.y = TerrainUtils.SampleHeight(ref hoehen, welt) + Schwebe;
            return new Vector3(welt.x, welt.y, welt.z);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            ZeichneTeilnetze();
            if (_netz == null) return;
            /*
             * CS2s OVERLAY-SHADER WILL INDIREKT GEZEICHNET WERDEN.
             *
             * Mit einem gewoehnlichen `DrawMesh` war am 2026-09-15 gar nichts
             * zu sehen: der Shader holt seine Farbe und seine Matrix ueber die
             * Instanznummer aus dem Datenpuffer, und die gibt es nur auf dem
             * instanzierten Weg. `OverlayRenderSystem` zeichnet ihn deshalb
             * mit `DrawMeshInstancedIndirect` (Zeile 923) - genau das hier,
             * nur mit unserem Netz und einer einzigen Instanz.
             */
            if (_cs2Material != null)
            {
                if (_cs2Argumente == null)
                    _cs2Argumente = new ComputeBuffer(1, 5 * sizeof(uint),
                        ComputeBufferType.IndirectArguments);
                _cs2Args[0] = _netz.GetIndexCount(0);
                _cs2Args[1] = 1;
                _cs2Args[2] = _netz.GetIndexStart(0);
                _cs2Args[3] = _netz.GetBaseVertex(0);
                _cs2Args[4] = 0;
                _cs2Argumente.SetData(_cs2Args);
                Graphics.DrawMeshInstancedIndirect(_netz, 0, _cs2Material,
                    _netz.bounds, _cs2Argumente, 0, null,
                    UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows: false, 0, null);
                ZeichneTeilnetze();
                return;
            }
            var material = _material;
            if (material == null) return;
            // Zeichnet in JEDE Kamera. Kein Schattenwurf, kein Empfang -
            // eine Vorschauflaeche ist Information, kein Gegenstand.
            Graphics.DrawMesh(_netz, Matrix4x4.identity, material, 0,
                null, 0, null, false, false, false);
        }

        private void ZeichneTeilnetze()
        {
            for (var i = 0; i < _teilnetze.Count; i++)
            {
                var gruppe = _teilnetze[i];
                if (gruppe.Netz == null || gruppe.Material == null) continue;
                gruppe.Argumente ??= new ComputeBuffer(1, 5 * sizeof(uint),
                    ComputeBufferType.IndirectArguments);
                ZeichneIndirekt(gruppe.Netz, gruppe.Material,
                    gruppe.Argumente, gruppe.Args);
            }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            _cs2Puffer?.Release();
            _cs2Puffer = null;
            _cs2Argumente?.Release();
            _cs2Argumente = null;
            foreach (var gruppe in _teilnetze)
            {
                gruppe.Puffer?.Release();
                gruppe.Argumente?.Release();
            }
            _teilnetze.Clear();
            base.OnDestroy();
        }

        [Preserve]
        public ParkingLotFlaechennetzSystem() { }
    }
}
