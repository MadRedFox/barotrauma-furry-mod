using System;
using System.Reflection;
using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

namespace PartialItemOverride
{
    /// <summary>
    /// Harmony patches for ItemFile to intercept item creation during loading.
    /// Uses a static constructor for early initialization.
    /// </summary>
    [HarmonyPatch]
    public static class ItemPrefabPatches
    {
        public static bool _patchesApplied = false;

        /// <summary>
        /// Static constructor - runs the FIRST time this class is accessed.
        /// We manually apply patches here to ensure they're applied early.
        /// </summary>
        static ItemPrefabPatches()
        {
            if (_patchesApplied) return;
            
            try
            {
                DebugConsole.NewMessage("[PartialOverride] 🔧 Static constructor running - applying patches manually...", Color.Cyan);
                
                var harmony = new Harmony("com.arcticfox.partialoverride.static");
                
                // Manually patch the CreatePrefab method
                var targetMethod = GetTargetMethod();
                if (targetMethod != null)
                {
                    var prefix = typeof(ItemPrefabPatches).GetMethod(nameof(CreatePrefab_Prefix), 
                        BindingFlags.Public | BindingFlags.Static);
                    
                    harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefix));
                    
                    DebugConsole.NewMessage("[PartialOverride] ✅ Patches applied successfully in static constructor!", Color.Green);
                    _patchesApplied = true;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[PartialOverride] ❌ Static constructor failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static MethodBase GetTargetMethod()
        {
            // Get the ItemFile type
            var itemFileType = typeof(ItemPrefab).Assembly.GetType("Barotrauma.ItemFile");
            if (itemFileType == null)
            {
                DebugConsole.ThrowError("[PartialOverride] Could not find ItemFile type!");
                return null;
            }

            // Get the CreatePrefab method (it's protected)
            var method = itemFileType.GetMethod("CreatePrefab", 
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(ContentXElement) },
                null);

            if (method == null)
            {
                DebugConsole.ThrowError("[PartialOverride] Could not find CreatePrefab method in ItemFile!");
                return null;
            }

            DebugConsole.NewMessage($"[PartialOverride] 🎯 Successfully found ItemFile.CreatePrefab!", Color.Green);
            return method;
        }

        /// <summary>
        /// Prefix patch for ItemFile.CreatePrefab.
        /// This intercepts item creation DURING the loading process.
        /// </summary>
        public static void CreatePrefab_Prefix(ref ContentXElement element, object __instance)
        {
            try
            {
                // ALWAYS log to confirm this patch is executing
                DebugConsole.Log("[PartialOverride] CreatePrefab_Prefix called!");
                
                // Extract file path from the ItemFile instance
                var itemFile = __instance as ContentFile;
                string filePath = itemFile?.Path.Value ?? "Unknown";

                // Get the item identifier for logging
                Identifier itemIdentifier = DetermineIdentifier(element);
                
                // Log EVERY item being loaded for debugging
                if (!itemIdentifier.IsEmpty)
                {
                    bool hasInherit = element.GetAttributeBool("inherit", false);
                    
                    // Always log to see what's being loaded
                    DebugConsole.Log($"[PartialOverride] Item creating: {itemIdentifier} (inherit={hasInherit}, file={filePath})");
                    
                    if (hasInherit)
                    {
                        DebugConsole.NewMessage($"[PartialOverride] 🎯 INTERCEPTED: {itemIdentifier} with inherit=true", Color.Yellow);
                    }
                }
                else
                {
                    // Log items without identifiers too
                    DebugConsole.AddWarning($"[PartialOverride] Item without identifier from {filePath}");
                }
                
                // Only process if the element has inherit="true"
                if (!element.GetAttributeBool("inherit", false))
                {
                    return;
                }

                if (itemIdentifier.IsEmpty)
                {
                    DebugConsole.AddWarning("[PartialOverride] Item with inherit=true has no identifier, skipping.");
                    return;
                }

                DebugConsole.NewMessage($"[PartialOverride] ========================================", Color.Cyan);
                DebugConsole.NewMessage($"[PartialOverride] Processing partial override: {itemIdentifier}", Color.Cyan);
                DebugConsole.NewMessage($"[PartialOverride] File: {filePath}", Color.Cyan);
                DebugConsole.NewMessage($"[PartialOverride] ========================================", Color.Cyan);

                // Process the partial override and replace the element reference
                element = PartialItemOverrideSystem.ProcessPartialOverride(element, itemIdentifier);
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[PartialOverride] ❌ Error in CreatePrefab_Prefix: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Determine the identifier of an item element (copied from ItemPrefab logic).
        /// This ensures we use the same identifier resolution as Barotrauma.
        /// </summary>
        private static Identifier DetermineIdentifier(ContentXElement element)
        {
            // Try to get identifier attribute
            Identifier identifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
            
            if (!identifier.IsEmpty)
            {
                return identifier;
            }

            // Fallback: try to get it from name (legacy support)
            string name = element.GetAttributeString("name", "");
            if (!string.IsNullOrEmpty(name))
            {
                // Check if this is a legacy item
                string categoryStr = element.GetAttributeString("category", "Misc");
                if (Enum.TryParse<MapEntityCategory>(categoryStr, true, out var category) && 
                    category.HasFlag(MapEntityCategory.Legacy))
                {
                    identifier = GenerateLegacyIdentifier(name);
                }
            }

            return identifier;
        }

        /// <summary>
        /// Generate legacy identifier from item name (copied from ItemPrefab.GenerateLegacyIdentifier).
        /// </summary>
        private static Identifier GenerateLegacyIdentifier(string name)
        {
            return ($"legacyitem_{name.Replace(" ", "")}").ToIdentifier();
        }
    }

    /// <summary>
    /// Additional patches for debugging and monitoring.
    /// </summary>
    [HarmonyPatch(typeof(ItemPrefab))]
    public static class ItemPrefabDebugPatches
    {
        /// <summary>
        /// Log when ParseConfigElement is called (useful for debugging timing issues).
        /// </summary>
        [HarmonyPatch("ParseConfigElement")]
        [HarmonyPrefix]
        public static void ParseConfigElement_Prefix(ItemPrefab __instance)
        {
            // Only log if this is an inherited item
            if (__instance.ConfigElement != null && 
                __instance.ConfigElement.GetAttributeBool("inherit", false))
            {
                DebugConsole.Log($"[PartialOverride] ParseConfigElement called for: {__instance.Identifier}");
            }
        }
    }
}
