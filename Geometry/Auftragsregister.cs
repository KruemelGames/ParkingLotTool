using System;
using System.Collections.Generic;

namespace ParkingLotTool.Geometry
{
    public enum Auftragsgrund { Migration, Repair, Wiederaufnahme, Normalbau }
    public enum Auftragsarbeiter { Fahrweg, Bestand, Flaechen, Strasse, Hintergrund }
    public enum Auftragsphase { Vorgemerkt, Angenommen, Gestartet, Abschluss }

    public readonly struct Auftragskennung : IEquatable<Auftragskennung>
    {
        public readonly long Id, Generation;
        public Auftragskennung(long id, long generation) { Id = id; Generation = generation; }
        public bool IstLeer => Id == 0;
        public bool Equals(Auftragskennung andere) => Id == andere.Id && Generation == andere.Generation;
        public override bool Equals(object andere) => andere is Auftragskennung id && Equals(id);
        public override int GetHashCode() => Id.GetHashCode() ^ Generation.GetHashCode();
        public static bool operator ==(Auftragskennung a, Auftragskennung b) => a.Equals(b);
        public static bool operator !=(Auftragskennung a, Auftragskennung b) => !a.Equals(b);
        public override string ToString() => Generation + "/" + Id;
    }

    public sealed class Bauauftrag<T>
    {
        public Auftragskennung Kennung { get; internal set; }
        public long Nutzer { get; internal set; }
        public T Lot { get; internal set; }
        public Auftragsarbeiter Arbeiter { get; internal set; }
        public Auftragsgrund Grund { get; internal set; }
        public int Schritt { get; internal set; }
        public Auftragsphase Phase { get; internal set; }
        public double Vorgemerkt { get; internal set; }
        public double Gestartet { get; internal set; }
        public double LetzteAenderung { get; internal set; }
        public string Wartegrund { get; internal set; }
    }

    public readonly struct Auftragsfortschritt
    {
        public readonly int Gesamt, Erledigt, Fehler, OffeneTeile;
        public Auftragsfortschritt(int gesamt, int erledigt, int fehler, int offeneTeile)
        { Gesamt = gesamt; Erledigt = erledigt; Fehler = fehler; OffeneTeile = offeneTeile; }
    }

    /** Nur Laufzeit. Absichten im Save bleiben die Wahrheit; nach Laden entstehen neue Kennungen.
     *  Zeit kommt vom Aufrufer. Keine Fristen, Abbrueche oder automatischen Speicheraufrufe. */
    public sealed class Auftragsregister<T>
    {
        private sealed class Nutzerauftrag
        {
            internal int Offen;
            internal bool Automatisch, Fehler;
        }
        private readonly Dictionary<long, Bauauftrag<T>> _offen = new();
        private readonly Dictionary<long, Nutzerauftrag> _nutzer = new();
        private long _naechsteId;
        private int _gesamt, _erledigt, _fehler;
        public long Generation { get; private set; } = 1;
        public bool Bereit { get; private set; }
        public int Offen => _offen.Count;
        public int OffeneNutzer => _nutzer.Count;

        public void NeuesLaden(double zeit)
        {
            Generation++;
            Bereit = false;
            _offen.Clear();
            _nutzer.Clear();
            _gesamt = _erledigt = _fehler = 0;
        }

        public void PufferWiederhergestellt() => Bereit = true;

        public long NeuerNutzer()
        {
            var id = ++_naechsteId;
            _nutzer.Add(id, new Nutzerauftrag());
            _gesamt++;
            return id;
        }

        public Auftragskennung Merke(T lot, Auftragsarbeiter arbeiter, Auftragsgrund grund,
            int schritt, double zeit, long nutzer = 0)
        {
            if (!Bereit) throw new InvalidOperationException("Auftragsregister vor Puffer-Wiederherstellung");
            if (grund == Auftragsgrund.Migration ? schritt <= 0 : schritt != 0)
                throw new ArgumentException("Nur Migrationen haben einen Schritt.");
            bool automatisch = nutzer == 0;
            if (automatisch) nutzer = NeuerNutzer();
            if (!_nutzer.TryGetValue(nutzer, out var gruppe))
                throw new InvalidOperationException("Nutzerauftrag nicht offen");
            gruppe.Automatisch |= automatisch;
            var id = new Auftragskennung(++_naechsteId, Generation);
            _offen.Add(id.Id, new Bauauftrag<T> { Kennung = id, Nutzer = nutzer, Lot = lot,
                Arbeiter = arbeiter, Grund = grund, Schritt = schritt, Vorgemerkt = zeit,
                LetzteAenderung = zeit, Wartegrund = "Uebergabe", Phase = Auftragsphase.Vorgemerkt });
            gruppe.Offen++;
            return id;
        }

        public bool Lies(Auftragskennung id, out Bauauftrag<T> auftrag)
        {
            auftrag = null;
            return Bereit && id.Generation == Generation && _offen.TryGetValue(id.Id, out auftrag);
        }

        public bool Annehmen(Auftragskennung id, T lot, Auftragsarbeiter arbeiter, double zeit)
        {
            if (!Lies(id, out var a) || a.Phase != Auftragsphase.Vorgemerkt
                || a.Arbeiter != arbeiter || !EqualityComparer<T>.Default.Equals(a.Lot, lot)) return false;
            a.Phase = Auftragsphase.Angenommen;
            a.LetzteAenderung = zeit; a.Wartegrund = "Warteschlange";
            return true;
        }

        public bool Starte(Auftragskennung id, double zeit)
        {
            if (!Lies(id, out var a) || a.Phase != Auftragsphase.Angenommen) return false;
            a.Phase = Auftragsphase.Gestartet;
            a.Gestartet = a.LetzteAenderung = zeit; a.Wartegrund = "Arbeitet";
            return true;
        }

        public bool DarfAnwenden(Auftragskennung id)
            => Lies(id, out var a) && a.Phase == Auftragsphase.Gestartet;

        public bool Fortschritt(Auftragskennung id, string grund, double zeit)
        {
            if (!Lies(id, out var a) || a.Phase == Auftragsphase.Abschluss || a.Wartegrund == grund) return false;
            a.Wartegrund = grund; a.LetzteAenderung = zeit;
            return true;
        }

        // Die Kennung wird VOR jedem Abschluss-Effekt beansprucht. Auch ein
        // werfender Empfaenger oder ein reentranter Doppelabschluss wirkt nur einmal.
        public bool Beende(Auftragskennung id, double zeit, Func<Bauauftrag<T>, bool> wirkung)
        {
            if (!Lies(id, out var a) || a.Phase == Auftragsphase.Abschluss) return false;
            bool angenommen = a.Phase != Auftragsphase.Vorgemerkt;
            a.Phase = Auftragsphase.Abschluss; a.LetzteAenderung = zeit;
            bool ok = false;
            try { ok = wirkung(a) && angenommen; }
            finally
            {
                if (id.Generation == Generation && _offen.Remove(id.Id))
                {
                    var n = _nutzer[a.Nutzer];
                    n.Offen--; n.Fehler |= !ok;
                    if (n.Automatisch && n.Offen == 0) NutzerBeendet(a.Nutzer, !n.Fehler);
                }
            }
            return true;
        }

        public bool NutzerBeendet(long nutzer, bool ok)
        {
            if (!_nutzer.TryGetValue(nutzer, out var n) || n.Offen != 0) return false;
            _nutzer.Remove(nutzer);
            _erledigt++;
            if (n.Fehler || !ok) _fehler++;
            return true;
        }

        // Fertige Gruppen brauchen nur noch Zaehler, keine Lot-Verweise.
        public Auftragsfortschritt Zaehle() => new Auftragsfortschritt(_gesamt, _erledigt, _fehler, _offen.Count);
    }
}
