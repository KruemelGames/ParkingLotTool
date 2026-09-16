using System.Collections.Generic;
using Game;
using Game.Simulation;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * DIE GEFUELLTEN FLAECHEN DER VORSCHAU.
     *
     * WARUM ES SEIN MUSS. CS2s Overlay kann kein Vieleck fuellen - es kennt
     * Kreis, Strich, Kurve, Text und die drei Koerper Cylinder/Arrow/Plane.
     * Und `AreaBatchSystem`, das die gebauten Flaechen zeichnet, schliesst
     * `Temp` in beiden Abfragen aus. Eine Vorschauflaeche ist im Spiel
     * deshalb ein Rand und sonst nichts. Wer sie gefuellt sehen will, muss
     * selbst zeichnen.
     *
     * WAS GEZEICHNET WIRD - und das ist der eigentliche Punkt. Nicht die
     * ENTWURFSTEILE (Randstrassenband, Fahrgassenbaender, Buchtbloecke,
     * Mittelstreifen, Kappen, Restfuellung), sondern die VERSCHMOLZENEN
     * RINGE, die beim Bauen an CS2 gehen. Die Entwurfsteile ueberlappen sich
     * im Plan - das ist keine Panne, so arbeitet der Kern; aufgeloest wird es
     * erst beim Verschmelzen. Zeichnet man sie trotzdem, sieht man
     * Ueberlappungen und Luecken, die es im Ergebnis nie gibt. Der Nutzer am
     * 2026-09-15: *"Strassenrechtecke ueberlappen sich einfach gegenseitig,
     * gruen und Buchten ueberlappen."*
     *
     * Daraus folgt die Arbeitsteilung der ganzen Vorschau:
     *
     *   - GEFUELLT wird hier, aus den verschmolzenen Ringen: Belag, Gras,
     *     Zoningboden, Zoningstrasse, Vorflaechen. Jede Sorte eine Gruppe,
     *     jede Gruppe eine Farbe. Das ist das Bild vom ERGEBNIS.
     *   - UMRISSEN wird im Overlay: Buchten nach Rolle, Zufahrten,
     *     Querstrassen. Das ist die Auskunft darueber, WORAUS es besteht.
     *
     * Weil nur eine Ebene fuellt, kann sich nichts mehr gegenseitig zudecken.
     *
     * WOMIT GEZEICHNET WIRD. Mit CS2s eigenem Overlay-Material. Ein eigenes
     * HDRP-Material hat jede gesaettigte Farbe entstellt - im Spiel gemessen
     * kam aus `#4ca64c` ein `#00c400`, Rot und Blau exakt null, waehrend
     * neutrales Grau sauber durchlief. Das Spiel hat dieses Problem nicht,
     * also nehmen wir sein Material: `OverlayConfigurationPrefab
     * .m_SolidObjectMaterial`, Farbe ueber den Datenpuffer
     * `colossal_OverlayCustomMeshBuffer`, gezeichnet mit
     * `DrawMeshInstancedIndirect`. Mit einem gewoehnlichen `DrawMesh` ist
     * nichts zu sehen: der Shader holt Farbe und Matrix ueber die
     * Instanznummer, und die gibt es nur auf dem instanzierten Weg. Die Farbe
     * muss LINEAR hinein.
     *
     * Belege: `Game.Rendering/OverlayRenderSystem.cs` Zeilen 71-82, 497,
     * 807-814, 923 und 1136-1142.
     */
    public sealed partial class ParkingLotFlaechennetzSystem : GameSystemBase
    {
        /** Knapp ueber dem Boden - zu wenig flimmert, zu viel schwebt. */
        private const float Schwebe = 0.15f;

        /**
         * LAENGSTE KANTE, BEVOR EIN DREIECK GETEILT WIRD.
         *
         * Ein Dreieck ist flach, das Gelaende ist es nicht. Ohne Unterteilung
         * schneidet eine grosse Flaeche am Hang in den Boden. 8 m ist der
         * Wert, bei dem eine Boeschung noch sauber aussieht und die
         * Dreieckszahl im niedrigen Tausenderbereich bleibt.
         */
        private const float MaxKante = 8f;

        /** Notbremse, damit eine entartete Flaeche nicht den Bildaufbau frisst. */
        private const int MaxDreiecke = 60000;

        private TerrainSystem _terrain;
        private Material _vorlage;
        private int _versuche;
        private bool _gemeldet;

        /**
         * Kann das Netz zeichnen?
         *
         * Die Vorschau fragt das, BEVOR sie ihre eigene Streifenfuellung
         * weglaesst. Kommt CS2s Material nicht, bleiben die Streifen - ein
         * Werkzeug, das nichts anzeigt, waere schlimmer als eines mit
         * Naehten.
         */
        internal bool Einsatzbereit => HoleVorlage();

        private readonly struct Dreieck
        {
            internal Dreieck(float2 a, float2 b, float2 c) { A = a; B = b; C = c; }
            internal float2 A { get; }
            internal float2 B { get; }
            internal float2 C { get; }
        }

        private readonly List<Dreieck> _stapel = new List<Dreieck>();

        /**
         * EINE GEFUELLTE GRUPPE.
         *
         * Eigene Gruppe je Farbe, weil CS2s Overlay-Shader seine Farbe aus
         * dem Puffer der gezeichneten INSTANZ holt. Ein Puffer mit mehreren
         * Eintraegen wuerde nicht helfen - jede Instanz zeichnet das ganze
         * Netz.
         */
        private sealed class Netzgruppe
        {
            internal Mesh Netz;
            internal Material Material;
            internal ComputeBuffer Puffer;
            internal ComputeBuffer Argumente;
            internal readonly uint[] Args = new uint[5];
            internal long Signatur;
            internal Color Farbe;
            internal float Sortierung = -1f;
        }

        /** Die Belagsflaechen. Wechseln je Vorschaulauf. */
        private readonly List<Netzgruppe> _flaechen = new List<Netzgruppe>();
        private int _flaechenAktiv;

        /** Die Teilflaechen-Einfaerbung. Wechselt je Bild - aber nur in der Farbe. */
        private readonly List<Netzgruppe> _teilnetze = new List<Netzgruppe>();
        private int _teilAktiv;

        private int _letzteDreiecke = -1;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        /**
         * Holt CS2s Overlay-Material als Vorlage fuer alle Gruppen.
         *
         * Lazy, nicht in `OnCreate`: die Prefabs sind beim Anlegen des
         * Systems noch nicht geladen. Nach ein paar erfolglosen Versuchen
         * wird aufgegeben - sonst fragt man in jedem Bild eine Abfrage an,
         * die nie etwas liefert.
         */
        private bool HoleVorlage()
        {
            if (_vorlage != null) return true;
            if (_versuche > 20) return false;
            _versuche++;
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
                _vorlage = konfig.m_SolidObjectMaterial;
                if (!_gemeldet)
                {
                    _gemeldet = true;
                    Mod.log.Info("PLT-Flaechennetz: zeichnet mit CS2s eigenem "
                        + "Overlay-Material (" + _vorlage.shader.name + ").");
                }
                return true;
            }
            catch (System.Exception e)
            {
                if (!_gemeldet)
                {
                    _gemeldet = true;
                    Mod.log.Warn("PLT-Flaechennetz: CS2s Overlay-Material "
                        + "nicht erreichbar (" + e.GetType().Name + ": "
                        + e.Message + "). Die Vorschau bleibt bei ihrer "
                        + "Streifenfuellung.");
                }
                _versuche = 999;
                return false;
            }
        }

        // ------------------------------------------------------------------
        // BELAGSFLAECHEN - eine Gruppe je Materialsorte, je Vorschaulauf neu.
        // ------------------------------------------------------------------

        /** Nichts gefuellt? Dann muss die Vorschau neu fuettern. */
        internal bool Leer => _flaechenAktiv == 0;

        internal void BeginneFlaechen() => _flaechenAktiv = 0;

        /**
         * Eine Materialsorte als gefuellte Flaeche.
         *
         * `sortierung` entscheidet, was ueber was liegt - CS2s Shader nimmt
         * den Wert als `_TransparentSortPriority`. Belag unten, Gras
         * darueber, Zoning zuoberst: so bleibt sichtbar, was oben liegt, auch
         * wenn zwei Flaechen sich an einer Kante beruehren.
         */
        internal void FuegeFlaechen(float2[][] ringe, Color farbe,
                                    float sortierung)
        {
            if (!HoleVorlage()) return;
            if (ringe == null || ringe.Length == 0) return;
            while (_flaechen.Count <= _flaechenAktiv)
                _flaechen.Add(new Netzgruppe());
            var gruppe = _flaechen[_flaechenAktiv];

            var signatur = Unterschrift(ringe);
            if (gruppe.Netz == null || gruppe.Signatur != signatur)
            {
                gruppe.Signatur = signatur;
                BaueNetz(gruppe, ringe);
            }
            if (gruppe.Netz == null) return;
            _flaechenAktiv++;
            RichteGruppe(gruppe, farbe, sortierung);
        }

        internal void SchliesseFlaechen()
        {
            for (var i = _flaechenAktiv; i < _flaechen.Count; i++)
                _flaechen[i].Netz = null;
            MeldeStand();
        }

        /** Nichts mehr zu zeichnen - Vorschau weg, Werkzeug aus, Reiter weg. */
        internal void Leere()
        {
            foreach (var gruppe in _flaechen) gruppe.Netz = null;
            foreach (var gruppe in _teilnetze) gruppe.Netz = null;
            _flaechenAktiv = 0;
            _teilAktiv = 0;
            _letzteDreiecke = -1;
        }

        // ------------------------------------------------------------------
        // TEILFLAECHEN - eine Gruppe je Teilstueck, je Bild eingefaerbt.
        // ------------------------------------------------------------------

        internal void BeginneTeilflaechen() => _teilAktiv = 0;

        /**
         * Faerbt eine Teilflaeche in ihrer ECHTEN Form ein.
         *
         * Vorher lag hier ein Rechteck um die Form herum. Bei einem schraegen
         * oder L-foermigen Teilstueck hat das die Nachbarflaechen mitgefaerbt.
         */
        internal void ZeichneTeilflaeche(float2[] umriss, Color farbe)
        {
            if (!HoleVorlage()) return;
            if (umriss == null || umriss.Length < 3) return;
            while (_teilnetze.Count <= _teilAktiv)
                _teilnetze.Add(new Netzgruppe());
            var gruppe = _teilnetze[_teilAktiv];

            var einer = new[] { umriss };
            var signatur = Unterschrift(einer);
            if (gruppe.Netz == null || gruppe.Signatur != signatur)
            {
                gruppe.Signatur = signatur;
                BaueNetz(gruppe, einer);
            }
            if (gruppe.Netz == null) return;
            _teilAktiv++;
            RichteGruppe(gruppe, farbe, 10f);
        }

        internal void SchliesseTeilflaechen()
        {
            for (var i = _teilAktiv; i < _teilnetze.Count; i++)
                _teilnetze[i].Netz = null;
        }

        // ------------------------------------------------------------------

        /** Material und Farbpuffer einer Gruppe auf den Stand bringen. */
        private void RichteGruppe(Netzgruppe gruppe, Color farbe,
                                  float sortierung)
        {
            gruppe.Material ??= new Material(_vorlage)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "PLT Flaechennetz",
            };
            if (gruppe.Puffer != null && gruppe.Farbe.Equals(farbe)
                && math.abs(gruppe.Sortierung - sortierung) < 0.001f) return;
            gruppe.Farbe = farbe;
            gruppe.Sortierung = sortierung;

            var daten = new Game.Rendering.OverlayRenderSystem.CustomMeshdData
            {
                m_Matrix = Matrix4x4.identity,
                m_InverseMatrix = Matrix4x4.identity,
                /*
                 * LINEAR - am Bildschirm ausgerechnet, nicht geraten.
                 * Geschrieben (0,298 / 0,651 / 0,298) kam (0,584 / 0,827 /
                 * 0,580) heraus - Zeichen fuer Zeichen die sRGB-Umrechnung.
                 * Der Shader liest die Farbe also als linearen Wert.
                 */
                m_FillColor = farbe.linear,
                m_Size = new float2(1f, 1f),
                m_CustomMeshType = (int)Game.Rendering.OverlayRenderSystem
                    .CustomMeshType.Plane,
            };
            gruppe.Puffer ??= new ComputeBuffer(1, System.Runtime
                .InteropServices.Marshal.SizeOf(typeof(Game.Rendering
                    .OverlayRenderSystem.CustomMeshdData)));
            gruppe.Puffer.SetData(new[] { daten });
            gruppe.Material.SetBuffer(
                Shader.PropertyToID("colossal_OverlayCustomMeshBuffer"),
                gruppe.Puffer);
            gruppe.Material.SetFloat("_TransparentSortPriority", sortierung);
        }

        /**
         * Erkennungszeichen eines Ringsatzes - millimetergenau.
         *
         * Nur dafuer da, unveraenderte Geometrie wiederzuerkennen. Aendert
         * sich nichts, bleibt das Netz stehen und es wird hoechstens die
         * Farbe neu in den Puffer geschrieben.
         */
        private static long Unterschrift(float2[][] ringe)
        {
            long h = ringe.Length;
            foreach (var ring in ringe)
            {
                if (ring == null) { h = h * 31 + 7; continue; }
                h = h * 31 + ring.Length;
                foreach (var p in ring)
                {
                    h = h * 31 + (long)math.round(p.x * 1000f);
                    h = h * 31 + (long)math.round(p.y * 1000f);
                }
            }
            return h;
        }

        /**
         * Baut ein Netz aus Ringen.
         *
         * Zerlegt wird mit `Cs2Triangulierung.Netz` - demselben Nachbau von
         * CS2s Ear-Clipping, mit dem wir schon vorhersagen, ob das Spiel eine
         * Flaeche annimmt. Kommt `null` zurueck, wuerde CS2 den Ring
         * verwerfen; dort waere nach dem Bauen nackter Boden, und genau so
         * bleibt die Stelle auch in der Vorschau leer.
         *
         * Ein Viereck wird trotzdem immer geteilt: der 0,1-m-Innenversatz von
         * CS2 laesst schmale Teile scheitern, die als Entwurfsteil nie an das
         * Spiel gehen.
         */
        private void BaueNetz(Netzgruppe gruppe, float2[][] ringe)
        {
            var hoehen = _terrain.GetHeightData(waitForPending: true);
            var punkte = new List<Vector3>();
            var dreiecke = new List<int>();

            foreach (var ring in ringe)
            {
                if (ring == null || ring.Length < 3) continue;
                var netz = Cs2Triangulierung.Netz(ring);
                if (netz == null && ring.Length == 4)
                    netz = new[] { 0, 1, 2, 0, 2, 3 };
                if (netz == null) continue;
                for (var i = 0; i + 2 < netz.Length; i += 3)
                {
                    if (dreiecke.Count / 3 >= MaxDreiecke) break;
                    Unterteile(ring[netz[i]], ring[netz[i + 1]],
                        ring[netz[i + 2]], ref hoehen, punkte, dreiecke);
                }
            }

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

        /**
         * Teilt ein Dreieck, bis keine Kante mehr laenger als `MaxKante` ist.
         *
         * Geteilt wird immer die LAENGSTE Kante in ihrer Mitte - die uebliche
         * Langkanten-Halbierung. Sie erzeugt deutlich weniger Dreiecke als
         * ein Vierer-Split und laesst schmale Streifen schmal, statt sie quer
         * zu zerhacken. Die Hoehe wird an jedem entstehenden Punkt neu am
         * Gelaende abgetastet; das ist der ganze Zweck.
         */
        private void Unterteile(float2 a, float2 b, float2 c,
                                ref TerrainHeightData hoehen,
                                List<Vector3> punkte, List<int> dreiecke)
        {
            // Eine Liste als Stapel: `Stack<T>` gibt es in dieser Umgebung
            // doppelt (System und mscorlib) und der Name ist mehrdeutig.
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
                if (math.max(pq, math.max(qr, rp)) > MaxKante)
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

        /**
         * CS2s Overlay-Shader will INDIREKT gezeichnet werden.
         *
         * Mit einem gewoehnlichen `DrawMesh` ist nichts zu sehen: er holt
         * Farbe und Matrix ueber die Instanznummer aus dem Datenpuffer, und
         * die gibt es nur auf dem instanzierten Weg. `OverlayRenderSystem`
         * zeichnet ihn deshalb mit `DrawMeshInstancedIndirect` (Zeile 923) -
         * genau das hier, nur mit unserem Netz und einer einzigen Instanz.
         */
        private static void ZeichneIndirekt(Netzgruppe gruppe)
        {
            if (gruppe.Netz == null || gruppe.Material == null) return;
            gruppe.Argumente ??= new ComputeBuffer(1, 5 * sizeof(uint),
                ComputeBufferType.IndirectArguments);
            gruppe.Args[0] = gruppe.Netz.GetIndexCount(0);
            gruppe.Args[1] = 1;
            gruppe.Args[2] = gruppe.Netz.GetIndexStart(0);
            gruppe.Args[3] = gruppe.Netz.GetBaseVertex(0);
            gruppe.Args[4] = 0;
            gruppe.Argumente.SetData(gruppe.Args);
            Graphics.DrawMeshInstancedIndirect(gruppe.Netz, 0, gruppe.Material,
                gruppe.Netz.bounds, gruppe.Argumente, 0, null,
                UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows: false, 0, null);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            for (var i = 0; i < _flaechen.Count; i++)
                ZeichneIndirekt(_flaechen[i]);
            for (var i = 0; i < _teilnetze.Count; i++)
                ZeichneIndirekt(_teilnetze[i]);
        }

        /**
         * Eine Logzeile je Aenderung, nicht je Bild.
         *
         * Die Vorschau rechnet beim Ziehen viele Male je Sekunde neu. Eine
         * Zeile je Durchgang macht das Log unlesbar, und unlesbare Logs sind
         * der Grund, aus dem man spaeter Befunde uebersieht.
         */
        private void MeldeStand()
        {
            var dreiecke = 0;
            for (var i = 0; i < _flaechenAktiv; i++)
                if (_flaechen[i].Netz != null)
                    dreiecke += (int)(_flaechen[i].Netz.GetIndexCount(0) / 3);
            if (dreiecke == _letzteDreiecke) return;
            _letzteDreiecke = dreiecke;
            Mod.log.Info("PLT-Flaechennetz: " + _flaechenAktiv
                + " Fuellebene(n), " + dreiecke + " Dreiecke "
                + "(Unterteilung ab " + MaxKante + " m).");
        }

        [Preserve]
        protected override void OnDestroy()
        {
            foreach (var gruppe in _flaechen)
            {
                gruppe.Puffer?.Release();
                gruppe.Argumente?.Release();
            }
            foreach (var gruppe in _teilnetze)
            {
                gruppe.Puffer?.Release();
                gruppe.Argumente?.Release();
            }
            _flaechen.Clear();
            _teilnetze.Clear();
            base.OnDestroy();
        }

        [Preserve]
        public ParkingLotFlaechennetzSystem() { }
    }
}
