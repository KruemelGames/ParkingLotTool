using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ParkingLotTool.Tools
{
    /** Zeilen allein begrenzen keine 7-MB-Diagnosezeile (Reportbefund 16.10.2026).
     *  Deshalb schon beim Lesen begrenzen; Filter sehen auch den verworfenen Rest. */
    internal static class ParkingLotLogauszug
    {
        internal const int MaxBytes = 1024 * 1024;
        internal const int ZeilenBytes = 8 * 1024;
        private const string Zeilenhinweis = "[... Zeile gekuerzt ...]";
        private const string Auszughinweis = "[... Auszug gekuerzt; juengste Zeilen behalten ...]";
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly string Ende = Environment.NewLine;

        internal static string Lesen(string pfad, int zeilen, string kopf, string[] marken = null)
        {
            var sammlung = new Sammlung(zeilen);
            foreach (var zeile in Zeilen(pfad, marken))
                if (zeile.Passt) sammlung.FuegeHinzu(zeile.Text);
            return sammlung.Text(kopf);
        }

        // Auch nach der Anonymisierung gelten Byte- und Zeilengrenze.
        internal static string Begrenzen(string text)
        {
            var sammlung = new Sammlung(4096);
            using var leser = new StringReader(text);
            foreach (var zeile in Zeilen(leser, null)) sammlung.FuegeHinzu(zeile.Text);
            return sammlung.Text(string.Empty);
        }

        internal static IEnumerable<(string Text, bool Passt)> Zeilen(string pfad, string[] marken = null)
        {
            using var strom = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var leser = new StreamReader(strom, Utf8, true);
            foreach (var zeile in Zeilen(leser, marken)) yield return zeile;
        }

        private static IEnumerable<(string Text, bool Passt)> Zeilen(TextReader leser, string[] marken)
        {
            var ueberhang = 0;
            if (marken != null)
                foreach (var marke in marken) ueberhang = Math.Max(ueberhang, marke.Length - 1);
            int c;
            while ((c = leser.Read()) >= 0)
            {
                var zeile = new StringBuilder();
                var suche = new StringBuilder();
                var passt = marken == null;
                var bytes = 0;
                var gekuerzt = false;
                do
                {
                    if (c == '\r' || c == '\n') break;
                    var erstes = (char)c;
                    var zweites = char.IsHighSurrogate(erstes) && leser.Peek() >= 0
                        && char.IsLowSurrogate((char)leser.Peek()) ? (char)leser.Read() : '\0';
                    var breite = zweites != '\0' ? 4 : erstes <= 0x7f ? 1 : erstes <= 0x7ff ? 2 : 3;
                    if (!gekuerzt && bytes + breite <= ZeilenBytes - Utf8.GetByteCount(Ende))
                    {
                        zeile.Append(erstes);
                        if (zweites != '\0') zeile.Append(zweites);
                        bytes += breite;
                    }
                    else gekuerzt = true;
                    if (!passt)
                    {
                        suche.Append(erstes);
                        if (zweites != '\0') suche.Append(zweites);
                        if (suche.Length >= 4096)
                            passt = Suche(suche, marken, ueberhang);
                    }
                }
                while ((c = leser.Read()) >= 0);
                if (c == '\r' && leser.Peek() == '\n') leser.Read();
                if (!passt) passt = Suche(suche, marken, ueberhang);
                yield return (Kuerze(zeile.ToString(), gekuerzt), passt);
            }
        }

        private static bool Suche(StringBuilder suche, string[] marken, int ueberhang)
        {
            var text = suche.ToString();
            foreach (var marke in marken)
                if (text.IndexOf(marke, StringComparison.Ordinal) >= 0) return true;
            if (suche.Length > ueberhang) suche.Remove(0, suche.Length - ueberhang);
            return false;
        }

        private static string Kuerze(string text, bool gekuerzt = false)
        {
            var platz = ZeilenBytes - Utf8.GetByteCount(Ende);
            if (!gekuerzt && Utf8.GetByteCount(text) <= platz) return text;
            platz -= Utf8.GetByteCount(Zeilenhinweis);
            int laenge = 0, bytes = 0;
            while (laenge < text.Length)
            {
                var paar = char.IsHighSurrogate(text[laenge]) && laenge + 1 < text.Length
                    && char.IsLowSurrogate(text[laenge + 1]);
                var breite = paar ? 4 : text[laenge] <= 0x7f ? 1 : text[laenge] <= 0x7ff ? 2 : 3;
                if (bytes + breite > platz) break;
                bytes += breite;
                laenge += paar ? 2 : 1;
            }
            return text.Substring(0, laenge) + Zeilenhinweis;
        }

        internal sealed class Sammlung
        {
            // Eigener Ring: Queue<T> kollidiert im Mod mit einer zweiten Assembly.
            private readonly string[] _zeilen;
            private readonly int[] _bytes;
            private int _anfang, _anzahl, _gesamtbytes;
            private bool _gekuerzt;

            internal Sammlung(int zeilen)
            {
                if (zeilen < 1) throw new ArgumentOutOfRangeException(nameof(zeilen));
                _zeilen = new string[zeilen];
                _bytes = new int[zeilen];
            }

            internal void FuegeHinzu(string text)
            {
                text = Kuerze(text);
                var bytes = Utf8.GetByteCount(text) + Utf8.GetByteCount(Ende);
                while (_anzahl > 0 && (_anzahl == _zeilen.Length || _gesamtbytes + bytes > MaxBytes))
                    EntferneAelteste();
                var index = (_anfang + _anzahl) % _zeilen.Length;
                _zeilen[index] = text;
                _bytes[index] = bytes;
                _gesamtbytes += bytes;
                _anzahl++;
            }

            internal void BegrenzeZeilen(int anzahl)
            {
                while (_anzahl > anzahl) EntferneAelteste();
            }

            private void EntferneAelteste()
            {
                _gesamtbytes -= _bytes[_anfang];
                _zeilen[_anfang] = null;
                _anfang = (_anfang + 1) % _zeilen.Length;
                _anzahl--;
                _gekuerzt = true;
            }

            internal string Text(string kopf)
            {
                var vorspann = string.IsNullOrEmpty(kopf) ? string.Empty : kopf + Ende;
                var platz = MaxBytes - Utf8.GetByteCount(vorspann + Auszughinweis + Ende);
                if (platz < 0) throw new ArgumentException("Logkopf ueberschreitet Auszuggrenze.", nameof(kopf));
                while (_gesamtbytes > platz) EntferneAelteste();
                var text = new StringBuilder(vorspann);
                if (_gekuerzt) text.Append(Auszughinweis).Append(Ende);
                for (var i = 0; i < _anzahl; i++)
                    text.Append(_zeilen[(_anfang + i) % _zeilen.Length]).Append(Ende);
                return text.ToString();
            }
        }
    }
}
