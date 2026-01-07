using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;

namespace PartialItemOverride
{
    /// <summary>
    /// Core system for partial item overrides using XPath-like syntax.
    /// This class is standalone and can be extracted into a separate mod.
    /// </summary>
    public static class PartialItemOverrideSystem
    {
        private static bool _isInitialized = false;
        private static Harmony _harmonyInstance;

        /// <summary>
        /// Initialize the partial override system. Call this from your mod's entry point.
        /// This ensures the static constructor has run and patches are applied.
        /// </summary>
        public static void Initialize(string harmonyId)
        {
            if (_isInitialized)
            {
                DebugConsole.Log("[PartialOverride] System already initialized.");
                return;
            }

            // Accessing ItemPrefabPatches will trigger its static constructor if not already run
            var patchStatus = ItemPrefabPatches._patchesApplied;
            
            _isInitialized = true;
            DebugConsole.NewMessage($"[PartialOverride] System initialized! Patches applied: {patchStatus}", Color.Green);
        }

        /// <summary>
        /// Process a ContentXElement to apply partial overrides if inherit="true"
        /// </summary>
        public static ContentXElement ProcessPartialOverride(ContentXElement element, Identifier itemIdentifier)
        {
            // Check if this element uses partial override
            bool shouldInherit = element.GetAttributeBool("inherit", false);
            if (!shouldInherit)
            {
                return element;
            }

            DebugConsole.NewMessage($"[PartialOverride] ========== PROCESSING PARTIAL OVERRIDE ==========", Color.Cyan);
            DebugConsole.Log($"[PartialOverride] Item identifier: {itemIdentifier}");
            DebugConsole.Log($"[PartialOverride] Package: {element.ContentPackage?.Name ?? "NULL"}");

            try
            {
                // Try to get the base item
                DebugConsole.Log($"[PartialOverride] Looking for base item '{itemIdentifier}' in ItemPrefab.Prefabs...");
                
                if (!ItemPrefab.Prefabs.TryGet(itemIdentifier, out var baseItemPrefab))
                {
                    DebugConsole.ThrowError($"[PartialOverride] ❌ Cannot find base item '{itemIdentifier}' to inherit from!");
                    DebugConsole.AddWarning($"[PartialOverride] Available items count: {ItemPrefab.Prefabs.Count()}");
                    
                    // List some available items for debugging
                    var availableItems = ItemPrefab.Prefabs.Take(10).Select(p => p.Identifier.ToString());
                    DebugConsole.AddWarning($"[PartialOverride] First 10 items: {string.Join(", ", availableItems)}");
                    
                    return element;
                }

                DebugConsole.NewMessage($"[PartialOverride] ✅ Found base item: {baseItemPrefab.Name} (from {baseItemPrefab.ContentFile?.ContentPackage?.Name ?? "Unknown"})", Color.Green);

                // Clone the base item's XML structure
                XElement mergedXml = new XElement(baseItemPrefab.ConfigElement);
                
                DebugConsole.Log($"[PartialOverride] Base XML root: <{mergedXml.Name}>");
                DebugConsole.Log($"[PartialOverride] Base XML children count: {mergedXml.Elements().Count()}");
                
                // List child elements
                var childNames = mergedXml.Elements().Select(e => e.Name.LocalName).Take(10);
                DebugConsole.Log($"[PartialOverride] Base XML children: {string.Join(", ", childNames)}");
                
                // Apply partial override operations
                ApplyPartialOverrides(mergedXml, element);

                // Convert back to ContentXElement with the override mod's content package
                var result = mergedXml.FromPackage(element.ContentPackage);
                
                DebugConsole.NewMessage($"[PartialOverride] ========== PROCESSING COMPLETE ==========", Color.Cyan);
                
                return result;
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[PartialOverride] ❌ Error processing partial override for '{itemIdentifier}': {ex.Message}\n{ex.StackTrace}");
                return element;
            }
        }

        /// <summary>
        /// Apply <add>, <del>, and <modify> operations to the base XML
        /// </summary>
        private static void ApplyPartialOverrides(XElement baseXml, ContentXElement overrideXml)
        {
            int operationsApplied = 0;

            foreach (var operation in overrideXml.Elements())
            {
                string opName = operation.Name.ToString().ToLowerInvariant();

                try
                {
                    switch (opName)
                    {
                        case "add":
                            HandleAddOperation(baseXml, operation);
                            operationsApplied++;
                            break;

                        case "del":
                        case "delete":
                        case "remove":
                            HandleDeleteOperation(baseXml, operation);
                            operationsApplied++;
                            break;

                        case "modify":
                        case "change":
                        case "set":
                            HandleModifyOperation(baseXml, operation);
                            operationsApplied++;
                            break;

                        default:
                            // Not an operation tag, might be a regular element to add
                            // (for backward compatibility with standard override)
                            break;
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.ThrowError($"[PartialOverride] Error applying '{opName}' operation: {ex.Message}");
                }
            }

            DebugConsole.Log($"[PartialOverride] Applied {operationsApplied} operations successfully.");
        }

        #region Operation Handlers

        /// <summary>
        /// Handle <add tag="/path/to/element">children to add</add>
        /// </summary>
        private static void HandleAddOperation(XElement baseXml, XElement addOperation)
        {
            string path = addOperation.GetAttributeString("tag", "");
            if (string.IsNullOrEmpty(path))
            {
                DebugConsole.ThrowError("[PartialOverride] <add> operation missing 'tag' attribute!");
                return;
            }

            XElement targetElement = XPathParser.FindElement(baseXml, path);
            if (targetElement == null)
            {
                DebugConsole.ThrowError($"[PartialOverride] Could not find target element for path: {path}");
                return;
            }

            // Add all child elements from the <add> tag
            foreach (var childToAdd in addOperation.Elements())
            {
                targetElement.Add(new XElement(childToAdd));
                DebugConsole.Log($"[PartialOverride] Added element '{childToAdd.Name}' to '{path}'");
            }
        }

        /// <summary>
        /// Handle <del tag="/path/to/element/to/delete" />
        /// </summary>
        private static void HandleDeleteOperation(XElement baseXml, XElement delOperation)
        {
            string path = delOperation.GetAttributeString("tag", "");
            if (string.IsNullOrEmpty(path))
            {
                DebugConsole.ThrowError("[PartialOverride] <del> operation missing 'tag' attribute!");
                return;
            }

            XElement targetElement = XPathParser.FindElement(baseXml, path);
            if (targetElement == null)
            {
                DebugConsole.AddWarning($"[PartialOverride] Could not find element to delete: {path}");
                return;
            }

            string elementInfo = $"{targetElement.Name}";
            targetElement.Remove();
            DebugConsole.Log($"[PartialOverride] Deleted element '{elementInfo}' at '{path}'");
        }

        /// <summary>
        /// Handle <modify tag="/path/to/element"><update property="attr" value="newValue" /></modify>
        /// Supports multiple <update> operations on the same element.
        /// </summary>
        private static void HandleModifyOperation(XElement baseXml, XElement modifyOperation)
        {
            string path = modifyOperation.GetAttributeString("tag", "");

            if (string.IsNullOrEmpty(path))
            {
                DebugConsole.ThrowError("[PartialOverride] <modify> operation missing 'tag' attribute!");
                return;
            }

            XElement targetElement = XPathParser.FindElement(baseXml, path);
            if (targetElement == null)
            {
                DebugConsole.ThrowError($"[PartialOverride] Could not find target element for path: {path}");
                return;
            }

            // Process all <update> child elements
            int updatesApplied = 0;
            foreach (var updateElement in modifyOperation.Elements())
            {
                if (!updateElement.Name.ToString().Equals("update", StringComparison.OrdinalIgnoreCase))
                {
                    DebugConsole.AddWarning($"[PartialOverride] Unknown child element '{updateElement.Name}' in <modify>, expected <update>");
                    continue;
                }

                string propertyName = updateElement.GetAttributeString("property", "");
                string newValue = updateElement.GetAttributeString("value", "");

                if (string.IsNullOrEmpty(propertyName))
                {
                    DebugConsole.ThrowError("[PartialOverride] <update> missing 'property' attribute!");
                    continue;
                }

                // Set or update the attribute
                var attribute = targetElement.Attribute(propertyName);
                if (attribute != null)
                {
                    string oldValue = attribute.Value;
                    attribute.Value = newValue;
                    DebugConsole.Log($"[PartialOverride] Modified '{path}#{propertyName}': '{oldValue}' → '{newValue}'");
                }
                else
                {
                    targetElement.SetAttributeValue(propertyName, newValue);
                    DebugConsole.Log($"[PartialOverride] Added new attribute '{path}#{propertyName}' = '{newValue}'");
                }

                updatesApplied++;
            }

            if (updatesApplied == 0)
            {
                DebugConsole.AddWarning($"[PartialOverride] <modify> operation for '{path}' had no <update> children!");
            }
        }

        #endregion
    }
}
