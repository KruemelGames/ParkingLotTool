using System;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using Colossal.Serialization.Entities;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Newtonsoft.Json;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public struct ParkingLotVegetationReceipt : IBufferElementData, ISerializable
    {
        public byte Value;
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(Value);
        public void Deserialize<TReader>(TReader reader) where TReader : IReader => reader.Read(out Value);
    }
    internal sealed class VegetationReceipt
    {
        public int Version = 1;
        public string Options, Signature;
    }
    public sealed partial class ParkingLotToolSystem
    {
        internal void RefreshVegetationPreview()
        {
            if (_areaPreviewLayout == null) return;
            SetVegetationPreview(_areaPreviewLayout);
            // UND DAS NETZ MIT. Seit die Pflanzen als Instanzen gezeichnet
            // werden, genuegt die Liste im Overlay nicht mehr - genau das
            // war der Grund, warum nach dem Einschalten kein Kreis kam.
            FuettereePflanzen();
        }
        private void SetVegetationPreview(ParkingLayout layout)
        {
            // NICHT ueber SurfacesForPlacement: die beantwortet, was BELEGT
            // wird, und liefert ohne Dekoration nichts. Gepflanzt wird
            // trotzdem - siehe GrassForVegetation.
            var grass = layout.GrassForVegetation;
            var options=_uiSystem?.Vegetation ?? new VegetationOptions();
            var assets=(_uiSystem?.VegetationAssets ?? Array.Empty<VegetationAsset>()).Where(a=>options.Species.Contains(a.Id)).ToArray();
            var species=assets.Select(a=>new VegetationSpecies {Id=a.Id,Tree=a.Tree,Spacing=a.Spacing}).ToArray();
            ParkingVegetation.Dichtefaktor = Mod.Optionen?.Vegetationsdichte ?? 1f;
            var plan=ParkingVegetation.Plan(grass,options,species);
            _overlay.SetVegetation(plan,species,_terrainSystem);
        }
        // Nur eigene neu erzeugte Temp-Baeume: Alter unmittelbar vor Apply festlegen.
        private readonly System.Collections.Generic.Dictionary<(Entity, float2), Game.Objects.Tree> _vegetationTreeStates = new();
        private readonly System.Collections.Generic.HashSet<(Entity, float2)> _vegetationPlanned = new();
        /*
         * DREI ZAEHLER STATT EINER JA/NEIN-ANTWORT.
         *
         * `ApplyVegetationAge` gibt true zurueck, sobald das Teil im Plan
         * steht - auch dann, wenn danach gar kein Zustand geschrieben wurde,
         * weil die Tree-Komponente fehlte oder kein Ziel hinterlegt war. Die
         * Meldung "native Pflanzen=133/133" sah deshalb bei einem geglueckten
         * und bei einem wirkungslosen Durchgang exakt gleich aus.
         *
         * Genau das war am 2026-09-15 die Sackgasse: der Neubau setzte die
         * Alter richtig, jedes Bearbeiten danach lieferte Zustand 0 - und
         * beide Male stand dieselbe Zeile im Log.
         */
        private int _vegZustandGesetzt, _vegOhneTreeKomponente, _vegOhneZiel;

        /** Nicht jede Baumart hat ein sechstes (Stumpf-)Mesh, wie auch
         *  Tree Controller prueft. Ohne das Mesh bleibt nur Tot. */
        private Game.Objects.Tree StumpfNurMitMesh(Game.Objects.Tree baum,
                                                   Entity prefab)
        {
            if (baum.m_State == Game.Objects.TreeState.Stump
                && (!EntityManager.HasBuffer<SubMesh>(prefab)
                    || EntityManager.GetBuffer<SubMesh>(prefab, true).Length <= 5))
                baum.m_State = Game.Objects.TreeState.Dead;
            return baum;
        }

        private bool ApplyVegetationAge(Entity entity, Entity prefab)
        {
            if (!EntityManager.HasComponent<Game.Objects.Transform>(entity)) return false;
            var position = EntityManager.GetComponentData<Game.Objects.Transform>(entity).m_Position;
            if (!_vegetationPlanned.Contains((prefab, position.xz))) return false;
            if (!EntityManager.HasComponent<Game.Objects.Tree>(entity)) _vegOhneTreeKomponente++;
            else if (!_vegetationTreeStates.ContainsKey((prefab, position.xz))) _vegOhneZiel++;
            if (EntityManager.HasComponent<Game.Objects.Tree>(entity) && _vegetationTreeStates.TryGetValue((prefab, position.xz), out var tree))
            {
                _vegZustandGesetzt++;
                tree = StumpfNurMitMesh(tree, prefab);
                EntityManager.SetComponentData(entity, tree);
                // Der Mesh-Zustand muss nach dem Temp-Alterswechsel neu gebuendelt werden.
                if (!EntityManager.HasComponent<BatchesUpdated>(entity)) EntityManager.AddComponent<BatchesUpdated>(entity);
            }
            return true;
        }
        // OverrideSystem folgt Owner, nicht Attached/PLT-Relation. Der nackte
        // Traeger besitzt weder Object noch Transform/Area: keine Streu-Umverteilung.
        // Baum -> Traeger -> Lot laesst den nativen AreaIterator das eigene Lot ausnehmen.
        private void SetVegetationOwner(Entity plant, Entity carrier, Entity lot)
        {
            var carrierOwner = new Owner { m_Owner=lot };
            if(EntityManager.HasComponent<Owner>(carrier)) EntityManager.SetComponentData(carrier,carrierOwner);
            else EntityManager.AddComponentData(carrier,carrierOwner);
            var owner = new Owner { m_Owner=carrier };
            if(EntityManager.HasComponent<Owner>(plant)) EntityManager.SetComponentData(plant,owner);
            else EntityManager.AddComponentData(plant,owner);
        }
        /**
         * DIE STUFE HEISST IM PANEL ANDERS ALS IN CS2.
         *
         * Unsere Knoepfe sagen Setzling / Jung / Ausgewachsen / Alt / Tot /
         * Baumstumpf; CS2 nennt dieselben Zustaende 0 / Teen / Adult /
         * Elderly / Dead / Stump. Wer die Rohnamen anzeigt, laesst den Nutzer
         * uebersetzen - und die Verschiebung um eine Stufe ("Jung" ist
         * CS2s "Teen") ist genau die Falle, in die man dabei tappt.
         */
        private static string Stufenname(Game.Objects.TreeState zustand)
        {
            if ((zustand & Game.Objects.TreeState.Stump) != 0)
                return ParkingLotTexte.T("Baumstumpf", "Stump");
            if ((zustand & Game.Objects.TreeState.Dead) != 0)
                return ParkingLotTexte.T("Tot", "Dead");
            if ((zustand & Game.Objects.TreeState.Elderly) != 0)
                return ParkingLotTexte.T("Alt", "Elderly");
            if ((zustand & Game.Objects.TreeState.Adult) != 0)
                return ParkingLotTexte.T("Ausgewachsen", "Mature");
            if ((zustand & Game.Objects.TreeState.Teen) != 0)
                return ParkingLotTexte.T("Jung", "Young");
            return ParkingLotTexte.T("Setzling", "Sapling");
        }

        /**
         * SAGT DEM NUTZER, WAS WIRKLICH STEHT.
         *
         * Der Nutzer am 2026-09-15: *"Ich kann selbst nicht wirklich checken
         * welche elderly sind welche nicht weil das an der Groesse liegt und
         * es auch kleinere elderly Baeume gibt."*
         *
         * Er hat recht, und das ist keine Kleinigkeit: Solange die Groesse
         * das einzige Merkmal ist, kann er die Altersauswahl gar nicht
         * pruefen - die Arten sind unterschiedlich gross, ein alter Strauch
         * bleibt klein. Die Zahl stand bisher nur im Log, also nur fuer mich.
         *
         * Gezaehlt werden die ECHTEN Zustaende der gesetzten Baeume, nicht
         * die Absicht aus den Einstellungen. Damit beantwortet die Zeile
         * genau die Frage "ist angekommen, was ich ausgewaehlt habe".
         */
        /*
         * NACHSCHAU - WAS STEHT KURZ NACH DEM BAU WIRKLICH DA?
         *
         * Bisher wurde der echte Baumzustand nur beim BETRETEN des
         * Bearbeitens gemeldet. Zwischen Bau und dieser Meldung liegen
         * Sekunden, in denen CS2 eigene Systeme ueber die frischen Objekte
         * laufen laesst. Ist der Zustand schon direkt nach dem Bau falsch,
         * hat unser Schreiben nicht gegriffen; ist er erst spaeter falsch,
         * setzt ihn jemand zurueck. Das sind zwei voellig verschiedene
         * Ursachen, und ohne diese zweite Messung sind sie nicht zu trennen.
         *
         * 120 Bilder Abstand, damit die Temp-Objekte sicher dauerhaft sind.
         */
        private Entity _vegNachschauLot = Entity.Null;
        private int _vegNachschauFrame;
        private int _vegNachschauStufe;
        private readonly System.Collections.Generic.Dictionary<(Entity, float2),
            Game.Objects.Tree> _vegNachschauZiele = new();

        /**
         * WARUM DAS ALTER AM TEMP-BAUM NICHT ANKOMMT - GEMESSEN UND GELESEN.
         *
         * Messung am 2026-09-15 (Log 19:27-19:29): `Zustand GESETZT` meldete
         * bei JEDEM Bau die volle Zahl - 49 von 49, 55 von 55. Wir schreiben
         * den Zustand also zuverlaessig. Zwei Sekunden spaeter stand trotzdem
         * sechsmal von sieben Baeumen Zustand 0 (Setzling), egal was gewaehlt
         * war. EINMAL kam Stumpf durch (Lot 77308) - und Stumpf laesst sich
         * ueber das Altersfeld gar nicht ausdruecken. Also hat genau dort
         * unsere eigene Schreibung ueberlebt, sonst nie.
         *
         * Das Dekompilat erklaert beide Haelften.
         * `Game.Tools/GenerateObjectsSystem.cs:975`:
         *
         *     flag2 = (definitionData.m_Flags & CreationFlags.Permanent) != 0
         *             || m_PrefabData.IsComponentEnabled(component.m_Prefab);
         *     flag3 = flag2 && m_PrefabTreeData.HasComponent(prefab);
         *     if (flag3 && !m_TreeData.TryGetComponent(original, out tree))
         *         tree = ObjectUtils.InitializeTreeState(definition.m_Age);
         *
         * Unsere `CreationDefinition` traegt keine Flags. Ist `flag3` falsch,
         * wird `m_Age` NIE ausgewertet und der Baum behaelt `default(Tree)` -
         * und das ist Zustand 0, der Setzling. Deshalb nuetzt das Altersfeld
         * hier nichts.
         *
         * Und unsere eigene Schreibung trifft das TEMP-Objekt. Das erzeugt
         * `GenerateObjectsSystem` aus den Definitionen immer wieder neu; jeder
         * Durchgang wirft unseren Wert weg. Ob er ueberlebt, haengt davon ab,
         * ob zwischen unserem Schreiben und dem Uebernehmen noch einmal
         * erzeugt wurde - reine Glueckssache, und genau so sah das Ergebnis
         * aus.
         *
         * Also wird der Zustand jetzt dort gesetzt, wo er nicht mehr
         * weggeworfen werden kann: am DAUERHAFTEN Baum, nach dem Bau. Die
         * Abfrage `_editRelatedParts` schliesst `Temp` und `Deleted`
         * ausdruecklich aus, trifft also nur fertige Teile.
         *
         * Zwei Schritte mit Abstand: erst setzen, dann nachlesen und melden.
         * Die zweite Messung ist kein Zierrat - sie ist der Beleg, dass der
         * Wert diesmal stehenbleibt.
         */
        internal void PflegeVegetationsnachschau()
        {
            if (_vegNachschauLot == Entity.Null) return;
            if (UnityEngine.Time.frameCount < _vegNachschauFrame) return;
            var lot = _vegNachschauLot;
            if (!EntityManager.Exists(lot))
            {
                _vegNachschauLot = Entity.Null;
                return;
            }
            if (_vegNachschauStufe == 1)
            {
                SetzeBaumzustaende(lot);
                _vegNachschauStufe = 2;
                _vegNachschauFrame = UnityEngine.Time.frameCount + 120;
                return;
            }
            _vegNachschauLot = Entity.Null;
            LogVegetationAges(lot, "NACH DEM BAU");
        }

        private void SetzeBaumzustaende(Entity lot)
        {
            if (_vegNachschauZiele.Count == 0) return;
            using var parts = _editRelatedParts.ToEntityArray(Allocator.Temp);
            int getroffen = 0, geaendert = 0, ohneZiel = 0;
            foreach (var part in parts)
            {
                if (EntityManager.GetComponentData<ParkingLotPartRelation>(part).Lot != lot)
                    continue;
                if (!EntityManager.HasComponent<Game.Objects.Tree>(part)) continue;
                if (!EntityManager.HasComponent<PrefabRef>(part)
                    || !EntityManager.HasComponent<Game.Objects.Transform>(part)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(part).m_Prefab;
                var stelle = EntityManager
                    .GetComponentData<Game.Objects.Transform>(part).m_Position.xz;
                if (!_vegNachschauZiele.TryGetValue((prefab, stelle), out var ziel))
                {
                    ohneZiel++;
                    continue;
                }
                getroffen++;
                ziel = StumpfNurMitMesh(ziel, prefab);
                var ist = EntityManager.GetComponentData<Game.Objects.Tree>(part);
                if (ist.m_State == ziel.m_State && ist.m_Growth == ziel.m_Growth)
                    continue;
                EntityManager.SetComponentData(part, ziel);
                if (!EntityManager.HasComponent<BatchesUpdated>(part))
                    EntityManager.AddComponent<BatchesUpdated>(part);
                geaendert++;
            }
            Mod.log.Info("PLT-Vegetation NACHSETZEN Lot " + lot.Index + ": "
                + getroffen + " Baeume im Plan gefunden, " + geaendert
                + " auf den gewaehlten Zustand gesetzt, " + ohneZiel
                + " ohne Planeintrag. Ziele im Speicher: "
                + _vegNachschauZiele.Count + ".");
        }

        private void LogVegetationAges(Entity lot, string anlass = "IST")
        {
            using var parts=_editRelatedParts.ToEntityArray(Allocator.Temp);
            var counts=new System.Collections.Generic.Dictionary<string,int>();
            int hidden=0, baeume=0;
            foreach(var part in parts)
            {
                if(EntityManager.GetComponentData<ParkingLotPartRelation>(part).Lot!=lot || !EntityManager.HasComponent<Game.Objects.Tree>(part)) continue;
                string age=Stufenname(EntityManager.GetComponentData<Game.Objects.Tree>(part).m_State);
                counts[age]=counts.TryGetValue(age,out var n)?n+1:1;
                baeume++;
                if(EntityManager.HasComponent<Overridden>(part)) hidden++;
            }
            Mod.log.Info("PLT-Vegetation " + anlass + " Lot " + lot.Index + ": " + string.Join("; ",counts.Select(p=>p.Key+"="+p.Value)) + "; Baeume="+baeume+"; Overridden="+hidden);
            if (baeume == 0) return;
            _uiSystem?.SetStatus(ParkingLotTexte.T("Bäume: ", "Trees: ")
                + string.Join(", ", counts
                    .OrderByDescending(p=>p.Value)
                    .Select(p=>p.Value+"x "+p.Key)));
        }
        private bool _vegetationPreserve;
        private VegetationReceipt _vegetationReceipt;
        private int _vegetationCount;
        private VegetationReceipt ReadVegetation(Entity lot)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot)) return null;
            if (EntityManager.HasBuffer<ParkingLotBuildText>(lot) && TryReadBuildText(EntityManager.GetBuffer<ParkingLotBuildText>(lot,true),4,out var saved) && !string.IsNullOrEmpty(saved))
            {
                try { return JsonConvert.DeserializeObject<VegetationReceipt>(saved); }
                catch(Exception e) { Mod.log.Warn("Vegetation im Hauptbauzettel unlesbar: " + e.Message); }
            }
            if (!EntityManager.HasBuffer<ParkingLotVegetationReceipt>(lot)) return null;
            var buffer = EntityManager.GetBuffer<ParkingLotVegetationReceipt>(lot,true);
            var bytes = new byte[buffer.Length];
            for (int i=0;i<bytes.Length;i++) bytes[i]=buffer[i].Value;
            try { return JsonConvert.DeserializeObject<VegetationReceipt>(Encoding.UTF8.GetString(bytes)); }
            catch(Exception e) { Mod.log.Warn("Vegetationsbauzettel unlesbar: " + e.Message); return null; }
        }
        private void LoadVegetation(Entity lot)
        {
            var receipt=ReadVegetation(lot);
            _uiSystem?.RestoreVegetation(receipt?.Options);
            LogVegetationAges(lot);
            Mod.log.Info("PLT-Vegetation laden Lot " + lot.Index + ": " + (receipt?.Options ?? "kein Vegetationsbauzettel; Standard aus"));
        }
        private void WriteVegetation(Entity lot)
        {
            if (_vegetationReceipt == null) throw new InvalidOperationException("Vegetationsbauplan fehlt vor dem Speichern.");
            string saved=JsonConvert.SerializeObject(_vegetationReceipt);
            AddBuildText(EntityManager.GetBuffer<ParkingLotBuildText>(lot),4,saved);
            var buffer = EntityManager.HasBuffer<ParkingLotVegetationReceipt>(lot)
                ? EntityManager.GetBuffer<ParkingLotVegetationReceipt>(lot) : EntityManager.AddBuffer<ParkingLotVegetationReceipt>(lot);
            buffer.Clear();
            foreach (byte b in Encoding.UTF8.GetBytes(saved))
                buffer.Add(new ParkingLotVegetationReceipt {Value=b});
            var read=ReadVegetation(lot);
            if(read?.Options!=_vegetationReceipt.Options || read.Signature!=_vegetationReceipt.Signature)
                throw new InvalidOperationException("Vegetationsbauzettel Ruecklesepruefung fehlgeschlagen.");
            Mod.log.Info("PLT-Vegetation gespeichert/geprueft Lot " + lot.Index + ": " + read.Options);
            // Eine KOPIE: `_vegetationTreeStates` raeumt der naechste Bau
            // weg, und zwei schnell aufeinanderfolgende Baeue wuerden sonst
            // die neuen Ziele auf das alte Lot schreiben.
            _vegNachschauZiele.Clear();
            foreach (var paar in _vegetationTreeStates)
                _vegNachschauZiele[paar.Key] = paar.Value;
            _vegNachschauLot = lot;
            _vegNachschauStufe = 1;
            _vegNachschauFrame = UnityEngine.Time.frameCount + 30;
        }
        private string VegetationSignature(float2[][] grass, string options)
        {
            var s = new StringBuilder(options);
            foreach(var ring in grass) {s.Append('|');foreach(var p in ring) s.Append(Math.Round(p.x*1000)).Append(',').Append(Math.Round(p.y*1000)).Append(';');}
            using(var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(s.ToString())));
        }
        private int CreateVegetationDefinitions(float2[][] grass, ref TerrainHeightData heightData)
        {
            _vegetationPreserve=false; _vegetationCount=0; _vegetationTreeStates.Clear(); _vegetationPlanned.Clear();
            _vegZustandGesetzt=0; _vegOhneTreeKomponente=0; _vegOhneZiel=0;
            var options = _uiSystem?.Vegetation ?? new VegetationOptions();
            string json=JsonConvert.SerializeObject(options);
            _vegetationReceipt=new VegetationReceipt {Options=json, Signature=VegetationSignature(grass,json)};
            var old=ReadVegetation(_editLot);
            if (old != null && old.Signature == _vegetationReceipt.Signature)
            {
                _vegetationPreserve=true;
                Mod.log.Info("PLT-Vorbauzettel Vegetation: unveraendert, bestehende Pflanzen werden uebernommen. " + json);
                return 0;
            }
            /*
             * WARUM NEU GEPFLANZT WIRD - DIE ANDERE HAELFTE DER AUSKUNFT.
             *
             * Gemeldet wurde nur der gute Ausgang: "unveraendert, bestehende
             * Pflanzen werden uebernommen". Wurde NICHT uebernommen, schwieg
             * das Log, und genau dann will man es wissen.
             *
             * Der Nutzer am 2026-09-15: *"hatte dann richtig grosse Baeume,
             * bin in edit rein hab 2mal auf elderly geklickt also an und aus.
             * und dann waren die grossen Baeume weg."* Zweimal klicken fuehrt
             * auf dieselbe Maske zurueck - dann MUSS die Unterschrift gleich
             * bleiben und uebernommen werden. Tut sie es nicht, steht hier ab
             * jetzt, welche der beiden sich unterscheidet.
             *
             * Die Unterschrift faellt aus den Einstellungen UND den
             * Gruenflaechen. Aendert sich beim Bearbeiten der Zuschnitt, ist
             * Neupflanzen richtig - auch das sieht man hier.
             */
            Mod.log.Info("PLT-Vorbauzettel Vegetation: wird NEU gepflanzt. "
                + (old == null
                    ? "Es gab noch keinen gespeicherten Vegetationszettel."
                    : "Unterschrift alt " + Kurz(old.Signature)
                      + " gegen neu " + Kurz(_vegetationReceipt.Signature)
                      + "; Einstellungen " + (old.Options == json
                          ? "GLEICH - dann liegt es an den Gruenflaechen"
                          : "verschieden") + ". Alt: " + old.Options));
            var assets=(_uiSystem?.VegetationAssets ?? Array.Empty<VegetationAsset>()).Where(a=>options.Species.Contains(a.Id)).ToArray();
            var species=assets.Select(a=>new VegetationSpecies {Id=a.Id,Tree=a.Tree,Spacing=a.Spacing}).ToArray();
            ParkingVegetation.Dichtefaktor = Mod.Optionen?.Vegetationsdichte ?? 1f;
            var plan=ParkingVegetation.Plan(grass,options,species);
            Mod.log.Info("PLT-Vorbauzettel Vegetation: Dichtefaktor "
                + ParkingVegetation.Dichtefaktor.ToString("F1") + "; " + json + "; Kandidaten="+plan.Candidates+"; Pflanzen="+plan.Plants.Count+"; Grenze="+plan.Limited
                + "; verworfen: Rand="+plan.RandVerworfen+", Wuerfel="+plan.WuerfelVerworfen
                + ", Abstand="+plan.AbstandVerworfen+", ausserhalb="+plan.AussenVerworfen);
            if(options.Enabled && assets.Length==0) _uiSystem?.SetStatus(ParkingLotTexte.T("Vegetation: keine verfügbaren Pflanzen ausgewählt.","Vegetation: no available plants selected."));
            if(plan.Limited) Mod.log.Warn("PLT-Vegetation: Kandidatengrenze erreicht; Teilbepflanzung im Vorbauzettel.");
            var gewaehlt=new int[6];
            var erwartet=new System.Collections.Generic.Dictionary<string,int>();
            foreach(var plant in plan.Plants)
            {
                var asset=assets[plant.Species];
                var position=new float3(plant.Position.x,0,plant.Position.y);
                position.y=TerrainUtils.SampleHeight(ref heightData,position);
                if(!math.all(math.isfinite(position))) continue;
                var random=new Unity.Mathematics.Random(plant.Seed == 0 ? 1u : plant.Seed);
                _vegetationPlanned.Add((asset.Prefab,position.xz));
                int ageIndex=ParkingVegetation.SelectAge(options.Ages, plant.Seed);
                float age=ParkingVegetation.AgeValue(ageIndex);
                gewaehlt[ageIndex]++;
                // Was CS2 aus diesem Altersfeld machen WIRD - nachgebaut aus
                // ObjectUtils.InitializeTreeState. Weicht es von der Wahl ab,
                // liegt es an den Schwellen und nicht an der Zufallszahl.
                var cs2 = age<0.1f?"Jung":age<0.25f?"Teen":age<0.6f?"Erwachsen"
                    :age<0.95f?"Elderly":"Tot";
                erwartet[cs2]=erwartet.TryGetValue(cs2,out var e)?e+1:1;
                if(asset.Tree) _vegetationTreeStates[(asset.Prefab,position.xz)]=new Game.Objects.Tree {
                    m_State=(Game.Objects.TreeState)(ageIndex==0?0:1<<(ageIndex-1)), m_Growth=128 };
                var definition=EntityManager.CreateEntity();
                EntityManager.AddComponentData(definition,new CreationDefinition {m_Prefab=asset.Prefab,m_RandomSeed=random.NextInt()});
                EntityManager.AddComponent<Updated>(definition);
                EntityManager.AddComponentData(definition,new ObjectDefinition {
                    m_Position=position,m_Rotation=quaternion.RotateY(random.NextFloat(0,math.PI*2)),
                    m_Probability=100,m_PrefabSubIndex=-1,m_Scale=new float3(1),m_Intensity=1,m_ParentMesh=-1,
                    m_Age=age,m_IsDecoration=false });
                RecordObjectDefinition("Vegetation",_vegetationCount++,asset.Prefab,definition,position);
            }
            var stufen=new[]{"Jung","Teen","Erwachsen","Elderly","Tot","Stumpf"};
            Mod.log.Info("PLT-Vegetation WAHL: Maske="+(options.Ages&63)
                + "; gewaehlt "
                + string.Join(", ", Enumerable.Range(0,6)
                    .Where(i=>gewaehlt[i]>0)
                    .Select(i=>stufen[i]+"="+gewaehlt[i]))
                + "; daraus macht CS2 laut Altersfeld "
                + string.Join(", ", erwartet.Select(p=>p.Key+"="+p.Value))
                + ". Steht darunter 'PLT-Vegetation IST' etwas anderes, "
                + "ueberlebt der Zustand den Bau nicht.");
            return _vegetationCount;
        }
        /** Kurzform einer Unterschrift - acht Zeichen genuegen zum Vergleichen. */
        private static string Kurz(string s)
            => string.IsNullOrEmpty(s) ? "(keine)"
               : s.Substring(0, Math.Min(8, s.Length));

        private void TransferVegetation(Entity old, Entity next, Entity carrier)
        {
            if(!_vegetationPreserve)
            {
                /*
                 * WIEVIELE ALTE PFLANZEN GEHEN HIER EIGENTLICH WEG?
                 *
                 * Der Satz "die alten Pflanzen gehen mit dem alten Parkplatz
                 * weg" war eine ANNAHME, keine Messung. Der Nutzer am
                 * 2026-09-15: *"ich habe Baeume auf Sapling gesetzt und es
                 * blieben noch die alten stehen."* Also wird ab jetzt
                 * gezaehlt, was hier zurueckbleibt - und der Aufraeumer sagt
                 * in seiner eigenen Zeile, wieviel davon er wirklich
                 * einsammelt. Stimmen die beiden Zahlen nicht ueberein, steht
                 * die Ursache damit fest, ohne dass man raten muss.
                 */
                var alteBaeume = 0;
                using (var vorher = _editRelatedParts.ToEntityArray(Allocator.Temp))
                    foreach (var part in vorher)
                    {
                        if (EntityManager.GetComponentData<ParkingLotPartRelation>(part).Lot != old)
                            continue;
                        if (!EntityManager.HasComponent<PrefabRef>(part)) continue;
                        if (EntityManager.HasComponent<PlantData>(
                                EntityManager.GetComponentData<PrefabRef>(part).m_Prefab))
                            alteBaeume++;
                    }
                Mod.log.Info("PLT-Bauzettel Vegetation: KEINE Uebernahme - "
                    + alteBaeume + " alte Pflanzen bleiben am alten Lot "
                    + old.Index + " und muessen vom Aufraeumer kommen; neue "
                    + "werden gesetzt. Grund siehe 'wird NEU gepflanzt' "
                    + "weiter oben.");
                return;
            }
            using var parts=_editRelatedParts.ToEntityArray(Allocator.Temp);
            int count=0;
            foreach(var part in parts)
            {
                var relation=EntityManager.GetComponentData<ParkingLotPartRelation>(part);
                if(relation.Lot!=old || !EntityManager.HasComponent<PrefabRef>(part)) continue;
                var prefab=EntityManager.GetComponentData<PrefabRef>(part).m_Prefab;
                if(!EntityManager.HasComponent<PlantData>(prefab)) continue;
                if(EntityManager.Exists(relation.Carrier) && EntityManager.HasBuffer<Game.Objects.SubObject>(relation.Carrier))
                {
                    var previous=EntityManager.GetBuffer<Game.Objects.SubObject>(relation.Carrier);
                    for(int i=previous.Length-1;i>=0;i--) if(previous[i].m_SubObject==part) previous.RemoveAt(i);
                }
                SetVegetationOwner(part,carrier,next);
                EntityManager.SetComponentData(part,new ParkingLotPartRelation {Lot=next,Carrier=carrier});
                EntityManager.SetComponentData(part,new Game.Objects.Attached(carrier,Entity.Null,0));
                if(EntityManager.HasComponent<Hidden>(part)) EntityManager.RemoveComponent<Hidden>(part);
                if(!EntityManager.HasComponent<BatchesUpdated>(part)) EntityManager.AddComponent<BatchesUpdated>(part);
                _hiddenByEdit.Remove(part);
                // Der neue Traeger ist nackt: keine Vanilla-Umverteilung von SubObjects.
                var children=EntityManager.GetBuffer<Game.Objects.SubObject>(carrier);
                bool found=false;foreach(var child in children) if(child.m_SubObject==part) found=true;
                if(!found) children.Add(new Game.Objects.SubObject {m_SubObject=part});
                count++;
            }
            Mod.log.Info("PLT-Bauzettel Vegetation: "+count+" bestehende Entities samt Alter uebernommen.");
        }
    }
}
