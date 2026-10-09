using System;
using System.Collections.Generic;
using System.IO;
using Colossal.Localization;
using Game.SceneFlow;
using Newtonsoft.Json;
using ParkingLotTool.Geometry;

namespace ParkingLotTool.Tools
{
    /**
     * LIEST `Lang/*.json` UND MELDET DIE TEXTE BEIM SPIEL AN.
     *
     * Die Dateien liegen neben der DLL. Jede heisst wie die Sprachkennung
     * des Spiels (`en-US.json`, `de-DE.json`, `zh-HANS.json`) - eine neue
     * Sprache ist also nur eine neue Datei, kein Build.
     *
     * Zwei Wege fuehren zu einem Text:
     *
     *   1. `Sprachtexte` - fuer alles, was der Mod selbst zusammensetzt
     *      (Statuszeile, Hinweise, Panel). Diese Klasse fuellt sie.
     *   2. Das Woerterbuch des Spiels unter `ParkingLotTool.<schluessel>` -
     *      dort koennen Mods wie I18n Everywhere jeden Text ersetzen, ohne
     *      dass PLT von ihnen weiss. Steht die Sprache auf "Automatisch",
     *      liest `Sprachtexte` zuerst dort nach.
     *
     * Bei einer ausdruecklich gewaehlten Sprache gilt nur die Datei: wer
     * "English", "Deutsch" oder "Portugues" waehlt, soll genau die bekommen.
     */
    internal static class ParkingLotSprachdateien
    {
        internal const string Praefix = "ParkingLotTool.";

        /** Ordner neben der DLL. Leer, solange der Asset-Pfad fehlt. */
        internal static string Ordner { get; private set; } = string.Empty;

        /** Zaehlt mit, wenn die Sprache wechselt - die Oberflaeche liest danach neu. */
        internal static int Stand { get; private set; }

        private static string _gemeldet;
        private static bool _angemeldet;

        internal static void Lade(string dllPfad)
        {
            Sprachtexte.Sprachen.Clear();
            Ordner = string.IsNullOrEmpty(dllPfad)
                ? string.Empty
                : Path.Combine(Path.GetDirectoryName(dllPfad) ?? string.Empty, "Lang");
            if (!Directory.Exists(Ordner))
            {
                Mod.log.Error("PLT-Sprache: Ordner fehlt: " + Ordner
                    + " - alle Texte erscheinen als [schluessel].");
                return;
            }
            foreach (var datei in Directory.GetFiles(Ordner, "*.json"))
            {
                var sprache = Path.GetFileNameWithoutExtension(datei);
                try
                {
                    var texte = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                        File.ReadAllText(datei));
                    if (texte == null) continue;
                    Sprachtexte.Sprachen[sprache] = new Dictionary<string, string>(
                        texte, StringComparer.Ordinal);
                    Mod.log.Info("PLT-Sprache: " + sprache + " mit "
                        + texte.Count + " Texten geladen.");
                }
                catch (Exception ausnahme)
                {
                    // Eine kaputte Uebersetzung darf den Mod nicht anhalten;
                    // ihre Texte fallen dann auf Englisch zurueck.
                    Mod.log.Error("PLT-Sprache: " + Path.GetFileName(datei)
                        + " nicht lesbar: " + ausnahme.Message);
                }
            }
            if (!Sprachtexte.Sprachen.ContainsKey(Sprachtexte.Rueckfall))
                Mod.log.Error("PLT-Sprache: " + Sprachtexte.Rueckfall
                    + ".json fehlt - es gibt keinen Rueckfall.");
        }

        /**
         * Jede Datei als eigene Quelle beim Spiel. Eine Sprache, die das
         * Spiel nicht kennt, nimmt es stillschweigend nicht an; fehlende
         * Eintraege fuellt es selbst aus `en-US` auf.
         */
        internal static void MeldeAn()
        {
            var lm = GameManager.instance?.localizationManager;
            if (lm == null || _angemeldet) return;
            _angemeldet = true;
            foreach (var paar in Sprachtexte.Sprachen)
            {
                var mitPraefix = new Dictionary<string, string>(paar.Value.Count);
                foreach (var eintrag in paar.Value)
                    mitPraefix[Praefix + eintrag.Key] = eintrag.Value;
                lm.AddSource(paar.Key, new MemorySource(mitPraefix));
            }
            lm.onActiveDictionaryChanged += WoerterbuchGewechselt;
            Pflege();
        }

        /**
         * Die Einstellungsseite unter jeder Spielsprache. Dieselbe Quelle
         * liefert ueberall die im Mod gewaehlte Sprache.
         */
        internal static void MeldeEinstellungenAn(Setting setting)
        {
            var lm = GameManager.instance?.localizationManager;
            if (lm == null) return;
            var quelle = new Beschriftungen(setting);
            var sprachen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { Sprachtexte.Rueckfall };
            foreach (var s in lm.GetSupportedLocales()) sprachen.Add(s);
            foreach (var s in Sprachtexte.Sprachen.Keys) sprachen.Add(s);
            foreach (var s in sprachen) lm.AddSource(s, quelle);
        }

        /**
         * Stellt `Sprachtexte` auf die gewaehlte Sprache - nach dem Laden und
         * nach jeder Aenderung in den Optionen. Ohne Wechsel kostet es einen
         * Vergleich.
         */
        internal static void Pflege()
        {
            if (!Stelle(out var ersterLauf)) return;
            /*
             * DIE EINSTELLUNGSSEITE ZIEHT SOFORT MIT.
             *
             * Bis 1.0.6 galt eine neue Sprache dort erst nach einem
             * Neustart. `ReloadActiveLocale` laesst das Spiel alle Quellen
             * neu lesen - dieselbe Mechanik nutzt schon der Aufraeumknopf.
             * Nicht beim ersten Lauf: da liest das Spiel ohnehin gerade.
             * Auch beim Wechsel auf "Automatisch", denn ausgeloest hat ihn
             * hier die Mod-Einstellung, nicht das Spiel.
             */
            if (ersterLauf) return;
            try { GameManager.instance?.localizationManager?.ReloadActiveLocale(); }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Sprache: Neulesen fehlgeschlagen: " + ausnahme.Message);
            }
        }

        /**
         * Das Spiel hat sein Woerterbuch neu gefuellt - Sprachwechsel im
         * Spiel, ein Mod wie I18n Everywhere, oder unser eigenes Neulesen.
         *
         * Kein `ReloadActiveLocale` von hier: das loest genau dieses Ereignis
         * wieder aus. Bei "Automatisch" zaehlt `Stand` trotzdem weiter, auch
         * ohne Sprachwechsel - eine geaenderte Ueberschreibung soll auch im
         * Panel ankommen, nicht nur in neu erzeugten Statuszeilen.
         */
        private static void WoerterbuchGewechselt()
        {
            if (Stelle(out _)) return;
            if (Sprachtexte.Ueberschreibung != null) Stand++;
        }

        /** Uebernimmt die gewaehlte Sprache. Wahr, wenn sie sich geaendert hat. */
        private static bool Stelle(out bool ersterLauf)
        {
            var setting = Mod.Optionen;
            var sprache = setting?.SprachId() ?? Sprachtexte.Rueckfall;
            var automatisch = setting != null
                && setting.Sprache == Setting.Sprachwahl.Automatic;
            var kennung = sprache + (automatisch ? "|auto" : string.Empty);
            ersterLauf = _gemeldet == null;
            if (kennung == _gemeldet) return false;
            _gemeldet = kennung;
            Sprachtexte.Aktiv = sprache;
            Sprachtexte.Ueberschreibung = automatisch ? AusDemSpiel : (Func<string, string>)null;
            Stand++;
            Mod.log.Info("PLT-Sprache: angezeigt wird " + sprache
                + (automatisch ? " (automatisch, Woerterbuch des Spiels zuerst)" : string.Empty));
            return true;
        }

        private static string AusDemSpiel(string schluessel)
        {
            var dict = GameManager.instance?.localizationManager?.activeDictionary;
            return dict != null && dict.TryGetValue(Praefix + schluessel, out var text)
                ? text : null;
        }

        /**
         * Alle Oberflaechentexte (`ui.*`) der angezeigten Sprache als JSON,
         * fuer die Bindung "Sprachtexte". Ohne Praefix `ui.`, englische
         * Eintraege fuellen Luecken.
         */
        internal static string OberflaechenJson()
        {
            var aus = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Sprachtexte.Sprachen.TryGetValue(Sprachtexte.Rueckfall, out var en))
                foreach (var e in en)
                    if (e.Key.StartsWith("ui.", StringComparison.Ordinal))
                        aus[e.Key.Substring(3)] = Sprachtexte.Roh(e.Key);
            if (Sprachtexte.Sprachen.TryGetValue(Sprachtexte.Aktiv, out var akt))
                foreach (var e in akt)
                    if (e.Key.StartsWith("ui.", StringComparison.Ordinal))
                        aus[e.Key.Substring(3)] = Sprachtexte.Roh(e.Key);
            return JsonConvert.SerializeObject(aus);
        }
    }
}
