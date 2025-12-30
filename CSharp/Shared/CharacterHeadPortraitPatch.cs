using HarmonyLib;
using Barotrauma;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.IO;

namespace ArcticFoxFurryMod
{
    /// <summary>
    /// Harmony patches to render ear limbs in character head portraits
    /// Used for ID cards and character creation menu
    /// </summary>
    [HarmonyPatch]
    public class CharacterHeadPortraitPatch
    {
        // Static constructor to verify the class is loaded
        static CharacterHeadPortraitPatch()
        {
            DebugConsole.NewMessage("[ArcticFoxMod] CharacterHeadPortraitPatch loaded successfully!", Color.Green);
        }

        // Storage for ear limb sprites per character
        private static readonly Dictionary<int, List<EarLimbData>> earLimbCache 
            = new Dictionary<int, List<EarLimbData>>();
        
        // Track which characters we've already debugged to prevent console spam
        private static readonly HashSet<int> debuggedCharacters = new HashSet<int>();

        private class EarLimbData
        {
            public Sprite Sprite { get; set; }
            public float Depth { get; set; }
            public string Name { get; set; }
            public Vector2 EarAnchor { get; set; } // limb1anchor - point on ear sprite
            public Vector2 HeadAnchor { get; set; } // limb2anchor - point on head sprite
            public float LimbScale { get; set; }

            public EarLimbData(Sprite sprite, float depth, string name, Vector2 earAnchor, Vector2 headAnchor, float limbScale)
            {
                Sprite = sprite;
                Depth = depth;
                Name = name;
                EarAnchor = earAnchor;
                HeadAnchor = headAnchor;
                LimbScale = limbScale;
            }
        }

        /// <summary>
        /// Determines if a limb element is an ear limb based on its name
        /// </summary>
        private static bool IsEarLimb(ContentXElement limbElement, out string earName)
        {
            string limbName = limbElement.GetAttributeString("name", "").ToLowerInvariant();
            
            // Check in order of priority (back ears first for proper detection)
            if (limbName.Contains("ear_right_back"))
            {
                earName = "ear_right_back";
                return true;
            }
            if (limbName.Contains("ear_left"))
            {
                earName = "ear_left";
                return true;
            }
            if (limbName.Contains("ear_right"))
            {
                earName = "ear_right";
                return true;
            }
            
            earName = null;
            return false;
        }

        /// <summary>
        /// Extract ear limb sprites from character ragdoll with joint positioning
        /// </summary>
        private static List<EarLimbData> ExtractEarLimbs(CharacterInfo characterInfo)
        {
            var earLimbs = new List<EarLimbData>();

            try
            {
                // Reduced debug spam - only log if we find ears

                if (characterInfo?.Ragdoll?.MainElement?.Elements() == null)
                {
                    DebugConsole.NewMessage("[ArcticFoxMod EarPatch] No ragdoll elements found", Color.Yellow);
                    return earLimbs;
                }

                int earCount = 0;
                foreach (ContentXElement limbElement in characterInfo.Ragdoll.MainElement.Elements())
                {
                    if (!IsEarLimb(limbElement, out string earName))
                    {
                        continue;
                    }

                    earCount++;

                    // Extract ear limb scale
                    float earLimbScale = limbElement.GetAttributeFloat("scale", 1.0f);

                    // Extract sprite for this ear
                    ContentXElement spriteElement = limbElement.GetChildElement("sprite");
                    if (spriteElement == null)
                    {
                        DebugConsole.NewMessage($"[ArcticFoxMod EarPatch] No sprite element for {earName}", Color.Yellow);
                        continue;
                    }

                    ContentPath contentPath = spriteElement.GetAttributeContentPath("texture");
                    if (contentPath.IsNullOrEmpty())
                    {
                        DebugConsole.NewMessage($"[ArcticFoxMod EarPatch] No texture path for {earName}", Color.Yellow);
                        continue;
                    }

                    // Replace vars in the path (e.g., [GENDER], [BODY])
                    string spritePath = characterInfo.ReplaceVars(contentPath.Value);
                    string fileName = Path.GetFileNameWithoutExtension(spritePath);
                    
                    // Looking for sprite...

                    // Find matching sprite file
                    Sprite earSprite = null;
                    string spriteDir = Path.GetDirectoryName(spritePath);
                    
                    if (!Directory.Exists(spriteDir))
                    {
                        DebugConsole.NewMessage($"[ArcticFoxMod EarPatch] Directory doesn't exist: {spriteDir}", Color.Red);
                        continue;
                    }

                    foreach (string file in Directory.GetFiles(spriteDir))
                    {
                        if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        
                        string fileWithoutTags = Path.GetFileNameWithoutExtension(file);
                        fileWithoutTags = fileWithoutTags.Split('[', ']').First();
                        
                        if (fileWithoutTags != fileName) continue;
                        
                        earSprite = new Sprite(spriteElement, "", file);
                        break;
                    }

                    if (earSprite != null)
                    {
                        float depth = spriteElement.GetAttributeFloat("depth", 0.05f);
                        
                        // Find the joint for this ear to get anchor points
                        var (earAnchor, headAnchor) = GetEarJointAnchors(characterInfo, earName);
                        
                        earLimbs.Add(new EarLimbData(earSprite, depth, earName, earAnchor, headAnchor, earLimbScale));
                    }
                    else
                    {
                        DebugConsole.NewMessage($"[ArcticFoxMod EarPatch] Failed to load sprite for {earName}", Color.Red);
                    }
                }

                if (earLimbs.Count > 0)
                {
                    DebugConsole.NewMessage($"[ArcticFoxMod] Loaded {earLimbs.Count} ear limbs for character portrait", Color.Green);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[ArcticFoxMod EarPatch] Error extracting ear sprites: {ex.Message}");
                DebugConsole.ThrowError($"[ArcticFoxMod EarPatch] Stack trace: {ex.StackTrace}");
            }

            return earLimbs;
        }

        /// <summary>
        /// Get both anchor points for an ear joint
        /// </summary>
        private static (Vector2 earAnchor, Vector2 headAnchor) GetEarJointAnchors(CharacterInfo characterInfo, string earName)
        {
            try
            {
                if (characterInfo?.Ragdoll?.MainElement == null) return (Vector2.Zero, Vector2.Zero);

                // Find the joint with matching name
                foreach (ContentXElement jointElement in characterInfo.Ragdoll.MainElement.Elements())
                {
                    if (!jointElement.Name.ToString().Equals("joint", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string jointName = jointElement.GetAttributeString("name", "");
                    if (!jointName.Equals(earName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Found the joint! Extract BOTH anchor positions
                    // limb1anchor = point on the EAR sprite
                    // limb2anchor = point on the HEAD sprite
                    string limb1AnchorStr = jointElement.GetAttributeString("limb1anchor", "0,0");
                    string limb2AnchorStr = jointElement.GetAttributeString("limb2anchor", "0,0");
                    
                    var anchor1Parts = limb1AnchorStr.Split(',');
                    var anchor2Parts = limb2AnchorStr.Split(',');
                    
                    if (anchor1Parts.Length >= 2 && anchor2Parts.Length >= 2)
                    {
                        float earX = float.Parse(anchor1Parts[0]);
                        float earY = float.Parse(anchor1Parts[1]);
                        float headX = float.Parse(anchor2Parts[0]);
                        float headY = float.Parse(anchor2Parts[1]);
                        
                        // Y-axis is inverted for screen space
                        Vector2 earAnchor = new Vector2(earX, -earY);
                        Vector2 headAnchor = new Vector2(headX, -headY);
                        
                        return (earAnchor, headAnchor);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[ArcticFoxMod EarPatch] Error getting joint anchors for {earName}: {ex.Message}");
            }

            return (Vector2.Zero, Vector2.Zero);
        }

        /// <summary>
        /// Get the scale of the head limb from ragdoll definition
        /// </summary>
        private static float GetHeadLimbScale(CharacterInfo characterInfo)
        {
            try
            {
                if (characterInfo?.Ragdoll?.MainElement?.Elements() == null) return 1.0f;

                foreach (ContentXElement limbElement in characterInfo.Ragdoll.MainElement.Elements())
                {
                    if (!limbElement.Name.ToString().Equals("limb", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string limbType = limbElement.GetAttributeString("type", "");
                    if (limbType.Equals("head", StringComparison.OrdinalIgnoreCase))
                    {
                        float scale = limbElement.GetAttributeFloat("scale", 1.0f);
                        return scale;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[ArcticFoxMod EarPatch] Error getting head limb scale: {ex.Message}");
            }

            return 1.0f;
        }

        /// <summary>
        /// Postfix patch for CharacterInfo.DrawIcon
        /// Renders ear limbs after the original method draws the head and wearables
        /// </summary>
        [HarmonyPatch(typeof(CharacterInfo), nameof(CharacterInfo.DrawIcon))]
        [HarmonyPostfix]
        public static void DrawIcon_Postfix(
            CharacterInfo __instance,
            SpriteBatch spriteBatch,
            Vector2 screenPos,
            Vector2 targetAreaSize,
            bool flip = false)
        {
            try
            {
                // Drawing ears with detailed coordinate logging

                // Use head sprite index as part of cache key so ears update when head changes
                int headIndex = (int)__instance.Head.SheetIndex.X + ((int)__instance.Head.SheetIndex.Y * 1000);
                int charKey = __instance.GetHashCode() + headIndex;

                // Check cache or extract ears (re-extract if head changed)
                if (!earLimbCache.ContainsKey(charKey))
                {
                    earLimbCache[charKey] = ExtractEarLimbs(__instance);
                }

                var earLimbs = earLimbCache[charKey];
                if (earLimbs.Count == 0)
                {
                    return; // No ears, silently return
                }

                var headSprite = __instance.HeadSprite;
                if (headSprite == null) return;

                // Calculate scale to match how the head is drawn
                float scale = Math.Min(targetAreaSize.X / headSprite.size.X, targetAreaSize.Y / headSprite.size.Y);
                var spriteEffects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

                // Sort by depth (back ears first)
                var sortedEars = earLimbs.OrderBy(e => e.Depth).ToList();

                // Drawing ears with detailed coordinate logging

                // Only debug once per character to avoid spam
                bool shouldDebug = !debuggedCharacters.Contains(charKey);
                if (shouldDebug)
                {
                    debuggedCharacters.Add(charKey);
                }

                foreach (var earData in sortedEars)
                {
                    // Calculate position so that earAnchor point aligns with headAnchor point
                    // Anchors are in limb's local coordinate space, divide by limb scale to normalize
                    float headLimbScale = GetHeadLimbScale(__instance);
                    float earLimbScale = earData.LimbScale;
                    
                    Vector2 adjustedHeadAnchor = earData.HeadAnchor / headLimbScale;
                    Vector2 adjustedEarAnchor = earData.EarAnchor / earLimbScale;
                    Vector2 alignmentOffset = (adjustedHeadAnchor - adjustedEarAnchor) * scale;
                    
                    // When flipped, negate the X-component of the offset
                    if (flip)
                    {
                        alignmentOffset.X = -alignmentOffset.X;
                    }
                    
                    Vector2 earPosition = screenPos + alignmentOffset;
                    
                    Vector2 origin = earData.Sprite.Origin;
                    if (flip)
                    {
                        origin.X = earData.Sprite.size.X - origin.X;
                    }

                    // Draw ear at calculated position
                    earData.Sprite.Draw(
                        spriteBatch,
                        earPosition,
                        origin: origin,
                        scale: scale,
                        color: __instance.Head.SkinColor,
                        depth: earData.Depth,
                        spriteEffect: spriteEffects
                    );
                }
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[ArcticFoxMod EarPatch] Error in DrawIcon_Postfix: {ex.Message}");
                DebugConsole.ThrowError($"[ArcticFoxMod EarPatch] Stack trace: {ex.StackTrace}");
            }
        }
    }
}
