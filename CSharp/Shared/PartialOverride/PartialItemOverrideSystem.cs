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

            try
            {
                DebugConsole.NewMessage("[PartialOverride] Processing items with inherit=\"true\"...", Color.Cyan);

                // Process all items POST-LOAD (C# runs after initial item loading)
                ProcessAllInheritItems();

                _isInitialized = true;
                DebugConsole.NewMessage("[PartialOverride] ✅ Partial override system ready!", Color.Green);
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[PartialOverride] Failed to initialize: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Scans all content packages for items with inherit="true" and processes them POST-LOAD.
        /// </summary>
        private static void ProcessAllInheritItems()
        {
            int processedCount = 0;

            // Iterate through all content packages
            foreach (var package in ContentPackageManager.EnabledPackages.All)
            {
                // Find all Item files in this package
                foreach (var file in package.Files)
                {
                    if (file is ItemFile itemFile)
                    {
                        processedCount += ProcessItemFile(itemFile, package);
                    }
                }
            }

            if (processedCount > 0)
            {
                DebugConsole.NewMessage($"[PartialOverride] Applied overrides to {processedCount} item(s)", Color.Green);
            }
        }

        /// <summary>
        /// Processes a single ItemFile, looking for items with inherit="true".
        /// </summary>
        private static int ProcessItemFile(ItemFile file, ContentPackage package)
        {
            int count = 0;

            try
            {
                // Load the XML document
                var doc = System.Xml.Linq.XDocument.Load(file.Path.Value);
                if (doc?.Root == null) return 0;

                // Find all Item elements (recursively search in case of Override wrappers)
                var itemElements = doc.Root.Descendants()
                    .Where(e => e.Name.LocalName.Equals("Item", StringComparison.OrdinalIgnoreCase));

                foreach (var itemElement in itemElements)
                {
                    // Check if this item has inherit="true"
                    var inheritAttr = itemElement.Attribute("inherit");
                    bool hasInherit = inheritAttr != null && inheritAttr.Value.Equals("true", StringComparison.OrdinalIgnoreCase);

                    if (!hasInherit)
                        continue;

                    // Get the identifier
                    var identifierAttr = itemElement.Attribute("identifier");
                    if (identifierAttr == null) continue;

                    Identifier itemId = identifierAttr.Value.ToIdentifier();

                    // Modify existing item
                    if (ItemPrefab.Prefabs.TryGet(itemId, out var existingPrefab))
                    {
                        DebugConsole.NewMessage($"[PartialOverride] 🎯 Processing item: {itemId}", Color.Yellow);
                        
                        var contentElement = new ContentXElement(package, itemElement);
                        ApplyOverrideToPrefab(existingPrefab, contentElement, itemId);
                        count++;
                    }
                    else
                    {
                        DebugConsole.AddWarning($"[PartialOverride] Item '{itemId}' not found for modification!");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[PartialOverride] Error processing file {file.Path}: {ex.Message}");
            }

            return count;
        }

        /// <summary>
        /// Applies override operations to an existing ItemPrefab by modifying its ConfigElement.
        /// </summary>
        private static void ApplyOverrideToPrefab(ItemPrefab prefab, ContentXElement overrideElement, Identifier itemId)
        {
            try
            {
                DebugConsole.Log($"[PartialOverride] Applying overrides to: {itemId}");

                // Get the base XML from the existing prefab
                var baseXml = new System.Xml.Linq.XElement(prefab.ConfigElement.Element);

                // Apply the override operations
                ApplyPartialOverrides(baseXml, overrideElement);

                // Now we need to update the prefab's ConfigElement
                // ConfigElement is a property with a private setter, we need to find its backing field
                var configElementField = typeof(ItemPrefab).GetField("<ConfigElement>k__BackingField", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                if (configElementField == null)
                {
                    // Try alternative: set via property if it has a setter
                    var configElementProperty = typeof(ItemPrefab).GetProperty("ConfigElement",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    
                    if (configElementProperty != null && configElementProperty.CanWrite)
                    {
                        var newContentElement = new ContentXElement(prefab.ConfigElement.ContentPackage, baseXml);
                        configElementProperty.SetValue(prefab, newContentElement);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"[PartialOverride] Could not update ConfigElement for {itemId}!");
                    }
                }
                else
                {
                    var newContentElement = new ContentXElement(prefab.ConfigElement.ContentPackage, baseXml);
                    configElementField.SetValue(prefab, newContentElement);
                }
                
                DebugConsole.NewMessage($"[PartialOverride] ✅ Successfully updated {itemId}", Color.Green);
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[PartialOverride] Error applying override to {itemId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a new ItemPrefab based on an existing item's configuration.
        /// This allows creating new items that inherit stats from other items.
        /// </summary>
        private static void CreateNewItemFromBase(ItemPrefab basePrefab, ContentXElement overrideElement, Identifier newItemId, Identifier baseItemId)
        {
            try
            {
                DebugConsole.Log($"[PartialOverride] Creating '{newItemId}' from base '{baseItemId}'");

                // Clone the base item's XML
                var newItemXml = new System.Xml.Linq.XElement(basePrefab.ConfigElement.Element);

                // Update the identifier to the new item's ID
                newItemXml.SetAttributeValue("identifier", newItemId.Value);

                // Remove the inherit attribute from the final XML
                newItemXml.Attribute("inherit")?.Remove();

                // Apply override operations from the new item definition
                ApplyPartialOverrides(newItemXml, overrideElement);

                // Create a new ItemPrefab and add it to the collection
                var newContentElement = new ContentXElement(overrideElement.ContentPackage, newItemXml);
                
                // Create new ItemPrefab (this will call the constructor)
                var newPrefab = new ItemPrefab(newContentElement, basePrefab.ContentFile as ItemFile);
                
                // Add to prefab collection
                ItemPrefab.Prefabs.Add(newPrefab, false);

                DebugConsole.NewMessage($"[PartialOverride] ✅ Created new item: {newItemId}", Color.Green);
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[PartialOverride] Error creating new item '{newItemId}': {ex.Message}");
            }
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
            DebugConsole.NewMessage($"[PartialOverride] Item identifier: {itemIdentifier}", Color.Cyan);
            DebugConsole.Log($"[PartialOverride] Package: {element.ContentPackage?.Name ?? "NULL"}");

            try
            {
                // Try to get the base item
                DebugConsole.NewMessage($"[PartialOverride] Looking for base item '{itemIdentifier}'...", Color.Yellow);
                
                if (!ItemPrefab.Prefabs.TryGet(itemIdentifier, out var baseItemPrefab))
                {
                    DebugConsole.ThrowError($"[PartialOverride] ❌ Cannot find base item '{itemIdentifier}' to inherit from!");
                    DebugConsole.AddWarning($"[PartialOverride] Available items count: {ItemPrefab.Prefabs.Count()}");
                    
                    // List some available items for debugging
                    var availableItems = ItemPrefab.Prefabs.Take(10).Select(p => p.Identifier.ToString());
                    DebugConsole.AddWarning($"[PartialOverride] First 10 items: {string.Join(", ", availableItems)}");
                    
                    return element;
                }

                DebugConsole.NewMessage($"[PartialOverride] ✅ Found base item!", Color.Green);
                DebugConsole.Log($"[PartialOverride] Base item name: {baseItemPrefab.Name}");
                DebugConsole.Log($"[PartialOverride] Base item from: {baseItemPrefab.ContentFile?.ContentPackage?.Name ?? "Unknown"}");

                // Clone the base item's XML structure
                XElement mergedXml = new XElement(baseItemPrefab.ConfigElement);
                
                DebugConsole.Log($"[PartialOverride] Cloned base XML: <{mergedXml.Name}>");
                DebugConsole.Log($"[PartialOverride] Base XML has {mergedXml.Elements().Count()} child elements");
                
                // Count override operations in incoming element
                int addOps = element.Elements().Count(e => e.Name.ToString().Equals("add", StringComparison.OrdinalIgnoreCase));
                int delOps = element.Elements().Count(e => e.Name.ToString().Equals("del", StringComparison.OrdinalIgnoreCase) || 
                                                            e.Name.ToString().Equals("delete", StringComparison.OrdinalIgnoreCase));
                int modOps = element.Elements().Count(e => e.Name.ToString().Equals("modify", StringComparison.OrdinalIgnoreCase));
                
                DebugConsole.NewMessage($"[PartialOverride] Override operations: {addOps} add, {delOps} delete, {modOps} modify", Color.Cyan);
                
                // Apply partial override operations
                ApplyPartialOverrides(mergedXml, element);

                // Convert back to ContentXElement with the override mod's content package
                var result = mergedXml.FromPackage(element.ContentPackage);
                
                DebugConsole.NewMessage($"[PartialOverride] ========== PROCESSING COMPLETE ==========", Color.Green);
                
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
