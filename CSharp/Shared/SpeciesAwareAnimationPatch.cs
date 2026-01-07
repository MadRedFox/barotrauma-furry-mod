using HarmonyLib;
using System;
using System.Linq;
using Barotrauma;
using Microsoft.Xna.Framework;

namespace ArcticFoxFurryMod
{
    /// <summary>
    /// Harmony patch to fix cross-species animation loading issues.
    /// Prevents animations with ExpectedSpecies set from being applied to wrong species,
    /// fixing the bug where Felinids fall back to DrunkenWalk when removing diving suits.
    /// </summary>
    [HarmonyPatch(typeof(AnimController), nameof(AnimController.TryLoadTemporaryAnimation))]
    public class SpeciesAwareAnimationPatch
    {
        /// <summary>
        /// Prefix patch that filters animations by ExpectedSpecies before they're attempted.
        /// Returns false to skip the original method if the species doesn't match.
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(
            AnimController __instance,
            StatusEffect.AnimLoadInfo animLoadInfo,
            bool throwErrors,
            ref bool __result)
        {
            try
            {
                // If ExpectedSpeciesNames is empty or null, allow all species (default behavior)
                if (animLoadInfo.ExpectedSpeciesNames.IsDefaultOrEmpty)
                {
                    return true; // Continue with original method
                }

                // Get the character from the AnimController
                var character = __instance.Character;
                if (character == null)
                {
                    return true; // Continue with original if can't get character
                }

                // Check if character's species is in the expected list
                bool speciesMatches = animLoadInfo.ExpectedSpeciesNames.Contains(character.SpeciesName);

                if (!speciesMatches)
                {
                    // Species doesn't match - skip this animation
                    // Set result to true so the StatusEffect doesn't mark it as failed and retry
                    __result = true;
                    
                    // Log debug info if throwErrors is true (means ExpectedSpecies includes this species)
                    if (throwErrors)
                    {
                        DebugConsole.Log($"[ArcticFoxMod] Skipped animation '{animLoadInfo.File}' for {character.SpeciesName} " +
                                        $"(expected: {string.Join(", ", animLoadInfo.ExpectedSpeciesNames)})");
                    }
                    
                    return false; // Skip original method
                }

                // Species matches - continue with original method
                return true;
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[ArcticFoxMod] Error in SpeciesAwareAnimationPatch: {ex.Message}");
                return true; // Continue with original on error to avoid breaking game
            }
        }
    }
}
