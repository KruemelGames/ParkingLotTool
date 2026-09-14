using Colossal.Serialization.Entities;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * Was ein Parkplatz ueber sich weiss - mitgeschrieben, nicht abgelesen.
     *
     * CS2 fuehrt KEINE Statistik je Parkplatz. Es weiss nur, wer gerade
     * dasteht; eine Frage wie „wer kommt hier ueberhaupt her" laesst sich aus
     * einem einzigen Blick nicht beantworten. Um drei Uhr nachts stehen fuenf
     * Autos da, und die haetten dann die Mehrheit.
     *
     * Deshalb tastet `ParkingLotStatistikSystem` regelmaessig ab und fuehrt
     * hier eine Strichliste, die den Spielstand ueberlebt.
     *
     * GLEITENDE MITTEL STATT SUMMEN. Fast alle Werte sind exponentiell
     * geglaettete Mittel in Promille oder Zehnteln, keine aufaddierten
     * Summen. Zwei Gruende: eine Summe ueber Monate laeuft irgendwann aus
     * dem int heraus, und ein Mittel ueber die gesamte Lebenszeit wuerde
     * nach einem Umbau noch jahrelang den alten Zustand zeigen. Das
     * gleitende Mittel altert von selbst.
     *
     * Echte Zaehlungen bleiben nur, wo die Zahl selbst die Aussage ist:
     * `AutosGesamt` und `Proben`.
     */
    public struct ParkingLotStatistik : IComponentData,
                                        IQueryTypeParameter,
                                        ISerializable
    {
        public const int AktuelleVersion = 1;

        /**
         * Gewicht des neuen Wertes am gleitenden Mittel, in Promille.
         *
         * 60 heisst: nach etwa 17 Proben ist eine Aenderung zu zwei Dritteln
         * durchgeschlagen. Bei einer Probe alle zehn Sekunden sind das rund
         * drei Minuten Spielzeit - schnell genug, dass eine Gebuehrenaenderung
         * sichtbar wird, langsam genug, dass die Karte nicht flackert.
         */
        public const int Traegheit = 60;

        public int Version;

        /** Simulationsframe des Baus. 0 bei Parkplaetzen aus alten Staenden. */
        public uint GebautFrame;

        /** Wie oft abgetastet wurde. Unter 5 zeigt die Karte noch nichts. */
        public int Proben;

        /** Belegung in Promille der Kapazitaet, gleitendes Mittel. */
        public int BelegtPromille;

        /**
         * Zuletzt gezaehlte belegte Buchten - ungeglaettet.
         *
         * Die Liste zeigt diese Zahl als „belegt". Sie ist bis zu einer
         * Probe alt, also gut neun Sekunden; ein eigener Zaehldurchlauf je
         * Bildaufbau waere ein Vielfaches teurer und brachte dem Leser nichts.
         */
        public int BelegtJetzt;

        public int BelegtHoechst;
        public uint BelegtHoechstFrame;

        // Wofuer die Leute hier sind - je Zweck ein gleitender Anteil in
        // Promille. Die sechs summieren sich ungefaehr, aber nicht exakt auf
        // 1000; das ist bei getrennt geglaetteten Werten normal und fuer die
        // Mengenstufe ohne Belang.
        public int ZweckEinkaufen;
        public int ZweckArbeiten;
        public int ZweckNachHause;
        public int ZweckFreizeit;
        public int ZweckBesichtigen;
        public int ZweckSonstige;

        /** Anteil Touristen an den Fahrzeughaltern, Promille. */
        public int TouristPromille;

        /*
         * ALTERSGRUPPEN, KEIN ALTER IN JAHREN.
         *
         * CS2 kennt kein Lebensalter in Jahren: `Citizen.GetAge()` liefert
         * genau vier Stufen, und `GetAgeInDays` zaehlt Spieltage seit der
         * Geburt, nicht Jahre. Ein Satz wie „im Schnitt 34 Jahre alt" waere
         * frei erfunden gewesen. Stattdessen vier gleitende Anteile in
         * Promille - damit greift dieselbe Mengenstufe wie ueberall sonst.
         */
        public int AlterKind;
        public int AlterJugend;
        public int AlterErwachsen;
        public int AlterSenior;

        /** Anteil mit Hochschulabschluss, Promille. */
        public int BildungPromille;

        /** Zufriedenheit der Gaeste, 0 bis 100. */
        public int Zufriedenheit;

        /** Restlicher Fussweg zum Ziel, Dezimeter, gleitendes Mittel. */
        public int FusswegDezimeter;

        /** Wie oft eine Fahrt hierher gescheitert ist, gleitend gezaehlt. */
        public int KeinWegPromille;

        /** Beobachtete Neuankuenfte seit dem Bau. Echte Zaehlung. */
        public int AutosGesamt;

        /** Mittlere Standzeit in Minuten Spielzeit. */
        public int StandzeitMinuten;

        // Fuer „du hast die Gebuehr geaendert, seitdem ...": der letzte
        // bekannte Gebuehrenstand, wann er sich aenderte, und wie voll es
        // davor war.
        public int LetzteGebuehr;
        public uint GebuehrFrame;
        public int BelegtVorGebuehr;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            /*
             * DIE VERSION STEHT VORNE, UND ZWAR MIT ABSICHT.
             *
             * Der Serializer legt alle Instanzen einer Komponente ohne
             * Einzelblock hintereinander. Ein spaeter angehaengtes Feld
             * wuerde alte Staende nicht erweitern, sondern den folgenden
             * Datensatz verschieben - siehe die ausfuehrliche Begruendung an
             * `ParkingLotEconomyData`. Weil diese Komponente neu ist, kann
             * sie sich das Versionsfeld von Anfang an leisten; damit ist ein
             * spaeteres Feld ein Zweizeiler statt einer Bitpackerei.
             */
            writer.Write(AktuelleVersion);
            writer.Write(GebautFrame);
            writer.Write(Proben);
            writer.Write(BelegtPromille);
            writer.Write(BelegtJetzt);
            writer.Write(BelegtHoechst);
            writer.Write(BelegtHoechstFrame);
            writer.Write(ZweckEinkaufen);
            writer.Write(ZweckArbeiten);
            writer.Write(ZweckNachHause);
            writer.Write(ZweckFreizeit);
            writer.Write(ZweckBesichtigen);
            writer.Write(ZweckSonstige);
            writer.Write(TouristPromille);
            writer.Write(AlterKind);
            writer.Write(AlterJugend);
            writer.Write(AlterErwachsen);
            writer.Write(AlterSenior);
            writer.Write(BildungPromille);
            writer.Write(Zufriedenheit);
            writer.Write(FusswegDezimeter);
            writer.Write(KeinWegPromille);
            writer.Write(AutosGesamt);
            writer.Write(StandzeitMinuten);
            writer.Write(LetzteGebuehr);
            writer.Write(GebuehrFrame);
            writer.Write(BelegtVorGebuehr);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            reader.Read(out GebautFrame);
            reader.Read(out Proben);
            reader.Read(out BelegtPromille);
            reader.Read(out BelegtJetzt);
            reader.Read(out BelegtHoechst);
            reader.Read(out BelegtHoechstFrame);
            reader.Read(out ZweckEinkaufen);
            reader.Read(out ZweckArbeiten);
            reader.Read(out ZweckNachHause);
            reader.Read(out ZweckFreizeit);
            reader.Read(out ZweckBesichtigen);
            reader.Read(out ZweckSonstige);
            reader.Read(out TouristPromille);
            reader.Read(out AlterKind);
            reader.Read(out AlterJugend);
            reader.Read(out AlterErwachsen);
            reader.Read(out AlterSenior);
            reader.Read(out BildungPromille);
            reader.Read(out Zufriedenheit);
            reader.Read(out FusswegDezimeter);
            reader.Read(out KeinWegPromille);
            reader.Read(out AutosGesamt);
            reader.Read(out StandzeitMinuten);
            reader.Read(out LetzteGebuehr);
            reader.Read(out GebuehrFrame);
            reader.Read(out BelegtVorGebuehr);
        }

        /**
         * Zieht einen neuen Messwert ins gleitende Mittel.
         *
         * Beim allerersten Mal wird der Wert uebernommen statt gemittelt -
         * sonst kroche eine frisch gebaute Anlage minutenlang von Null hoch
         * und behauptete solange, sie sei leer.
         */
        public static int Glaette(int bisher, int neu, bool ersteProbe)
        {
            if (ersteProbe) return neu;
            return bisher + ((neu - bisher) * Traegheit) / 1000;
        }
    }

    /**
     * Wohin die Gaeste eines Parkplatzes zu Fuss weitergehen.
     *
     * Eine Zeile je Zielgebaeude. Der Puffer ist gedeckelt (siehe
     * `ParkingLotStatistikSystem.ZieleHoechstens`); ist er voll, faellt das
     * Ziel mit dem kleinsten Gewicht heraus. So ueberlebt die Rangliste auch
     * einen Parkplatz mitten in der Innenstadt, ohne unbegrenzt zu wachsen.
     */
    public struct ParkingLotZielwert : IBufferElementData, ISerializable
    {
        public const int AktuelleVersion = 1;

        public Entity Gebaeude;

        /** Gleitendes Gewicht, kein Zaehler: alte Ziele altern heraus. */
        public int Gewicht;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(AktuelleVersion);
            writer.Write(Gebaeude);
            writer.Write(Gewicht);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int _);
            reader.Read(out Gebaeude);
            reader.Read(out Gewicht);
        }
    }

    /**
     * Belegung nach Tageszeit - vierundzwanzig feste Zeilen, Stunde 0 bis 23.
     *
     * Traegt das Tagesprofil („voll ab acht, leer ab achtzehn") und die
     * Spitzenstunde. Feste Laenge, damit der Index die Stunde IST und kein
     * Suchen noetig wird.
     */
    public struct ParkingLotStundenwert : IBufferElementData, ISerializable
    {
        public const int AktuelleVersion = 1;

        /** Belegung in Promille zu dieser Stunde, gleitendes Mittel. */
        public int BelegtPromille;

        /** Wie oft in dieser Stunde schon gemessen wurde. */
        public int Proben;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(AktuelleVersion);
            writer.Write(BelegtPromille);
            writer.Write(Proben);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int _);
            reader.Read(out BelegtPromille);
            reader.Read(out Proben);
        }
    }

    /**
     * Die letzten Tage, als Ring.
     *
     * `Tag` ist der Spieltag, damit beim Ueberschreiben feststeht, welcher
     * Eintrag der aelteste ist. Ohne dieses Feld wuesste man nach dem Laden
     * nicht mehr, wo der Ring gerade steht.
     */
    public struct ParkingLotTagwert : IBufferElementData, ISerializable
    {
        public const int AktuelleVersion = 1;

        public int Tag;
        public int Autos;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(AktuelleVersion);
            writer.Write(Tag);
            writer.Write(Autos);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int _);
            reader.Read(out Tag);
            reader.Read(out Autos);
        }
    }
}
