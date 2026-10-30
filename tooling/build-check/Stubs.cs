// Shape-accurate stubs for: UnityEngine, TMPro, UnityEngine.UI, HarmonyLib, BepInEx, and the
// Valheim types the mod touches. This file exists ONLY so the mod's five real source files can be
// compiled and type-checked on a machine without the game or the mod DLLs. It is not shipped and
// must never be added to the mod project.
//
// Members are declared with the signatures the mod actually uses, matching what the decompiled
// mod sources and the Harmony/BepInEx 5 APIs expose.

using System;
using System.Collections.Generic;
using System.Reflection;

// ---------------------------------------------------------------------------------------------
// UnityEngine
// ---------------------------------------------------------------------------------------------
namespace UnityEngine
{
    public class Object
    {
        public string name;
        public static implicit operator bool(Object o) => o != null;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => base.GetHashCode();
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color red => new Color(1f, 0f, 0f, 1f);
        public static Color Lerp(Color a, Color b, float t) => a;
    }

    public static class ColorUtility
    {
        public static string ToHtmlStringRGBA(Color color) => string.Empty;
    }

    public static class Mathf
    {
        public static int Clamp(int value, int min, int max) => value;
        public static float Clamp(float value, float min, float max) => value;
        public static int Max(int a, int b) => a;
        public static float Max(float a, float b) => a;
        public static int Min(int a, int b) => a;
        public static float Floor(float f) => f;
        public static float PingPong(float t, float length) => t;
    }

    public static class Time
    {
        public static int frameCount;
        public static float time;
        public static float unscaledTime;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform;
        public int GetInstanceID() => 0;
        public T GetComponent<T>() where T : class => null;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class => null;
        public T GetComponentInParent<T>() where T : class => null;
    }

    public class Behaviour : Component
    {
        public bool enabled;
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public class Transform : Component
    {
        public Transform parent;
        public Vector3 position;
        public Transform Find(string path) => null;
    }

    public class RectTransform : Transform
    {
    }

    public class GameObject : Object
    {
        public bool activeSelf;
        public Transform transform;
        public void SetActive(bool value) { }
        public T GetComponent<T>() where T : class => null;
        public T GetComponentInChildren<T>() where T : class => null;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class => null;
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => 0f;
        public static Vector3 operator -(Vector3 a, Vector3 b) => a;
    }

    public enum FindObjectsSortMode
    {
        None,
        InstanceID
    }

    public static class ObjectStatics
    {
        // placeholder to keep the namespace populated
    }
}

// ---------------------------------------------------------------------------------------------
// TMPro / UnityEngine.UI
// ---------------------------------------------------------------------------------------------
namespace TMPro
{
    public enum TextWrappingModes
    {
        NoWrap,
        Normal,
        PreserveWhitespace
    }

    public enum TextOverflowModes
    {
        Overflow,
        Ellipsis,
        Masking,
        Truncate
    }

    public class TMP_Text : UnityEngine.Component
    {
        public string text;
        public UnityEngine.Color color;
        public bool enableAutoSizing;
        public float fontSizeMin;
        public float fontSizeMax;
        public TMPro.TextWrappingModes textWrappingMode;
        public TMPro.TextOverflowModes overflowMode;
        public void SetText(string value) { }
    }

    public class TextMeshProUGUI : TMP_Text
    {
    }

    public class TMP_InputField : UnityEngine.Component
    {
        public string text;
    }
}

namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.Component
    {
        public UnityEngine.Color color;
    }

    public class Selectable : UnityEngine.Component
    {
        public bool interactable;
    }

    public class Button : Selectable
    {
    }

    public class Image : Graphic
    {
    }
}

// ---------------------------------------------------------------------------------------------
// Valheim (assembly_valheim, global namespace)
// ---------------------------------------------------------------------------------------------
public struct Vector2i
{
    public int x, y;
    public Vector2i(int x, int y) { this.x = x; this.y = y; }
}

public class ItemDrop : UnityEngine.Component
{
    public ItemData m_itemData;

    public class SharedData
    {
        public string m_name;
        public int m_itemType;
    }

    public class ItemData
    {
        public SharedData m_shared;
        public int m_stack;
        public int m_quality;
        public int m_worldLevel;
        public Vector2i m_gridPos;
        public ItemDrop m_dropPrefab;
        public ItemData Clone() => this;
    }
}

public static class Piece
{
    public class Requirement
    {
        public ItemDrop m_resItem;
        public int m_amount;
        public int m_amountPerLevel;
        public int GetAmount(int quality) => m_amount;
    }
}

public class Recipe
{
    public Piece.Requirement[] m_resources;
    public bool m_requireOnlyOneIngredient;
    public ItemDrop m_item;
    public int m_minStationLevel;
}

public class Inventory
{
    public List<ItemDrop.ItemData> GetAllItems() => null;
    public int GetHeight() => 0;
    public int GetWidth() => 0;
    public string GetName() => string.Empty;
    public int CountItems(string name) => 0;
    public int CountItems(string name, int quality, bool matchWorldLevel) => 0;
    public void Changed() { }
    public void UpdateTotalWeight() { }
}

public class Container : UnityEngine.Component
{
    public Inventory GetInventory() => null;
    public bool Load() => true;
    public bool TakeAll(Humanoid character) => true;
}

public class Humanoid : UnityEngine.MonoBehaviour
{
    public Inventory GetInventory() => null;
    public bool IsItemEquiped(ItemDrop.ItemData item) => false;
}

public class Player : Humanoid
{
    public static Player m_localPlayer;

    public bool NoCostCheat() => false;
    public bool IsKnownMaterial(string name) => false;
    public bool HaveRequirements(Recipe recipe, bool craft, int quality) => false;
    public bool HaveRequirements(Recipe recipe, bool discover, int quality, int amount) => false;
    public bool HaveRequirements(Recipe recipe) => false;
}

public class InventoryGui : UnityEngine.MonoBehaviour
{
    public static InventoryGui instance;
    public static bool IsVisible() => true;

    public UnityEngine.UI.Button m_craftButton;
    public UnityEngine.Transform m_player;
    public UnityEngine.Transform m_container;
    public UnityEngine.Transform m_crafting;
    public object m_selectedRecipe;
    public float m_craftTimer;

    public bool InUpradeTab() => false;
    public bool InCraftTab() => true;
    public void UpdateRecipe(Player player) { }
    public void UpdateRecipe(Player player, float dt) { }
    public bool SetupRequirement(
        UnityEngine.Transform elementRoot,
        Piece.Requirement req,
        Player player,
        bool craft,
        int quality) => true;
    public void Update() { }
}

public class ZoneSystem
{
    public static ZoneSystem instance;
    public bool GetGlobalKey(string key) => false;
}

public static class GlobalKeys
{
    public const string NoCraftCost = "nocost";
    public const string NoBuildCost = "nobuildcost";
}

public class PrivateArea
{
    public static bool CheckAccess(UnityEngine.Vector3 point, float radius, bool build, bool flash) => true;
}

// ---------------------------------------------------------------------------------------------
// HarmonyLib
// ---------------------------------------------------------------------------------------------
namespace HarmonyLib
{
    public enum Priority
    {
        First = 800,
        Normal = 400,
        Last = 100
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type declaringType) { }
        public HarmonyPatch(Type declaringType, string methodName) { }
        public HarmonyPatch(Type declaringType, string methodName, params Type[] argumentTypes) { }
        public HarmonyPatch(string methodName) { }
        public HarmonyPatch(MethodType methodType) { }
        public HarmonyPatch(Type declaringType, MethodType methodType) { }
        public HarmonyPatch(Type declaringType, MethodType methodType, params Type[] argumentTypes) { }
    }

    public enum MethodType
    {
        Normal,
        Getter,
        Setter,
        Constructor,
        StaticConstructor,
        Enumerator
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyFinalizer : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyTranspiler : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyTargetMethod : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrepare : Attribute { }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class HarmonyPriority : Attribute
    {
        public HarmonyPriority(int priority) { }
        public HarmonyPriority(Priority priority) { }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class HarmonyAfter : Attribute
    {
        public HarmonyAfter(params string[] after) { }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class HarmonyBefore : Attribute
    {
        public HarmonyBefore(params string[] before) { }
    }

    public class HarmonyMethod
    {
        public HarmonyMethod() { }
        public HarmonyMethod(MethodInfo method) { }
        public HarmonyMethod(Type type, string name) { }
        public HarmonyMethod(Type type, string name, params Type[] argumentTypes) { }
        public Priority priority;
        public string[] after;
        public string[] before;
    }

    public class Harmony
    {
        public Harmony(string id) { }
        public void Patch(
            MethodBase original,
            HarmonyMethod prefix = null,
            HarmonyMethod postfix = null,
            HarmonyMethod transpiler = null,
            HarmonyMethod finalizer = null,
            HarmonyMethod ilmanipulator = null) { }
        public void PatchAll() { }
        public void UnpatchSelf() { }
        public void UnpatchAll(string harmonyID = null) { }
        public static IEnumerable<MethodBase> GetAllPatchedMethods() => null;
        public static Patches GetPatchInfo(MethodBase method) => null;
    }

    public class Patch
    {
        public string owner;
        public int priority;
    }

    public class Patches
    {
        public IList<Patch> Prefixes;
        public IList<Patch> Postfixes;
        public IList<Patch> Transpilers;
        public IList<Patch> Finalizers;
    }

    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name) => null;
        public static MethodInfo Method(Type type, string name, Type[] parameters) => null;
        public static MethodInfo Method(Type type, string name, Type[] parameters, Type[] generics) => null;
        public static FieldInfo Field(Type type, string name) => null;
        public static PropertyInfo Property(Type type, string name) => null;
        public static Type TypeByName(string name) => null;
    }
}

// ---------------------------------------------------------------------------------------------
// BepInEx
// ---------------------------------------------------------------------------------------------
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public class BepInPlugin : Attribute
    {
        public BepInPlugin(string GUID, string name, string version) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BepInDependency : Attribute
    {
        [Flags]
        public enum DependencyFlags
        {
            HardDependency = 1,
            SoftDependency = 2
        }

        public BepInDependency(string dependencyGUID) { }
        public BepInDependency(string dependencyGUID, DependencyFlags flags) { }
    }

    public static class Paths
    {
        public static string ConfigPath = string.Empty;
        public static string PluginPath = string.Empty;
        public static string BepInExRootPath = string.Empty;
    }
}

namespace BepInEx.Configuration
{
    public class ConfigDefinition
    {
        public string Section;
        public string Key;
    }

    public class ConfigEntryBase
    {
        public object BoxedValue { get; set; }
        public ConfigDefinition Definition { get; set; }
        public bool IsDefaultValue() => false;
    }

    public class ConfigEntry<T> : ConfigEntryBase
    {
        public T Value { get; set; }
        public event EventHandler SettingChanged;
        public void Raise() => SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    public class ConfigDescription
    {
        public ConfigDescription(string description, params object[] acceptableValues) { }
        public ConfigDescription(string description) { }
    }

    public class AcceptableValueRange<T> : IEquatable<AcceptableValueRange<T>> where T : IComparable
    {
        public AcceptableValueRange(T minValue, T maxValue) { }
        public T MinValue { get; set; }
        public T MaxValue { get; set; }
        public bool Equals(AcceptableValueRange<T> other) => false;
    }

    public class ConfigFile
    {
        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description) => null;
        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription configDescription) => null;
        public void Save() { }
        public void Reload() { }
        public bool TryGetEntry<T>(string section, string key, out ConfigEntry<T> entry) { entry = null; return false; }
    }
}

namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public void LogInfo(object data) { }
        public void LogWarning(object data) { }
        public void LogError(object data) { }
        public void LogDebug(object data) { }
    }

    public interface ILogSource
    {
        void Log(LogLevel level, object data);
    }

    public enum LogLevel
    {
        None,
        Fatal,
        Error,
        Warning,
        Message,
        Info,
        Debug
    }
}

namespace BepInEx.Bootstrap
{
    public class PluginInfo
    {
        public string Metadata;
        public BaseUnityPlugin Instance;
    }

    public static class Chainloader
    {
        public static readonly Dictionary<string, PluginInfo> PluginInfos = new Dictionary<string, PluginInfo>();
    }
}

namespace BepInEx
{
    public abstract class BaseUnityPlugin : UnityEngine.MonoBehaviour
    {
        public BepInEx.Logging.ManualLogSource Logger => null;
        public BepInEx.Configuration.ConfigFile Config => null;
    }
}
