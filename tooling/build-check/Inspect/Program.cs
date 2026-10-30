// Audits every reflection binding the mod performs at runtime against real assemblies.
// Valheim/BepInEx members are proven by the compiler; this checks the *string*-addressed targets.
using System.Reflection;

internal static class Program
{
    private const string Managed = "/home/hermes/projects/valheim/quartermaster_integrations/refs/game/valheim_Data/Managed";
    private const string Core = "/home/hermes/projects/valheim/quartermaster_integrations/refs/game/BepInEx/core";
    private const string Mods = "/home/hermes/projects/valheim/quartermaster_integrations/refs/mods";
    private const string Candidates = "/home/hermes/projects/valheim/quartermaster_integrations/refs/candidates";

    private static int _fail;

    private static int Main()
    {
        string[] dirs = { Managed, Core, Mods, Candidates };
        var paths = dirs.Where(Directory.Exists)
                        .SelectMany(d => Directory.GetFiles(d, "*.dll"))
                        .GroupBy(Path.GetFileName)
                        .Select(g => g.First())
                        .ToList();

        using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths));

        // ---- Valheim targets that the mod's attribute patches address ---------------------------
        var vh = mlc.LoadFromAssemblyPath(Path.Combine(Managed, "assembly_valheim.dll"));
        Method(vh, "InventoryGui", "SetupRequirement", new[] { "Transform", "Requirement", "Player", "Boolean", "Int32", "Int32" }, "Boolean", true);
        Method(vh, "InventoryGui", "UpdateRecipe", new[] { "Player", "Single" }, "Void", false);
        Method(vh, "InventoryGui", "Update", Array.Empty<string>(), "Void", false);
        Method(vh, "Inventory", "Changed", new[] { "Boolean", "Boolean" }, "Void", false);
        Method(vh, "Container", "Load", Array.Empty<string>(), "Boolean", false);
        Method(vh, "Container", "TakeAll", new[] { "Humanoid" }, "Boolean", false);
        Method(vh, "Player", "HaveRequirements", new[] { "Recipe", "Boolean", "Int32", "Int32" }, "Boolean", false);

        // Private InventoryGui state we address by name - a rename here fails silently (no patch
        // error, just a recipe that can no longer be resolved), so it has to be audited explicitly.
        Field(vh, "InventoryGui", "m_selectedRecipe", "RecipeDataPair", false);
        Field(vh, "InventoryGui", "m_craftTimer", "Single", false);
        Field(vh, "InventoryGui", "m_currentContainer", "Container", false);
        Field(vh, "InventoryGui", "m_craftButton", "Button", false);

        // ---- ExtraSlots 1.2.21 ---------------------------------------------------------------
        var es = Load(mlc, Path.Combine(Mods, "ExtraSlots.dll"));
        Method(es, "ExtraSlots.Slots", "IsGridPositionASlot", new[] { "Vector2i" }, "Boolean", true);
        Method(es, "ExtraSlots.Slots", "IsItemInSlot", new[] { "ItemData" }, "Boolean", true);
        Property(es, "ExtraSlots.Slots", "InventoryHeightPlayer", "Int32", true);

        // ---- MyLittleUI 1.2.32 ----------------------------------------------------------------
        var mlui = Load(mlc, Path.Combine(Mods, "MyLittleUI.dll"));
        Field(mlui, "MyLittleUI.MultiCraft", "amount", "Int32", true);

        // ---- AdventureBackpacks 2.4.6 ---------------------------------------------------------
        var ab = Load(mlc, Path.Combine(Mods, "AdventureBackpacks.dll"));
        Method(ab, "AdventureBackpacks.API.ABAPI", "IsBackpackOpen", Array.Empty<string>(), "Boolean", true);

        // ---- Quartermaster 0.1.65 (pack runs 0.1.66; nearest published build) -------------------
        var qm = Load(mlc, Path.Combine(Mods, "Quartermaster.dll"));
        Method(qm, "Quartermaster.WarehouseService", "CountAvailableNearPlayer", new[] { "String", "Int32", "Boolean" }, "Int32", true);
        Method(qm, "Quartermaster.WarehouseService", "SortInventory", new[] { "Inventory", "Boolean", "Boolean" }, "Boolean", true);
        Method(qm, "Quartermaster.WarehouseService", "StoreAllInOpenChest", new[] { "Player", "Container" }, "Int32", true);
        Method(qm, "Quartermaster.ExternalSlotCompatibility", "IsProtectedPlayerSlot", new[] { "Int32", "Int32" }, "Boolean", true);
        Method(qm, "Quartermaster.ExternalSlotCompatibility", "IsProtectedPlayerSlot", new[] { "ItemData" }, "Boolean", true);

        // ---- AAA Crafting 2.1.8 (pack runs 2.1.12; nearest published build) --------------------
        var aaa = Load(mlc, Path.Combine(Mods, "AzuAntiArthriticCrafting.dll"));
        Field(aaa, "AzuAntiArthriticCrafting.AzuAntiArthriticCraftingPlugin", "CfcLoaded", "Boolean", true);
        Field(aaa, "AzuAntiArthriticCrafting.MaxCraftAmount", "InventoryChanged", "Boolean", true);
        Field(aaa, "AzuAntiArthriticCrafting.Patches.AAACraft", "amount", "Int32", true);
        Field(aaa, "AzuAntiArthriticCrafting.Patches.AAACraft", "inputAmount", "TMP_InputField", true);
        Field(aaa, "AzuAntiArthriticCrafting.Patches.AAACraft", "btnMinus", "Button", true);
        Field(aaa, "AzuAntiArthriticCrafting.Patches.AAACraft", "btnPlus", "Button", true);
        Method(aaa, "AzuAntiArthriticCrafting.Patches.AAACraft", "CalculateMaxCraftAmount", Array.Empty<string>(), "Int32", true);
        Method(aaa, "AzuAntiArthriticCrafting.Utilities.Utilities", "CalculateMaxCraftAmount", Array.Empty<string>(), "Int32", true);
        Method(aaa, "AzuAntiArthriticCrafting.Utilities.Utilities", "GetItemsInCloseContainers", new[] { "String" }, "Int32", true);
        Method(aaa, "AzuAntiArthriticCrafting.Patches.RecipeListPerfCache", "Invalidate", Array.Empty<string>(), "Void", true);

        // ---- AAABuildMenu 1.0.6 ----------------------------------------------------------------
        var abm = Load(mlc, Path.Combine(Candidates, "AAABuildMenu.dll"));
        Method(abm, "AAABuildMenu.PinnedRecipeTracker", "CountItem", new[] { "Inventory", "String" }, "Int32", true);

        Console.WriteLine("\n### versions audited");
        Console.WriteLine("  Quartermaster 0.1.66, AAA Crafting 2.1.12, MyLittleUI 1.2.33,");
        Console.WriteLine("  ExtraSlots 1.2.21, AdventureBackpacks 2.4.6  (exact deployed builds, from Hexium)");
        Console.WriteLine("  AAABuildMenu 1.0.6  (Hexium only - not on Thunderstore)");

        Console.WriteLine("\n### built mod assembly");
        string modDll = "/home/hermes/projects/valheim/quartermaster_integrations/bin/Release/net48/QuarterMaster_Integrations.dll";
        if (File.Exists(modDll))
        {
            var modAsm = mlc.LoadFromAssemblyPath(modDll);
            var v = modAsm.GetName().Version;
            Console.WriteLine($"  {modAsm.GetName().Name} {v}  (toString(3) = {v?.ToString(3)})");
        }
        else
        {
            Console.WriteLine("  not built yet");
        }

        Console.WriteLine(_fail == 0 ? "\nALL AUDITED BINDINGS OK" : $"\n{_fail} BINDING(S) FAILED");
        return _fail == 0 ? 0 : 1;
    }

    private static Assembly Load(MetadataLoadContext mlc, string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"### MISSING ASSEMBLY {path}");
            _fail++;
            return null;
        }

        try
        {
            return mlc.LoadFromAssemblyPath(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"### could not load {Path.GetFileName(path)}: {ex.Message}");
            _fail++;
            return null;
        }
    }

    private static void Method(Assembly asm, string typeName, string method, string[] paramTypeNames, string returnType, bool expectStatic)
    {
        if (asm == null) return;

        Type t = asm.GetType(typeName);
        if (t == null)
        {
            Report(false, $"{typeName}.{method}", "type not found");
            return;
        }

        MethodInfo m = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(x => x.Name == method &&
                                             x.GetParameters().Length == paramTypeNames.Length &&
                                             x.ReturnType.Name == returnType &&
                                             x.IsStatic == expectStatic);

        if (m == null)
        {
            var near = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(x => x.Name == method)
                        .Select(x => $"{(x.IsStatic ? "static " : "")}({string.Join(",", x.GetParameters().Select(p => p.ParameterType.Name))}):{x.ReturnType.Name}");
            Report(false, $"{typeName}.{method}", "no matching overload; found: " + string.Join(" | ", near));
            return;
        }

        Report(true, $"{typeName}.{method}({string.Join(",", paramTypeNames)}):{returnType}{(expectStatic ? " [static]" : "")}", null);
    }

    private static void Field(Assembly asm, string typeName, string field, string fieldType, bool expectStatic)
    {
        if (asm == null) return;

        Type t = asm.GetType(typeName);
        FieldInfo f = t?.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                       .FirstOrDefault(x => x.Name == field && x.FieldType.Name == fieldType && x.IsStatic == expectStatic);

        if (f == null)
        {
            string actual = t == null
                ? "type not found"
                : string.Join(" | ", t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                      .Where(x => x.Name == field)
                                      .Select(x => $"{x.FieldType.Name} {(x.IsStatic ? "static" : "instance")}"));
            Report(false, $"{typeName}.{field}:{fieldType}", string.IsNullOrEmpty(actual) ? "field not found" : "found: " + actual);
            return;
        }

        Report(true, $"{typeName}.{field}:{fieldType} [{(expectStatic ? "static" : "instance")}]", null);
    }

    private static void Property(Assembly asm, string typeName, string prop, string propType, bool expectStatic)
    {
        if (asm == null) return;

        Type t = asm.GetType(typeName);
        MethodInfo g = t?.GetProperty(prop, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);

        if (g == null || g.ReturnType.Name != propType || g.IsStatic != expectStatic)
        {
            Report(false, $"{typeName}.{prop}:{propType}", g == null
                ? "property/getter not found"
                : $"getter returns {g.ReturnType.Name} {(g.IsStatic ? "static" : "instance")}");
            return;
        }

        Report(true, $"{typeName}.{prop}:{propType} [{(expectStatic ? "static" : "instance")} getter]", null);
    }

    private static void Report(bool ok, string what, string detail)
    {
        if (!ok) _fail++;
        Console.WriteLine(ok ? $"  OK    {what}" : $"  FAIL  {what}  ->  {detail}");
    }
}
