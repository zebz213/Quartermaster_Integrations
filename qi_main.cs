using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Quartermaster_Integrations
{
    public enum RequirementDisplayMode
    {
        CombinedTotal,
        SplitInventoryAndStorage,
        TotalWithSplit
    }

    /// <summary>
    /// Applies a single Harmony patch and reports failures individually. Patching methods that live
    /// in another mod means guessing at private signatures; without a per-patch guard one mismatch
    /// silently aborts every remaining patch in the same Initialize call.
    /// </summary>
    internal static class HarmonyPatchGuard
    {
        internal static bool TryPatch(
            Harmony harmony,
            MethodBase target,
            string label,
            HarmonyMethod prefix = null,
            HarmonyMethod postfix = null,
            HarmonyMethod finalizer = null)
        {
            if (harmony == null) return false;

            if (target == null)
            {
                QIDebugger.LogError($"[Patch] '{label}' skipped: target method not found.");
                return false;
            }

            try
            {
                // The 5-argument overload is [Obsolete(..., error: true)] in HarmonyX 2.x.
                harmony.Patch(target, prefix, postfix, null, finalizer, null);
                if (QIDebugger.Enabled) QIDebugger.LogDebug($"[Patch] '{label}' applied.");
                return true;
            }
            catch (Exception ex)
            {
                QIDebugger.LogError($"[Patch] '{label}' failed to apply.", ex);
                return false;
            }
        }
    }

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(QuartermasterGUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(MLUIGUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AAACraftingGUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(ExtraSlotsGUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AdventureBackpacksGUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AAABuildMenuGUID, BepInDependency.DependencyFlags.SoftDependency)]
    public class BridgePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.bootstrapboys.quartermaster_integrations";
        public const string PluginName = "Quartermaster Integrations";

        // Keep in sync with <Version> in QuarterMaster_Integrations.csproj, resources/manifest.json
        // and the top entry of resources/CHANGELOG.md. Awake() warns if the built assembly disagrees.
        //
        // Versioning: bug fixes bump the patch digit (2.4.2 -> 2.4.3); adding an integration bumps the
        // minor and resets the patch (2.4.3 -> 2.5.0). Every bump gets a changelog entry.
        public const string PluginVersion = "2.5.0";

        public const string QuartermasterGUID = "local.valheim.quartermaster";
        public const string MLUIGUID = "shudnal.MyLittleUI";
        public const string AAACraftingGUID = "Azumatt.AzuAntiArthriticCrafting";
        public const string ExtraSlotsGUID = "shudnal.ExtraSlots";
        public const string AdventureBackpacksGUID = "vapok.mods.adventurebackpacks";
        public const string AAABuildMenuGUID = "Azumatt.AAABuildMenu";

        private readonly Harmony _harmony = new Harmony(PluginGUID);
        internal static ManualLogSource Log;

        public static ConfigEntry<bool> ModEnabled;
        public static ConfigEntry<RequirementDisplayMode> DisplayMode;
        public static ConfigEntry<bool> DeferToOtherFormatting;
        public static ConfigEntry<Color> SufficientResourceColor;
        public static ConfigEntry<Color> InsufficientFlashBrightColor;
        public static ConfigEntry<Color> InsufficientFlashDarkColor;
        public static ConfigEntry<float> FlashSpeed;
        public static ConfigEntry<bool> DebugMode;

        public static string SufficientColorHex = "ADE8E6FF";

        // Quartermaster's own nearby-storage query. Binding it means our counts are computed by the
        // same rules that decide what can actually be spent at craft time: CraftRange, the per-chest
        // Crafting Supply flag, the global CraftFromContainers toggle, ward/in-use checks and
        // world-level matching. Rolling our own container scan produced numbers Quartermaster would
        // not honour.
        private const int AnyQuality = -1;
        private const bool MatchWorldLevel = false;
        private static Func<string, int, bool, int> _countAvailableNearPlayer;

        // Per-frame unified caches for zero-GC lookups
        private static int _cachedFrame = -1;
        private static bool _cacheDirty;
        private static readonly Dictionary<string, int> CachedStorageCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> CachedPocketCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        private void Awake()
        {
            Log = Logger;

            ModEnabled = Config.Bind(
                "1. General",
                "Enabled",
                true,
                "Enable or disable the Quartermaster Integrations mod.");

            DisplayMode = Config.Bind(
                "1. General",
                "RequirementDisplayMode",
                RequirementDisplayMode.CombinedTotal,
                "Format for crafting requirement amounts:\n" +
                "CombinedTotal = 10 (16) [Req (Total)]\n" +
                "SplitInventoryAndStorage = 10 (4+12) [Req (Inv+QM)]\n" +
                "TotalWithSplit = 10 (16 [4+12])");

            DeferToOtherFormatting = Config.Bind(
                "1. General",
                "DeferToOtherFormatting",
                false,
                "Leave the requirement amount text alone when another mod (AAA Crafting, AdventureBackpacks) " +
                "has already formatted it. Vanilla's own text is a bare number, so this only triggers when " +
                "something else has decorated the row. Enable it if you prefer their 'have/need' display.");

            SufficientResourceColor = Config.Bind(
                "2. Visuals",
                "SufficientColor",
                new Color(0.68f, 0.91f, 0.90f, 1f),
                "Text color for available resource counts when you have enough components.");

            InsufficientFlashBrightColor = Config.Bind(
                "2. Visuals",
                "InsufficientFlashBrightColor",
                new Color(1f, 0.22f, 0.22f, 1f),
                "Primary bright red color when flashing insufficient crafting requirements.");

            InsufficientFlashDarkColor = Config.Bind(
                "2. Visuals",
                "InsufficientFlashDarkColor",
                new Color(0.45f, 0.02f, 0.02f, 1f),
                "Secondary dark red color when flashing insufficient crafting requirements.");

            FlashSpeed = Config.Bind(
                "2. Visuals",
                "FlashSpeed",
                4.0f,
                new ConfigDescription("Flashing speed when lacking components to craft.", new AcceptableValueRange<float>(1f, 12f)));

            DebugMode = Config.Bind(
                "3. Debug",
                "DebugMode",
                false,
                "Enable detailed debug logging to file and console.");

            UpdateCachedColors();
            SufficientResourceColor.SettingChanged += (_, __) => UpdateCachedColors();

            QIDebugger.Initialize();
            LogVersionConsistency();
            QIDebugger.LogUiSignatures();
            BindQuartermasterStorageQuery();

            // Initialize soft-dependency integrations from their dedicated files
            MLUIIntegration.Initialize();
            AAACraftingIntegration.Initialize(_harmony);
            ExtraSlotsIntegration.Initialize(_harmony);
            AdventureBackpacksIntegration.Initialize(_harmony);
            AAABuildMenuIntegration.Initialize(_harmony);

            // Soft-dependency integrations set up their own patches; container-content invalidation
            // comes from Inventory.Changed (Container.Load ends in Inventory.Load, which calls
            // Changed()), so there is no Container.Load patch to add.
            _harmony.PatchAll();

            // Logged after the integrations so readiness-derived values (BatchLimit, per-mod
            // readiness) are final; reading them mid-initialisation reported false negatives.
            QIDebugger.LogConfigSnapshot();
            QIDebugger.LogPatchInventory();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded (MLUI: {MLUIIntegration.IsReady}, AAA: {AAACraftingIntegration.IsReady}, ExtraSlots: {ExtraSlotsIntegration.IsReady}, Backpacks: {AdventureBackpacksIntegration.IsReady}, BuildMenu: {AAABuildMenuIntegration.IsReady}, QM storage query: {_countAvailableNearPlayer != null}).");
        }

        private void Start()
        {
            AAACraftingIntegration.EnableContainerCrafting();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        public static bool Enabled => ModEnabled == null || ModEnabled.Value;

        private void LogVersionConsistency()
        {
            try
            {
                // assembly_valheim declares a global-namespace `Version` type, which shadows
                // System.Version inside this namespace; qualify it explicitly.
                System.Version assemblyVersion = typeof(BridgePlugin).Assembly.GetName().Version;
                if (assemblyVersion == null) return;

                string shortVersion = assemblyVersion.ToString(3);
                if (!string.Equals(shortVersion, PluginVersion, StringComparison.Ordinal))
                {
                    Log?.LogWarning(
                        $"Assembly version ({shortVersion}) does not match PluginVersion ({PluginVersion}). " +
                        "Update <Version> in QuarterMaster_Integrations.csproj, resources/manifest.json and PluginVersion together.");
                }
            }
            catch (Exception ex)
            {
                QIDebugger.LogDebug("Version consistency check failed", ex);
            }
        }

        /// <summary>
        /// Binds Quartermaster.WarehouseService.CountAvailableNearPlayer(string, int, bool) : int, the
        /// same call Quartermaster uses to answer "how much of this is available in nearby storage".
        /// </summary>
        private void BindQuartermasterStorageQuery()
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(QuartermasterGUID, out PluginInfo qmInfo) || qmInfo?.Instance == null)
                {
                    QIDebugger.LogError("[QM] Quartermaster is not loaded; storage counts are unavailable.");
                    return;
                }

                Assembly qmAssembly = qmInfo.Instance.GetType().Assembly;
                Type warehouseType = qmAssembly.GetType("Quartermaster.WarehouseService");
                if (warehouseType == null)
                {
                    QIDebugger.LogError("[QM] Quartermaster.WarehouseService not found; storage counts are unavailable.");
                    return;
                }

                MethodInfo query = AccessTools.Method(
                    warehouseType,
                    "CountAvailableNearPlayer",
                    new[] { typeof(string), typeof(int), typeof(bool) });

                if (query == null || query.ReturnType != typeof(int))
                {
                    QIDebugger.LogError("[QM] WarehouseService.CountAvailableNearPlayer(string, int, bool) : int not found. " +
                             "Storage counts are unavailable; this mod probably needs updating for this Quartermaster version.");
                    return;
                }

                _countAvailableNearPlayer = (Func<string, int, bool, int>)Delegate.CreateDelegate(
                    typeof(Func<string, int, bool, int>), query);

                if (QIDebugger.Enabled)
                {
                    QIDebugger.LogDebug("[QM] Bound WarehouseService.CountAvailableNearPlayer.");
                }
            }
            catch (Exception ex)
            {
                QIDebugger.LogError("Failed to bind Quartermaster's nearby-storage query", ex);
            }
        }

        private static void EnsureFrameCacheUpdated(Player player)
        {
            int currentFrame = Time.frameCount;
            if (currentFrame == _cachedFrame && !_cacheDirty) return;

            _cachedFrame = currentFrame;
            _cacheDirty = false;
            CachedStorageCounts.Clear();
            CachedPocketCounts.Clear();

            if (player == null) return;

            Inventory pInv = player.GetInventory();
            if (pInv == null) return;

            List<ItemDrop.ItemData> pItems = pInv.GetAllItems();
            for (int i = 0; i < pItems.Count; i++)
            {
                ItemDrop.ItemData item = pItems[i];
                if (item?.m_shared?.m_name == null) continue;

                string name = item.m_shared.m_name;
                CachedPocketCounts.TryGetValue(name, out int current);
                CachedPocketCounts[name] = current + item.m_stack;
            }
        }

        public static int GetQuartermasterStorageCount(Player player, string itemName)
        {
            if (!Enabled) return 0;
            if (player == null || string.IsNullOrEmpty(itemName)) return 0;

            EnsureFrameCacheUpdated(player);
            return GetCachedStorageCount(itemName);
        }

        public static void GetItemCounts(Player player, string itemName, out int pocketCount, out int qmCount, out int totalAvailable)
        {
            if (player == null || string.IsNullOrEmpty(itemName))
            {
                pocketCount = 0;
                qmCount = 0;
                totalAvailable = 0;
                return;
            }

            EnsureFrameCacheUpdated(player);
            CachedPocketCounts.TryGetValue(itemName, out pocketCount);

            // Enabled is checked here rather than inside GetCachedStorageCount so a disabled mod
            // never touches Quartermaster at all.
            qmCount = Enabled ? GetCachedStorageCount(itemName) : 0;
            totalAvailable = pocketCount + qmCount;
        }

        public static int GetTotalAvailable(Player player, string itemName)
        {
            GetItemCounts(player, itemName, out _, out _, out int total);
            return total;
        }

        private static int GetCachedStorageCount(string itemName)
        {
            if (CachedStorageCounts.TryGetValue(itemName, out int cached)) return cached;

            int resolved = QueryQuartermasterStorage(itemName);
            CachedStorageCounts[itemName] = resolved;
            return resolved;
        }

        private static int QueryQuartermasterStorage(string itemName)
        {
            if (_countAvailableNearPlayer == null) return 0;

            try
            {
                return _countAvailableNearPlayer(itemName, AnyQuality, MatchWorldLevel);
            }
            catch (Exception ex)
            {
                // Fail closed: showing only pocket counts is what vanilla shows, and is better than
                // advertising storage Quartermaster will refuse to pull from.
                QIDebugger.LogDebug($"[QM] CountAvailableNearPlayer failed for '{itemName}'", ex);
                return 0;
            }
        }

        /// <summary>
        /// Marks the per-frame caches stale so the next consumer rebuilds them immediately.
        /// </summary>
        public static void MarkCacheDirty()
        {
            _cacheDirty = true;
        }

        private static void UpdateCachedColors()
        {
            SufficientColorHex = ColorUtility.ToHtmlStringRGBA(
                SufficientResourceColor != null ? SufficientResourceColor.Value : new Color(0.68f, 0.91f, 0.90f, 1f));
        }

    }

    public static class MultiCraftBridgeManager
    {
        private static int _cachedAmountFrame = -1;
        private static int _cachedAmount = 1;

        public static int GetBatchLimit() => AAACraftingIntegration.IsReady ? 100 : 99;

        public static int GetTargetCraftAmount()
        {
            if (!BridgePlugin.Enabled) return 1;

            // Called once per requirement row per frame; memoize so the reflection reads inside
            // TryGetCraftAmount run at most once per frame instead of once per row.
            int frame = Time.frameCount;
            if (frame == _cachedAmountFrame) return _cachedAmount;

            _cachedAmountFrame = frame;
            _cachedAmount = ResolveTargetCraftAmount();
            return _cachedAmount;
        }

        private static int ResolveTargetCraftAmount()
        {
            if (InventoryGui.instance != null && InventoryGui.instance.InUpradeTab())
            {
                return 1;
            }

            int batchCap = GetBatchLimit();

            if (AAACraftingIntegration.IsReady && AAACraftingIntegration.TryGetCraftAmount(out int aaaAmt))
            {
                return Mathf.Clamp(aaaAmt, 1, batchCap);
            }

            if (MLUIIntegration.IsReady && MLUIIntegration.TryGetCraftAmount(out int mluiAmt))
            {
                return Mathf.Clamp(mluiAmt, 1, batchCap);
            }

            return 1;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "SetupRequirement")]
    public static class InventoryGui_SetupRequirement_QuartermasterBridgePatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(new[] { BridgePlugin.MLUIGUID, BridgePlugin.AAACraftingGUID, BridgePlugin.QuartermasterGUID })]
        public static void Postfix(
            Transform elementRoot,
            Piece.Requirement req,
            Player player,
            bool craft,
            int quality,
            int craftMultiplier)
        {
            if (!BridgePlugin.Enabled) return;
            if (elementRoot == null || player == null || req?.m_resItem == null) return;

            int baseReqAmount = req.GetAmount(quality);
            if (baseReqAmount <= 0) return;

            TMP_Text amountText = elementRoot.Find("res_amount")?.GetComponent<TMP_Text>();
            if (amountText == null) return;

            // AAA Crafting and AdventureBackpacks both rewrite this row. Vanilla's own text is a bare
            // number; if the existing text is anything else, someone else formatted it, and
            // DeferToOtherFormatting lets their display win instead of being overwritten here.
            if (BridgePlugin.DeferToOtherFormatting != null && BridgePlugin.DeferToOtherFormatting.Value &&
                !string.IsNullOrEmpty(amountText.text) &&
                !int.TryParse(amountText.text.Trim(), out _))
            {
                return;
            }

            string itemName = req.m_resItem.m_itemData?.m_shared?.m_name;
            if (string.IsNullOrEmpty(itemName)) return;

            bool isUpgrading = InventoryGui.instance != null && InventoryGui.instance.InUpradeTab();

            // The game hands the craft amount to SetupRequirement itself (sixth parameter), so use it
            // when it carries a batch. Builds where that parameter is always 1 fall back to reading
            // AAA Crafting / My Little UI directly.
            int effectiveMultiplier = (craft && !isUpgrading) ? craftMultiplier : 1;
            if (craft && !isUpgrading && effectiveMultiplier <= 1)
            {
                effectiveMultiplier = MultiCraftBridgeManager.GetTargetCraftAmount();
            }

            int scaledRequiredAmount = baseReqAmount * effectiveMultiplier;

            BridgePlugin.GetItemCounts(player, itemName, out int pocketCount, out int qmCount, out int totalAvailable);

            bool noCost = player.NoCostCheat() ||
                          (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost));
            bool hasEnough = noCost || totalAvailable >= scaledRequiredAmount;

            if (!amountText.enableAutoSizing)
            {
                amountText.enableAutoSizing = true;
                amountText.fontSizeMin = 9f;
                amountText.fontSizeMax = 14f;
                amountText.textWrappingMode = TextWrappingModes.NoWrap;
                amountText.overflowMode = TextOverflowModes.Overflow;
            }

            string availabilitySegment = FormatAvailability(pocketCount, qmCount, totalAvailable);

            if (hasEnough)
            {
                amountText.color = Color.white;
                amountText.SetText($"{scaledRequiredAmount} <color=#{BridgePlugin.SufficientColorHex}>{availabilitySegment}</color>");
            }
            else
            {
                amountText.color = Color.white;
                amountText.SetText($"<color=#{FlashHex()}>{scaledRequiredAmount} {availabilitySegment}</color>");
            }
        }

        private static float _flashTick = float.NaN;
        private static string _flashHex = "FF3838FF";

        /// <summary>
        /// One HTML colour per animation tick rather than one per requirement row per frame. The
        /// flash advances at FlashSpeed*3 Hz, so every row drawn in the same frame shares a colour;
        /// recomputing it per row was building the same string six times a frame.
        /// </summary>
        private static string FlashHex()
        {
            float speed = BridgePlugin.FlashSpeed != null ? BridgePlugin.FlashSpeed.Value : 4f;
            float tick = Mathf.Floor(Time.unscaledTime * speed * 3f);
            if (tick == _flashTick) return _flashHex;

            _flashTick = tick;

            float t = Mathf.PingPong(tick / 3f, 1f);
            Color bright = BridgePlugin.InsufficientFlashBrightColor != null
                ? BridgePlugin.InsufficientFlashBrightColor.Value
                : new Color(1f, 0.22f, 0.22f, 1f);
            Color dark = BridgePlugin.InsufficientFlashDarkColor != null
                ? BridgePlugin.InsufficientFlashDarkColor.Value
                : new Color(0.45f, 0.02f, 0.02f, 1f);

            _flashHex = ColorUtility.ToHtmlStringRGBA(Color.Lerp(bright, dark, t));
            return _flashHex;
        }

        private static string FormatAvailability(int invCount, int qmCount, int totalAvailable)
        {
            RequirementDisplayMode mode = BridgePlugin.DisplayMode != null
                ? BridgePlugin.DisplayMode.Value
                : RequirementDisplayMode.CombinedTotal;

            switch (mode)
            {
                case RequirementDisplayMode.SplitInventoryAndStorage:
                    return $"({invCount.ToString(CultureInfo.InvariantCulture)}+{qmCount.ToString(CultureInfo.InvariantCulture)})";

                case RequirementDisplayMode.TotalWithSplit:
                    return $"({totalAvailable.ToString(CultureInfo.InvariantCulture)} [{invCount.ToString(CultureInfo.InvariantCulture)}+{qmCount.ToString(CultureInfo.InvariantCulture)}])";

                case RequirementDisplayMode.CombinedTotal:
                default:
                    return $"({totalAvailable.ToString(CultureInfo.InvariantCulture)})";
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui))]
    public static class InventoryGui_UpdateRecipe_BatchEnforcementPatch
    {
        private static readonly FieldInfo SelectedRecipeField = AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe");
        private static readonly FieldInfo CraftTimerField = AccessTools.Field(typeof(InventoryGui), "m_craftTimer");

        private static Type _resolvedPairType;
        private static PropertyInfo _recipeProp;
        private static PropertyInfo _itemDataProp;

        public static (Recipe recipe, ItemDrop.ItemData itemData) GetSelectedRecipeData(InventoryGui gui)
        {
            if (gui == null || SelectedRecipeField == null) return (null, null);

            object pairObj = SelectedRecipeField.GetValue(gui);
            if (pairObj == null) return (null, null);

            Type pairType = pairObj.GetType();
            if (_resolvedPairType != pairType || _recipeProp == null || _itemDataProp == null)
            {
                PropertyInfo recipeProp = AccessTools.Property(pairType, "Recipe");
                PropertyInfo itemDataProp = AccessTools.Property(pairType, "ItemData");

                if (recipeProp == null || itemDataProp == null)
                {
                    // Deliberately do not cache a failed lookup: latching it here would permanently
                    // disable max-craft and batch enforcement for the rest of the session.
                    if (QIDebugger.Enabled)
                    {
                        QIDebugger.LogDebug(
                            $"[GetSelectedRecipeData] Unresolved members on '{pairType.FullName}' " +
                            $"(Recipe found: {recipeProp != null}, ItemData found: {itemDataProp != null}).");
                    }
                    return (null, null);
                }

                _resolvedPairType = pairType;
                _recipeProp = recipeProp;
                _itemDataProp = itemDataProp;
            }

            Recipe r = _recipeProp.GetValue(pairObj, null) as Recipe;
            ItemDrop.ItemData item = _itemDataProp.GetValue(pairObj, null) as ItemDrop.ItemData;
            return (r, item);
        }

        public static float GetCraftTimer(InventoryGui gui)
        {
            if (gui == null || CraftTimerField == null) return -1f;
            return (float)CraftTimerField.GetValue(gui);
        }

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(InventoryGui), "UpdateRecipe", new[] { typeof(Player), typeof(float) })
                ?? AccessTools.Method(typeof(InventoryGui), "UpdateRecipe", new[] { typeof(Player) });
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(new[] { BridgePlugin.AAACraftingGUID, BridgePlugin.MLUIGUID })]
        [HarmonyPostfix]
        public static void Postfix(InventoryGui __instance, Player player)
        {
            if (!BridgePlugin.Enabled) return;
            if (__instance == null || player == null || !InventoryGui.IsVisible()) return;

            (Recipe recipe, ItemDrop.ItemData upgradeItem) = GetSelectedRecipeData(__instance);
            if (recipe == null || __instance.m_craftButton == null || GetCraftTimer(__instance) >= 0f) return;

            bool isUpgrading = __instance.InUpradeTab() || upgradeItem != null;
            bool noCost = player.NoCostCheat() || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost));

            if (QIDebugger.Enabled)
            {
                QIDebugger.LogCraftingSnapshot(
                    recipe,
                    player,
                    isUpgrading ? 1 : MultiCraftBridgeManager.GetTargetCraftAmount(),
                    isUpgrading);
            }

            if (isUpgrading)
            {
                int nextQuality = upgradeItem != null ? upgradeItem.m_quality + 1 : 2;
                bool canUpgrade = noCost || player.HaveRequirements(recipe, false, nextQuality);
                if (canUpgrade)
                {
                    __instance.m_craftButton.interactable = true;
                }
                return;
            }

            int multiplier = MultiCraftBridgeManager.GetTargetCraftAmount();

            // Never derive "can craft" from m_craftButton.interactable: this postfix writes that
            // very field, so reading it compares our previous decision against itself and the
            // button latches enabled after the resources are gone.
            //
            // Quartermaster transpiles Player.HaveRequirements so its ingredient counting goes
            // through WarehouseService, which means this single check already covers pockets,
            // qualifying chests and the CraftFromContainers toggle. Adding our own count on top
            // produced false positives for chests Quartermaster refuses to pull from.
            bool canCraftSingle = noCost || player.HaveRequirements(recipe, false, 1);

            if (canCraftSingle)
            {
                // The amount-aware overload; also Quartermaster-transpiled, so a batch is only
                // advertised as craftable when the storage rules agree.
                bool canBatch = multiplier <= 1 || player.HaveRequirements(recipe, false, 1, multiplier);
                __instance.m_craftButton.interactable = canBatch;
            }
            else
            {
                __instance.m_craftButton.interactable = false;
            }

            AAACraftingIntegration.ForceEnableBatchButtons(canCraftSingle, multiplier);
        }
    }

    [HarmonyPatch]
    public static class ContainerStateChangedWatchers
    {
        [HarmonyPatch(typeof(Inventory), "Changed")]
        [HarmonyPostfix]
        public static void InventoryChangedPostfix()
        {
            // Inventory.Changed fires for every container in the world on every item move, so
            // clearing the caches unconditionally made a distant smelter force a cache rebuild and
            // a AAA recipe-list invalidation per tick. The caches only feed the crafting UI, so
            // only mark them dirty while it is open.
            if (!InventoryGui.IsVisible()) return;

            BridgePlugin.MarkCacheDirty();
            AAACraftingIntegration.NotifyInventoryChanged();
        }
    }
}
