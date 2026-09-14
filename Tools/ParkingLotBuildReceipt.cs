using System;
using Colossal.Serialization.Entities;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Der dauerhafte Bauzettel eines Lots.
     *
     * EIGENE KOMPONENTE STATT ERWEITERUNG EINES BESTEHENDEN DATENSATZES.
     * Der ECS-Serializer legt Instanzen einer Komponente ohne Einzelblock
     * hintereinander. Ein neues Feld in ParkingLotEconomyData oder einer
     * Relation wuerde deshalb in alten Spielstaenden den jeweils folgenden
     * Datensatz verschieben. Dieser Typ beginnt mit seiner eigenen Version;
     * spaetere Fassungen koennen beim Lesen von Version 1 exakt bei deren
     * letztem Feld aufhoeren.
     *
     * Die UI-Werte stehen zusaetzlich zu LayoutSettings hier. Das ist noetig,
     * weil ausgeschaltetes Mittelgruen in LayoutSettings nur als Md=0 ankommt
     * und damit die gemerkte Reglerbreite verloren waere.
     */
    public struct ParkingLotBuildReceipt : IComponentData,
                                           IQueryTypeParameter,
                                           ISerializable
    {
        /*
         * VERSION 2 seit dem 2026-08-31: die gewaehlte Bezugslinie kam dazu.
         *
         * Genau dafuer steht die Versionszahl an erster Stelle. Der
         * Serializer legt Instanzen ohne Laengenangabe hintereinander; wer
         * einfach ein Feld anhaengt, verschiebt in alten Spielstaenden alle
         * folgenden Datensaetze. Mit der Version davor weiss der Leser, wie
         * weit er lesen darf - Fassung 1 endet nach `BayIcons`.
         */
        public const int CurrentVersion = 3;

        public int Version;
        /** Bezugsrichtung in Grad; `NaN` heisst "keine Linie gewaehlt". */
        public double Ausrichtwinkel;
        /*
         * Die Enden der Bezugslinie. Ohne sie waere der Winkel nach dem
         * Wiederoeffnen eingefroren - der Nutzer erwartet aber, dass er dem
         * Polygon folgt, solange es die Kante gibt.
         */
        public float AusrichtAx;
        public float AusrichtAz;
        public float AusrichtBx;
        public float AusrichtBz;
        public double Es;
        public double Ai;
        public double Cw;
        public double Sl;
        public double Sw;
        public double Md;
        public double Cr;
        public double Angle;
        public double KantenVersatz;
        public bool Qk;
        public bool Randstrassen;
        public bool Auto;
        public bool AutomaticEntrances;
        public bool Zellen;
        public bool EineFlaeche;
        public bool NoNotch;
        public bool Single;
        public bool NoHalf;
        /** 0=edge, 1=fixed, 2=auto. */
        public int AngleMode;

        public double MedianWidth;
        public bool GreenMedian;
        public double CrossBays;
        public bool SurfaceRoadOn;
        public bool SurfaceDecorationOn;
        public bool SurfaceApronOn;
        public bool BayIcons;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(Es);
            writer.Write(Ai);
            writer.Write(Cw);
            writer.Write(Sl);
            writer.Write(Sw);
            writer.Write(Md);
            writer.Write(Cr);
            writer.Write(Angle);
            writer.Write(KantenVersatz);
            writer.Write(Qk);
            writer.Write(Auto);
            writer.Write(AutomaticEntrances);
            writer.Write(Zellen);
            writer.Write(EineFlaeche);
            writer.Write(NoNotch);
            writer.Write(Single);
            writer.Write(NoHalf);
            writer.Write(AngleMode);
            writer.Write(MedianWidth);
            writer.Write(GreenMedian);
            writer.Write(CrossBays);
            writer.Write(SurfaceRoadOn);
            writer.Write(SurfaceDecorationOn);
            writer.Write(SurfaceApronOn);
            writer.Write(BayIcons);
            // Ans ENDE, damit Fassung 1 unveraendert davor liegt.
            writer.Write(Ausrichtwinkel);
            writer.Write(AusrichtAx);
            writer.Write(AusrichtAz);
            writer.Write(AusrichtBx);
            writer.Write(AusrichtBz);
            writer.Write(Randstrassen);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            reader.Read(out Es);
            reader.Read(out Ai);
            reader.Read(out Cw);
            reader.Read(out Sl);
            reader.Read(out Sw);
            reader.Read(out Md);
            reader.Read(out Cr);
            reader.Read(out Angle);
            reader.Read(out KantenVersatz);
            reader.Read(out Qk);
            reader.Read(out Auto);
            reader.Read(out AutomaticEntrances);
            reader.Read(out Zellen);
            reader.Read(out EineFlaeche);
            reader.Read(out NoNotch);
            reader.Read(out Single);
            reader.Read(out NoHalf);
            reader.Read(out AngleMode);
            reader.Read(out MedianWidth);
            reader.Read(out GreenMedian);
            reader.Read(out CrossBays);
            reader.Read(out SurfaceRoadOn);
            reader.Read(out SurfaceDecorationOn);
            reader.Read(out SurfaceApronOn);
            reader.Read(out BayIcons);
            // Fassung 1 kennt das Feld nicht - dort endet der Datensatz
            // vorher, es darf also NICHT gelesen werden.
            if (Version >= 2)
            {
                reader.Read(out Ausrichtwinkel);
                reader.Read(out AusrichtAx);
                reader.Read(out AusrichtAz);
                reader.Read(out AusrichtBx);
                reader.Read(out AusrichtBz);
            }
            else Ausrichtwinkel = double.NaN;
            Randstrassen = true;
            if (Version >= 3) reader.Read(out Randstrassen);
        }

        internal LayoutSettings ToLayoutSettings(Entrance[] entrances)
        {
            return new LayoutSettings
            {
                Es = Es,
                Ai = Ai,
                Cw = Cw,
                Sl = Sl,
                Sw = Sw,
                Md = Md,
                Cr = Cr,
                Qk = Qk,
                Randstrassen = Randstrassen,
                Auto = Auto,
                Angle = Angle,
                AngleMode = DecodeAngleMode(AngleMode),
                Entrances = entrances ?? Array.Empty<Entrance>(),
                AutomaticEntrances = AutomaticEntrances,
                KantenVersatz = KantenVersatz,
                Zellen = Zellen,
                EineFlaeche = EineFlaeche,
                NoNotch = NoNotch,
                Single = Single,
                NoHalf = NoHalf,
            };
        }

        // Die Zuordnung steht in `Geometry/Winkelmodus.cs` - dort, wo sie
        // das Testprojekt pruefen kann. Hier stand sie einmal doppelt und
        // kannte "quer" nicht; siehe den Kopf jener Datei.
        internal static int EncodeAngleMode(string value)
            => Winkelmodus.Kodiere(value);

        internal static string DecodeAngleMode(int value)
            => Winkelmodus.Dekodiere(value);
    }

    [InternalBufferCapacity(0)]
    public struct ParkingLotBuildPoint : IBufferElementData, ISerializable
    {
        public const int CurrentVersion = 1;
        public int Version;
        public float3 Position;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(Position);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            reader.Read(out Position);
        }
    }

    [InternalBufferCapacity(0)]
    public struct ParkingLotBuildEntrance : IBufferElementData, ISerializable
    {
        public const int CurrentVersion = 1;
        public int Version;
        public int Edge;
        public double Along;
        /** 0=kein Eckfang, 1=start, 2=end. */
        public int Corner;
        public Zufahrtsart Art;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(Edge);
            writer.Write(Along);
            writer.Write(Corner);
            writer.Write((int)Art);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            reader.Read(out Edge);
            reader.Read(out Along);
            reader.Read(out Corner);
            reader.Read(out int art);
            Art = (Zufahrtsart)art;
        }
    }

    /**
     * Eine optionale Teilflaechen-Zuweisung des Bauzettels.
     *
     * VERSION IST DAS ERSTE FELD JEDES ELEMENTS. Der ECS-Serializer schreibt
     * Pufferelemente ohne Laengenrahmen hintereinander; ein spaeter
     * angehaengtes Feld darf deshalb nur nach einer lesbaren Version folgen.
     *
     * Anker und Linienenden sind die dauerhaften Merkmale. Der Winkel ist nur
     * der Rueckfall, falls die Linie nach einer Polygonaenderung verschwindet.
     */
    [InternalBufferCapacity(0)]
    public struct ParkingLotBuildAlignment : IBufferElementData, ISerializable
    {
        public const int CurrentVersion = 1;
        public int Version;
        public float2 Anchor;
        public float2 LineA;
        public float2 LineB;
        public double Angle;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(Anchor);
            writer.Write(LineA);
            writer.Write(LineB);
            writer.Write(Angle);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            reader.Read(out Anchor);
            reader.Read(out LineA);
            reader.Read(out LineB);
            reader.Read(out Angle);
        }
    }

    /**
     * Ein von Hand gezogener Trennschnitt des Bauzettels.
     *
     * WARUM ER MIT MUSS: die Schnitte bestimmen, WELCHE Teilflaechen es gibt,
     * und die Zuweisungen daneben haengen an genau diesen Flaechen. Ohne sie
     * faende ein bearbeiteter Parkplatz beim Uebernehmen wieder die
     * automatische Zerlegung vor - und die schneidet anders. Ansage des
     * Nutzers am 2026-09-01: *"der bzw. die Schnitte muessen mit in den
     * Bauzettel rein."*
     *
     * VERSION IST DAS ERSTE FELD, wie bei jedem Pufferelement hier: der
     * ECS-Serializer legt sie ohne Laengenrahmen hintereinander.
     *
     * Gespeichert werden die WELTLAGEN der beiden Ecken, nicht ihre Nummern -
     * dieselbe Lehre wie bei Zugaengen und Bezugslinien.
     */
    [InternalBufferCapacity(0)]
    /**
     * EINE GESETZTE ZONING-FLAECHE IM BAUZETTEL.
     *
     * Ohne sie waere ein gebauter Parkplatz beim Bearbeiten um seine
     * Parzellen aermer - genau das, was der Nutzer fuer die Trennschnitte
     * schon einmal verlangt hat. Sein Punkt 5 der Bedienung: *"Alles landet
     * im Rueckgaengig-Stapel UND im Bauzettel, damit ein gebauter Parkplatz
     * spaeter wieder bearbeitet werden kann."*
     *
     * Gemerkt wird die ECKE, nicht die Mitte - dieselbe Ecke, von der aus
     * `ParkingGeometry.ZoningEcken` rechnet. Wer hier die Mitte speicherte,
     * bekaeme bei jeder ungeraden Parzellenzahl eine halbe Zelle Versatz.
     *
     * Version steht als ERSTES Feld: Pufferelemente liegen im Spielstand
     * hintereinander, und wer die Version nicht zuerst liest, kann ein
     * aelteres Element nicht von einem neueren unterscheiden.
     */
    /**
     * EINE VON HAND GESCHALTETE STRASSENSEITE.
     *
     * Der Nutzer kann im Zoning-Reiter einzelne Seiten umschalten. Ohne
     * diesen Eintrag galte das nur bis zum naechsten Bau: der setzt alle
     * Seiten wieder auf die Panelwahl. Auf seine Frage *"geht das auch in
     * den Bauzettel?"* - ja, und dort gehoert es hin, denn der Zettel ist
     * das, woraus derselbe Parkplatz wieder entsteht.
     *
     * DIE KANTE WIRD UEBER IHRE ENDPUNKTE WIEDERGEFUNDEN, nicht ueber ihre
     * Entity: Entity-Verweise ueberleben das Laden nicht, das hat der
     * Sondenlauf am 2026-09-02 gezeigt. Aendert sich die Geometrie so weit,
     * dass keine Kante mehr passt, verfaellt der Eintrag stillschweigend -
     * besser als eine Seite zu schalten, die jemand anders meinte.
     */
    public struct ParkingLotBuildZoningSeite : IBufferElementData, ISerializable
    {
        public const int CurrentVersion = 1;
        public int Version;
        public float2 A;
        public float2 B;
        public bool Links;
        public bool Aus;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(A);
            writer.Write(B);
            writer.Write(Links);
            writer.Write(Aus);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            reader.Read(out A);
            reader.Read(out B);
            reader.Read(out Links);
            reader.Read(out Aus);
        }
    }

    /**
     * EINE UMRISSLINIE MIT RANDZONING.
     *
     * Zwei Punkte, keine Nummer - aus demselben Grund wie bei den
     * Strassenseiten und den Ausrichtungen: die Nummerierung des Umrisses
     * ueberlebt keine Bearbeitung.
     */
    public struct ParkingLotBuildRandzoning : IBufferElementData, ISerializable
    {
        public const int CurrentVersion = 1;
        public int Version;
        public float2 A;
        public float2 B;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(A);
            writer.Write(B);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            reader.Read(out A);
            reader.Read(out B);
        }
    }

    public struct ParkingLotBuildZoning : IBufferElementData, ISerializable
    {
        /**
         * Fassung 2 fuehrt den RAND mit.
         *
         * Er fehlte, und damit verlor ein geladener Parkplatz sein aeusseres
         * Bauland - derselbe Fehler wie in `ZoningVerschoben`, nur eine
         * Ebene tiefer. Alte Zettel bleiben gueltig: sie hatten den Rand nie
         * anders als in Strassenbreite, und genau die wird nachgetragen.
         */
        public const int CurrentVersion = 3;
        public int Version;
        public float2 Ecke;
        public int Spalten;
        public int Reihen;
        public double Winkel;
        public double Rand;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(Ecke);
            writer.Write(Spalten);
            writer.Write(Reihen);
            writer.Write(Winkel);
            writer.Write(Rand);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            reader.Read(out Ecke);
            reader.Read(out Spalten);
            reader.Read(out Reihen);
            reader.Read(out Winkel);
            if (Version >= 2) reader.Read(out Rand);
            else Rand = 8.0;
        }
    }

    public struct ParkingLotBuildCut : IBufferElementData, ISerializable
    {
        public const int CurrentVersion = 1;
        public int Version;
        public float2 A;
        public float2 B;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(A);
            writer.Write(B);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            reader.Read(out A);
            reader.Read(out B);
        }
    }

    /** UTF-8-Byte eines variablen Bauzetteltexts; Kind 1=Fahr-, 2=Zwischenfläche. */
    [InternalBufferCapacity(0)]
    public struct ParkingLotBuildText : IBufferElementData, ISerializable
    {
        public const int CurrentVersion = 1;
        public int Version;
        public int Kind;
        public int Index;
        public byte Value;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(Kind);
            writer.Write(Index);
            writer.Write(Value);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            reader.Read(out Kind);
            reader.Read(out Index);
            reader.Read(out Value);
        }
    }
}
