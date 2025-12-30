using HarmonyLib;
using System;
using System.Reflection;
using Barotrauma;
using Microsoft.Xna.Framework;
using PartialItemOverride;

namespace ArcticFoxFurryMod
{
    /// <summary>
    /// Tail movement mod entry point - integrates with Barotrauma's mod loading system
    /// </summary>
    public class TailMovementMod : IAssemblyPlugin
    {
        private static Harmony harmony;
        private static bool isInitialized = false;

        public void Initialize()
        {
            if (isInitialized)
            {
                DebugConsole.NewMessage("[ArcticFoxMod] Tail movement already initialized, skipping...", Color.Yellow);
                return;
            }

            try
            {
                // Create Harmony instance with unique ID
                harmony = new Harmony("com.arcticfox.barotrauma.tailmovement");
                
                // Apply all patches from HumanoidTailPatch class and CharacterHeadPortraitPatch class
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                
                // Initialize the Partial Item Override System
                PartialItemOverrideSystem.Initialize("com.arcticfox.barotrauma");
                
                isInitialized = true;
                
                DebugConsole.NewMessage("[ArcticFoxMod] Tail movement successfully initialized! Humanoid characters can now have animated tails.", new Color(0, 255, 0));
                DebugConsole.NewMessage("[ArcticFoxMod] Character portrait patches initialized! Ear limbs will now render on ID cards and character icons.", new Color(0, 255, 0));
                DebugConsole.NewMessage("[ArcticFoxMod] Partial Item Override System enabled! Use inherit=\"true\" in item XMLs.", new Color(100, 200, 255));
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[ArcticFoxMod] Failed to initialize tail movement: {ex.Message}");
                DebugConsole.ThrowError($"[ArcticFoxMod] Stack trace: {ex.StackTrace}");
            }
        }

        public void OnLoadCompleted()
        {
            // Called after all mods are loaded
            if (isInitialized)
            {
                DebugConsole.NewMessage("[ArcticFoxMod] Tail movement mod fully loaded. Enjoy your animated tails! 🦊", new Color(100, 200, 255));
            }
        }

        public void PreInitPatching()
        {
            // Called before initialization if needed
        }

        public void Dispose()
        {
            // Cleanup when mod is unloaded
            if (!isInitialized) return;

            try
            {
                harmony?.UnpatchSelf();
                isInitialized = false;
                
                DebugConsole.NewMessage("[ArcticFoxMod] Tail movement patches cleaned up successfully!");
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[ArcticFoxMod] Failed to cleanup tail movement: {ex.Message}");
            }
        }
    }
}

