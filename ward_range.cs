using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Quartermaster_Integrations
{
    /// <summary>
    /// "A chest inside a ward is reachable from anywhere inside that ward."
    ///
    /// Quartermaster measures every chest against one radius around the player (CraftRange), so a
    /// store at the far side of a warded base is invisible even though the ward itself says the
    /// player belongs there. When the player is standing inside a ward bubble, this class asks
    /// Quartermaster for the containers within the *ward's* radius around the *ward's* centre
    /// instead, which is the same set Quartermaster would offer if the player were standing next to
    /// each chest.
    ///
    /// It widens range only. Every candidate still has to pass Quartermaster's per-chest Crafting
    /// Supply flag, its in-use check, and ward permissions (vanilla and WardIsLove), so this can
    /// never reach a chest the player is not entitled to use.
    /// </summary>
    public static class WardRange
    {
        public static bool IsReady { get; private set; }
        public static string BindError { get; private set; }

        private static FieldInfo _allAreas;
        private static MethodInfo _isEnabled;
        private static FieldInfo _radius;
        private static Func<Vector3, float, bool, bool, string, List<Container>> _nearby;
        private static Func<Container, string, int, bool, int> _availableInContainer;
        private static Func<Container, bool> _craftingSupply;

        private static readonly List<Container> Cached = new List<Container>();
        private static readonly List<Container> Empty = new List<Container>();
        private static int _cacheFrame = -1;
        private static float _lastLogTime;

        /// <summary>True only when the toggle is on and every binding resolved.</summary>
        public static bool Active =>
            IsReady &&
            BridgePlugin.Enabled &&
            BridgePlugin.WardRangeOverride != null &&
            BridgePlugin.WardRangeOverride.Value;

        public static void Initialize()
        {
            if (!BridgePlugin.Enabled) return;

            try
            {
                Assembly qm = BridgePlugin.QuartermasterAssembly;
                Type registry = qm?.GetType("Quartermaster.ContainerRegistry");
                Type transfers = qm?.GetType("Quartermaster.InventoryTransfers");
                Type area = typeof(PrivateArea);

                MethodInfo nearby = AccessTools.Method(registry, "Nearby", new[]
                {
                    typeof(Vector3), typeof(float), typeof(bool), typeof(bool), typeof(string)
                });
                MethodInfo getSettings = AccessTools.Method(registry, "GetSettings", new[] { typeof(Container) });
                MethodInfo available = AccessTools.Method(transfers, "AvailableInContainer", new[]
                {
                    typeof(Container), typeof(string), typeof(int), typeof(bool)
                });

                _allAreas = AccessTools.Field(area, "m_allAreas");
                _isEnabled = AccessTools.Method(area, "IsEnabled", Type.EmptyTypes);
                _radius = AccessTools.Field(area, "m_radius");

                if (nearby == null || getSettings == null || available == null ||
                    _allAreas == null || _isEnabled == null || _radius == null)
                {
                    BindError =
                        $"Nearby={nearby != null}, GetSettings={getSettings != null}, " +
                        $"AvailableInContainer={available != null}, m_allAreas={_allAreas != null}, " +
                        $"IsEnabled={_isEnabled != null}, m_radius={_radius != null}";

                    QIDebugger.LogError("[WardRange] Bindings incomplete: " + BindError);
                    return;
                }

                FieldInfo supply = getSettings.ReturnType.GetField("CraftingSupply", BindingFlags.Public | BindingFlags.Instance);
                if (supply == null)
                {
                    BindError = "ChestSettings.CraftingSupply not found";
                    QIDebugger.LogError("[WardRange] " + BindError);
                    return;
                }

                _nearby = (Func<Vector3, float, bool, bool, string, List<Container>>)Delegate.CreateDelegate(
                    typeof(Func<Vector3, float, bool, bool, string, List<Container>>), nearby);

                _availableInContainer = (Func<Container, string, int, bool, int>)Delegate.CreateDelegate(
                    typeof(Func<Container, string, int, bool, int>), available);

                // GetSettings returns an internal struct, so the supply flag is read through the
                // struct's own field rather than a typed delegate.
                _craftingSupply = container =>
                    (bool)supply.GetValue(getSettings.Invoke(null, new object[] { container }));

                IsReady = true;

                if (QIDebugger.Enabled)
                {
                    QIDebugger.LogDebug("[WardRange] Bound: ward-bubble range available (toggle is " +
                                        $"{(BridgePlugin.WardRangeOverride.Value ? "on" : "off")}).");
                }
            }
            catch (Exception ex)
            {
                QIDebugger.LogError("[WardRange] Binding failed", ex);
            }
        }

        /// <summary>
        /// Containers inside every ward the player is currently standing in, minus
        /// <paramref name="alreadyCounted"/> and anything the player may not use. Memoised per frame:
        /// Epic Loot asks for the item list and then removes from it in the same frame.
        /// </summary>
        public static List<Container> Collect(Player player, List<Container> alreadyCounted)
        {
            if (!Active || player == null) return Empty;

            int frame = Time.frameCount;
            if (_cacheFrame == frame) return Cached;

            Cached.Clear();
            _cacheFrame = frame;

            if (!(_allAreas.GetValue(null) is List<PrivateArea> areas)) return Cached;

            Vector3 playerPosition = player.transform.position;

            foreach (PrivateArea area in areas)
            {
                if (area == null) continue;

                try
                {
                    if (!(bool)_isEnabled.Invoke(area, null)) continue;

                    float radius = (float)_radius.GetValue(area);
                    if (radius <= 0f) continue;

                    Vector3 center = area.transform.position;

                    // Same test as PrivateArea.IsInside: the player has to be in this bubble.
                    if (Utils.DistanceXZ(center, playerPosition) >= radius) continue;

                    List<Container> inBubble = _nearby(center, radius, false, false, null);
                    if (inBubble == null) continue;

                    foreach (Container container in inBubble)
                    {
                        if (container == null) continue;
                        if (alreadyCounted != null && alreadyCounted.Contains(container)) continue;
                        if (Cached.Contains(container)) continue;
                        if (!Permitted(player, container)) continue;

                        try
                        {
                            if (!_craftingSupply(container)) continue;
                        }
                        catch (Exception ex)
                        {
                            QIDebugger.LogDebug("[WardRange] Crafting Supply probe failed; skipping chest", ex);
                            continue;
                        }

                        Cached.Add(container);
                    }
                }
                catch (Exception ex)
                {
                    QIDebugger.LogError("[WardRange] Failed to scan a ward bubble", ex);
                }
            }

            if (QIDebugger.Enabled && Cached.Count > 0 && Time.time - _lastLogTime > 10f)
            {
                _lastLogTime = Time.time;
                QIDebugger.LogDebug(
                    $"[WardRange] {Cached.Count} extra chest(s) reachable through the ward bubble at {playerPosition}");
            }

            return Cached;
        }

        /// <summary>How many of this item a bubble container holds, by Quartermaster's own maths.</summary>
        public static int AvailableIn(Container container, string sharedName)
        {
            if (!IsReady || container == null || string.IsNullOrEmpty(sharedName)) return 0;

            try
            {
                return _availableInContainer(container, sharedName, -1, false);
            }
            catch (Exception ex)
            {
                QIDebugger.LogError("[WardRange] AvailableInContainer failed", ex);
                return 0;
            }
        }

        private static bool Permitted(Player player, Container container)
        {
            // Vanilla wards, then WardIsLove's own permission model when that mod is driving.
            if (!PrivateArea.CheckAccess(container.transform.position, 0f, false, true)) return false;
            return WardIsLoveIntegration.IsPermitted(player, container);
        }
    }
}
