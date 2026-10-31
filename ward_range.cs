using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Quartermaster_Integrations
{
    /// <summary>
    /// While the player is inside a WardIsLove ward, that ward's radius becomes the crafting and
    /// storage range. WardIsLove is the host: its per-ward radius (the value its own UI edits, which
    /// can differ from the vanilla ward's) replaces Quartermaster's CraftRange for everything that
    /// range touches - the crafting window's counts, the max-craft number, and which chests a craft
    /// is allowed to pull from. Outside every ward nothing changes.
    ///
    /// Why a transpiler instead of adding counts on top: Quartermaster decides range in one place
    /// (CraftStorageAccess.Nearby) and re-checks it when validating a craft (SharedCrafting.
    /// SourcesValid). Widening the displayed number without widening those would let the UI promise
    /// materials the craft then refuses, or worse, let a craft pass with materials it cannot take.
    /// Substituting the radius keeps counting and withdrawal on the same answer.
    ///
    /// Off switch: `1. General > WardHostRange`. When off, or when WardIsLove is absent, neither
    /// patch is applied and Quartermaster behaves exactly as before.
    /// </summary>
    public static class WardHostRange
    {
        public static bool IsReady { get; private set; }
        public static string BindError { get; private set; }

        private static ConfigEntry<float> _craftRange;
        private static FieldInfo _allAreas;
        private static Type _wardType;
        private static MethodInfo _getWardRadius;

        // Radius lookup runs inside Quartermaster's per-check range read, so it is memoised per frame.
        private static int _cacheFrame = -1;
        private static float _cachedRadius;

        // Set by the transpiler so a silent no-match cannot masquerade as a working patch.
        private static int _replacements;

        public static bool Active =>
            IsReady &&
            BridgePlugin.Enabled &&
            BridgePlugin.WardHostRange != null &&
            BridgePlugin.WardHostRange.Value;

        public static void Initialize(Harmony harmony, Assembly wardIsLoveAssembly)
        {
            if (!BridgePlugin.Enabled || harmony == null || wardIsLoveAssembly == null) return;

            try
            {
                Assembly qm = BridgePlugin.QuartermasterAssembly;
                Type pluginType = qm?.GetType("Quartermaster.Plugin");
                Type transfersType = qm?.GetType("Quartermaster.CraftStorageAccess");
                Type sharedCrafting = qm?.GetType("Quartermaster.SharedCrafting");

                _craftRange = AccessTools.Field(pluginType, "CraftRange")?.GetValue(null) as ConfigEntry<float>;
                _allAreas = AccessTools.Field(typeof(PrivateArea), "m_allAreas");

                _wardType = wardIsLoveAssembly.GetType("WardIsLove.Util.WardMonoscript");
                _getWardRadius = _wardType == null
                    ? null
                    : AccessTools.Method(
                        wardIsLoveAssembly.GetType("WardIsLove.Extensions.WardMonoscriptExt"),
                        "GetWardRadius",
                        new[] { _wardType });

                MethodInfo nearby = transfersType == null
                    ? null
                    : AccessTools.Method(transfersType, "Nearby", new[] { typeof(Player) });

                MethodInfo sourcesValid = sharedCrafting == null
                    ? null
                    : AccessTools.Method(sharedCrafting, "SourcesValid", new[] { typeof(Player), typeof(Container[]) });

                if (_craftRange == null || _allAreas == null || _wardType == null || _getWardRadius == null ||
                    nearby == null || sourcesValid == null)
                {
                    BindError =
                        $"CraftRange={_craftRange != null}, m_allAreas={_allAreas != null}, " +
                        $"WardMonoscript={_wardType != null}, GetWardRadius={_getWardRadius != null}, " +
                        $"CraftStorageAccess.Nearby={nearby != null}, SourcesValid={sourcesValid != null}";

                    QIDebugger.LogError("[WardHostRange] Bindings incomplete: " + BindError);
                    return;
                }

                // Nearby is a plain method in 0.1.66 (it ends in ToArray), but if Quartermaster ever
                // makes it an iterator the body moves to MoveNext - patch whichever actually holds the
                // read. Same for SourcesValid, whose distance test lives in a compiler-generated
                // predicate rather than in the method itself.
                int counted = PatchCandidates(
                    harmony,
                    "counting",
                    nearby,
                    AccessTools.EnumeratorMoveNext(nearby));

                int validated = PatchCandidates(
                    harmony,
                    "validation",
                    sourcesValid,
                    FindPredicate(sharedCrafting, "SourcesValid"));

                IsReady = counted > 0 && validated > 0;

                if (QIDebugger.Enabled)
                {
                    QIDebugger.LogDebug(
                        $"[WardHostRange] Replaced range reads: counting={counted}, validation={validated} " +
                        $"(toggle is {(BridgePlugin.WardHostRange.Value ? "on" : "off")}).");
                }

                if (!IsReady)
                {
                    BindError = $"range reads replaced: counting={counted}, validation={validated}";
                    QIDebugger.LogError(
                        "[WardHostRange] Quartermaster's range reads moved; ward range is not applied. " + BindError);
                }
            }
            catch (Exception ex)
            {
                QIDebugger.LogError("[WardHostRange] Initialization failed", ex);
            }
        }

        /// <summary>
        /// Applies the range-read transpiler to every candidate that exists and reports how many
        /// reads were actually rewritten across them. A candidate with no read is a no-op patch,
        /// which is harmless; requiring a non-zero total is what catches a moved signature.
        /// </summary>
        private static int PatchCandidates(Harmony harmony, string label, params MethodBase[] candidates)
        {
            int total = 0;

            foreach (MethodBase candidate in candidates)
            {
                if (candidate == null) continue;

                _replacements = 0;
                bool applied = HarmonyPatchGuard.TryPatch(
                    harmony,
                    candidate,
                    $"{label}:{candidate.DeclaringType?.Name}.{candidate.Name}",
                    transpiler: new HarmonyMethod(typeof(WardHostRange), nameof(ReplaceRangeRead)));

                if (applied) total += _replacements;
            }

            return total;
        }

        /// <summary>
        /// Finds the compiler-generated predicate a method delegates to (for example
        /// '&lt;SourcesValid&gt;b__0' inside a display class), which is where an inline range test
        /// actually lives once a lambda captures the player.
        /// </summary>
        private static MethodInfo FindPredicate(Type type, string owner)
        {
            if (type == null) return null;

            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!nested.Name.Contains("<" + owner + ">")) continue;

                foreach (MethodInfo method in nested.GetMethods(
                             BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name.Contains("b__")) return method;
                }
            }

            return null;
        }

        /// <summary>
        /// Swaps every `ConfigEntry&lt;float&gt;.Value` read in the target for a call to
        /// <see cref="EffectiveRange"/>. The instance is popped because the replaced callvirt
        /// consumed it and pushed the float.
        /// </summary>
        private static IEnumerable<CodeInstruction> ReplaceRangeRead(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(ConfigEntry<float>), "Value");
            MethodInfo replacement = AccessTools.Method(typeof(WardHostRange), nameof(EffectiveRange));

            if (getter == null || replacement == null)
            {
                foreach (CodeInstruction instruction in instructions) yield return instruction;
                yield break;
            }

            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call) &&
                    Equals(instruction.operand, getter))
                {
                    _replacements++;

                    // Keep any branch labels attached to the instruction being replaced.
                    yield return new CodeInstruction(OpCodes.Pop) { labels = instruction.labels };
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                    continue;
                }

                yield return instruction;
            }
        }

        /// <summary>
        /// The radius Quartermaster should use right now: the ward's own radius while the player
        /// stands in one, otherwise Quartermaster's configured CraftRange.
        /// </summary>
        public static float EffectiveRange()
        {
            float craftRange = _craftRange != null ? _craftRange.Value : 100f;
            if (!Active) return craftRange;

            float wardRadius = CurrentWardRadius();
            return wardRadius > 0f ? wardRadius : craftRange;
        }

        /// <summary>
        /// Largest WardIsLove ward radius containing the player, or 0 when they are in none.
        /// </summary>
        private static float CurrentWardRadius()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return 0f;

            int frame = Time.frameCount;
            if (frame == _cacheFrame) return _cachedRadius;

            _cacheFrame = frame;
            _cachedRadius = 0f;

            if (!(_allAreas.GetValue(null) is List<PrivateArea> areas)) return 0f;

            Vector3 playerPosition = player.transform.position;

            foreach (PrivateArea area in areas)
            {
                if (area == null) continue;

                try
                {
                    // Only wards WardIsLove is actually hosting count; a plain vanilla ward keeps
                    // Quartermaster's own radius.
                    Component ward = area.GetComponent(_wardType);
                    if (ward == null) continue;

                    float radius = (float)_getWardRadius.Invoke(null, new object[] { ward });
                    if (radius <= 0f) continue;

                    // Inside test in XZ, matching how the ward itself measures its bubble.
                    if (Utils.DistanceXZ(area.transform.position, playerPosition) > radius) continue;

                    if (radius > _cachedRadius) _cachedRadius = radius;
                }
                catch (Exception ex)
                {
                    QIDebugger.LogError("[WardHostRange] Failed to read a ward radius", ex);
                }
            }

            return _cachedRadius;
        }
    }
}
