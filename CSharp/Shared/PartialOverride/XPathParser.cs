using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Barotrauma;
using Microsoft.Xna.Framework;

namespace PartialItemOverride
{
    /// <summary>
    /// Parser for XPath-like syntax used in partial overrides.
    /// Supports: /Element/SubElement[attribute=value]/DeepElement#attributeName
    /// </summary>
    public static class XPathParser
    {
        /// <summary>
        /// Find an element in the XML tree using XPath-like syntax.
        /// Example: "/Wearable/sprite[name='Combat Armor']/conditionalSprite"
        /// </summary>
        public static XElement FindElement(XElement root, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return root;
            }

            DebugConsole.Log($"[XPathParser] Finding element with path: {path}");

            // Remove leading slash
            if (path.StartsWith("/"))
            {
                path = path.Substring(1);
            }

            // Remove attribute selector if present (e.g., #attribute)
            int attributeIndex = path.IndexOf('#');
            if (attributeIndex >= 0)
            {
                path = path.Substring(0, attributeIndex);
            }

            // Split path into segments
            string[] segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            XElement current = root;

            DebugConsole.Log($"[XPathParser] Path segments: {string.Join(" -> ", segments)}");
            DebugConsole.Log($"[XPathParser] Starting from: <{root.Name}>");

            for (int i = 0; i < segments.Length; i++)
            {
                if (current == null)
                {
                    DebugConsole.AddWarning($"[XPathParser] ❌ Lost at segment {i}: {segments[i]}");
                    break;
                }

                string segment = segments[i];
                DebugConsole.Log($"[XPathParser] [Segment {i+1}/{segments.Length}] Processing: '{segment}'");
                
                XElement next = ProcessSegment(current, segment);
                
                if (next == null)
                {
                    DebugConsole.AddWarning($"[XPathParser] ❌ No match for segment '{segment}' in <{current.Name}>");
                    DebugConsole.AddWarning($"[XPathParser] Available children: {string.Join(", ", current.Elements().Select(e => e.Name.LocalName))}");
                }
                else
                {
                    DebugConsole.Log($"[XPathParser] ✅ Found: <{next.Name}>");
                }
                
                current = next;
            }

            if (current != null)
            {
                DebugConsole.NewMessage($"[XPathParser] ✅ Successfully found element: <{current.Name}>", Color.Green);
            }
            else
            {
                DebugConsole.ThrowError($"[XPathParser] ❌ Failed to find element for path: {path}");
            }

            return current;
        }

        /// <summary>
        /// Parse a path that includes an attribute selector.
        /// Example: "/Wearable/sprite[name='Armor']#hidelimb" 
        /// Returns: ("/Wearable/sprite[name='Armor']", "hidelimb")
        /// </summary>
        public static (string elementPath, string attributeName) ParsePathWithAttribute(string fullPath)
        {
            int attributeIndex = fullPath.LastIndexOf('#');
            
            if (attributeIndex < 0)
            {
                return (fullPath, null);
            }

            string elementPath = fullPath.Substring(0, attributeIndex);
            string attributeName = fullPath.Substring(attributeIndex + 1);

            return (elementPath, attributeName);
        }

        /// <summary>
        /// Process a single path segment, which may include predicates.
        /// Example: "sprite[name='Combat Armor']" or "RequiredItem[identifier=plastic]"
        /// </summary>
        private static XElement ProcessSegment(XElement parent, string segment)
        {
            // Check if segment has predicates (square brackets)
            if (!segment.Contains('['))
            {
                // Simple element name
                var result = parent.Element(segment);
                if (result == null)
                {
                    DebugConsole.AddWarning($"[XPathParser] No child element '{segment}' found");
                }
                return result;
            }

            // Parse element name and predicates
            var (elementName, predicates) = ParsePredicates(segment);
            
            DebugConsole.Log($"[XPathParser] Looking for <{elementName}> with predicates:");
            foreach (var pred in predicates)
            {
                DebugConsole.Log($"[XPathParser]   - {pred.Key} = '{pred.Value}'");
            }

            // Get all elements with matching name
            var matchingElements = parent.Elements(elementName).ToList();
            DebugConsole.Log($"[XPathParser] Found {matchingElements.Count} <{elementName}> elements");

            // Filter by predicates
            int tested = 0;
            foreach (var element in matchingElements)
            {
                tested++;
                bool matches = MatchesPredicates(element, predicates);
                
                if (matches)
                {
                    DebugConsole.NewMessage($"[XPathParser] ✅ Element #{tested} matches!", Color.Green);
                    return element;
                }
                else
                {
                    // Show why it didn't match
                    var attrs = element.Attributes().Select(a => $"{a.Name}='{a.Value}'").ToList();
                    DebugConsole.AddWarning($"[XPathParser] Element #{tested} doesn't match. Attributes: {string.Join(", ", attrs)}");
                }
            }

            DebugConsole.ThrowError($"[XPathParser] ❌ No {elementName} element matched the predicates!");
            return null;
        }

        /// <summary>
        /// Parse predicates from a segment.
        /// Example: "sprite[name='Combat Armor',type=main]" 
        /// Returns: ("sprite", {("name", "Combat Armor"), ("type", "main")})
        /// </summary>
        private static (string elementName, Dictionary<string, string> predicates) ParsePredicates(string segment)
        {
            int bracketIndex = segment.IndexOf('[');
            string elementName = segment.Substring(0, bracketIndex);
            
            // Extract content between brackets
            int closeBracket = segment.LastIndexOf(']');
            string predicateContent = segment.Substring(bracketIndex + 1, closeBracket - bracketIndex - 1);

            var predicates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Split by comma (but be careful with quoted strings)
            var predicatePairs = SplitPredicates(predicateContent);

            foreach (var pair in predicatePairs)
            {
                var parts = pair.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    string attrName = parts[0].Trim();
                    string attrValue = parts[1].Trim().Trim('\'', '"'); // Remove quotes
                    predicates[attrName] = attrValue;
                }
            }

            return (elementName, predicates);
        }

        /// <summary>
        /// Split predicates by comma, respecting quoted strings.
        /// Example: "name='Combat, Heavy',identifier=armor" → ["name='Combat, Heavy'", "identifier=armor"]
        /// </summary>
        private static List<string> SplitPredicates(string predicateContent)
        {
            var results = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            char quoteChar = '\0';

            foreach (char c in predicateContent)
            {
                if ((c == '\'' || c == '"') && !inQuotes)
                {
                    inQuotes = true;
                    quoteChar = c;
                    current.Append(c);
                }
                else if (c == quoteChar && inQuotes)
                {
                    inQuotes = false;
                    current.Append(c);
                }
                else if (c == ',' && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        results.Add(current.ToString().Trim());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
            {
                results.Add(current.ToString().Trim());
            }

            return results;
        }

        /// <summary>
        /// Check if an element matches all specified predicates (attribute filters).
        /// </summary>
        private static bool MatchesPredicates(XElement element, Dictionary<string, string> predicates)
        {
            foreach (var predicate in predicates)
            {
                var attribute = element.Attribute(predicate.Key);
                
                if (attribute == null)
                {
                    return false;
                }

                // Case-insensitive comparison
                if (!string.Equals(attribute.Value, predicate.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Helper method for debugging: print the path to an element
        /// </summary>
        public static string GetElementPath(XElement element)
        {
            if (element == null) return "";
            
            var path = new List<string>();
            var current = element;

            while (current != null)
            {
                path.Insert(0, current.Name.LocalName);
                current = current.Parent;
            }

            return "/" + string.Join("/", path);
        }
    }
}
