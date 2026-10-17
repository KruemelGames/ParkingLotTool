using System;
using System.Collections.Generic;
using Unity.Entities;

// Rahmen fuer die produktive Infosektion; keine Reparatur oder Bauarbeit wird ausgefuehrt.
namespace UnityEngine.Scripting { public sealed class PreserveAttribute : Attribute { } }
namespace Colossal.UI.Binding
{
    public interface IJsonWriter { void PropertyName(string name); void Write<T>(T value); }
    public sealed class TriggerBinding
    {
        public readonly string Name; public readonly Action Aktion;
        public TriggerBinding(string gruppe, string name, Action aktion) { Name = name; Aktion = aktion; }
    }
    public sealed class JsonWriter : IJsonWriter
    {
        private string _name;
        public readonly Dictionary<string, object> Werte = new();
        public void PropertyName(string name) => _name = name;
        public void Write<T>(T value) => Werte[_name] = value;
    }
}
namespace Game.UI.InGame
{
    public abstract class InfoSectionBase
    {
        public World World { get; set; }
        public EntityManager EntityManager => World.EntityManager;
        protected Entity selectedEntity;
        protected bool visible;
        protected readonly InfoUI m_InfoUISystem = new();
        public readonly Dictionary<string, Action> Ausloeser = new();
        protected abstract string group { get; }
        protected virtual void OnCreate() { }
        protected abstract void Reset();
        protected abstract void OnUpdate();
        protected abstract void OnProcess();
        public abstract void OnWriteProperties(Colossal.UI.Binding.IJsonWriter writer);
        protected void AddBinding(Colossal.UI.Binding.TriggerBinding binding) => Ausloeser[binding.Name] = binding.Aktion;
        protected void RequestUpdate() { }
        public void Erzeuge() => OnCreate();
        public bool Aktualisiere(Entity e) { selectedEntity = e; OnUpdate(); return visible; }
    }
    public sealed class InfoUI { public void AddMiddleSection(InfoSectionBase section) { } }
}
namespace ParkingLotTool
{
    public static class ParkingLotTexte { public static string T(string key) => key; }
}
namespace ParkingLotTool.Tools
{
    public sealed class ParkingLotWaisenSystem
    {
        public int Status, Reparaturen;
        public readonly List<Entity> Halbwaisen = new();
        public int Zustand(Entity lot) => Status;
        public void Reparieren(Entity lot) => Reparaturen++;
    }
    public sealed class ParkingLotHintergrundSystem
    {
        public string Grund = "";
        public string Sperrgrund(Entity lot) => Grund;
    }
    public sealed class ParkingLotSyncSystem { public bool ArbeitLaeuft; }
    public sealed class ParkingLotToolSystem
    {
        public const string LotOwnerPrefabName = "PLT Parkplatzflaeche";
        public bool IsEditing;
    }
    public struct ParkingLotOffenerErsatz { }
    public static class ParkingLotBaukontextLeser
    {
        public static bool Vollstaendig(EntityManager em, Entity lot)
            => em.HasComponent<ParkingLotCarrierReference>(lot) && em.HasComponent<ParkingLotBuildReceipt>(lot)
                && em.HasBuffer<ParkingLotBuildPoint>(lot) && em.HasBuffer<ParkingLotBuildEntrance>(lot)
                && em.HasBuffer<ParkingLotBuildText>(lot);
    }
    public static class ParkingLotSchrittmarke { public static void Setze(string text) { } }
    public static class ParkingLotListeUISystem { public static string SchluesselVon(Entity e) => e.Index + ":" + e.Version; }
}
