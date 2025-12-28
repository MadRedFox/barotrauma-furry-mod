using HarmonyLib;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using Barotrauma;

namespace ArcticFoxFurryMod
{
    /// <summary>
    /// Harmony patches to add tail movement support to humanoid characters
    /// Allows anthro characters to have animated tails while walking, running, and swimming
    /// </summary>
    [HarmonyPatch]
    public class HumanoidTailPatch
    {
        // Storage for tail properties per animation instance
        // Key format: "TypeName_InstanceHashCode"
        private static readonly Dictionary<string, TailProperties> tailPropertiesStorage 
            = new Dictionary<string, TailProperties>();

        /// <summary>
        /// Container for tail-related properties including animation state
        /// </summary>
        private class TailProperties
        {
            public float TailAngleInRadians { get; set; } = -0.26f; // Base angle: -15 degrees
            public float TailTorque { get; set; } = 3.5f; // Gentle torque for physics-friendly application
            public float AnimationTimer { get; set; } = 0.0f; // Timer for wave animation
            
            // Frame tracking to prevent multiple updates per frame
            public double LastUpdateTime { get; set; } = 0.0; // Track last time we updated (in seconds)
            
            // Randomized animation parameters
            public float RandomCycleDuration { get; set; } = 0f; // Random cycle length (1x to 2x of base)
            public float RandomPauseDuration { get; set; } = 0.0f; // Random pause after cycle (5-15 seconds)
            public float RandomMaxAngleBase { get; set; } = -0.698f; // Randomized max angle for base tail
            public float RandomMaxAngleExtended { get; set; } = -1.047f; // Randomized max angle for extended segments
            public bool InPause { get; set; } = false; // Currently pausing between cycles
            public float LastPauseChangeTime { get; set; } = 0.0f; // When we last entered/exited pause for smooth transitions
            
            private static Random random = new Random();
            
            /// <summary>
            /// Generates new random animation parameters for the next cycle
            /// </summary>
            /// <param name="currentTime">Current animation time (unused in simple version)</param>
            /// <param name="isFirstInit">True if this is the first initialization</param>
            public void GenerateNewCycle(float currentTime, bool isFirstInit = false)
            {
                // Random cycle duration: 4 to 8 seconds (much slower, less variable)
                RandomCycleDuration = 4.0f + ((float)random.NextDouble() * 4.0f);
                
                // Random pause: 2 to 4 seconds (short, frequent animations)
                RandomPauseDuration = 2.0f + ((float)random.NextDouble() * 2.0f);
                
                // Generate random max angles for both base and extended segments
                // Base tail: -40° to -65° (in radians: -0.698 to -1.134)
                float baseDegrees = -40f + ((float)random.NextDouble() * -25f);
                RandomMaxAngleBase = MathHelper.ToRadians(baseDegrees);
                
                // Extended segments: -40° to -70° (in radians: -0.698 to -1.222)
                float extendedDegrees = -40f + ((float)random.NextDouble() * -30f);
                RandomMaxAngleExtended = MathHelper.ToRadians(extendedDegrees);
                
                // Set pause flag - first init starts immediately, subsequent enter pause
                InPause = !isFirstInit;
            }
        }

        /// <summary>
        /// Gets or creates tail properties for an animation parameter object
        /// Now keyed by controller instance so all tail segments share the same timing
        /// </summary>
        private static TailProperties GetOrCreateTailProps(object controller)
        {
            if (controller == null) return new TailProperties();
            
            // Use controller hash code so all tail segments of same character share timing
            string key = $"{controller.GetType().Name}_{controller.GetHashCode()}";
            if (!tailPropertiesStorage.ContainsKey(key))
            {
                tailPropertiesStorage[key] = new TailProperties();
            }
            return tailPropertiesStorage[key];
        }

        /// <summary>
        /// Calculates dynamic tail angle based on animation timer and randomized parameters
        /// Uses three-phase animation with specific easing:
        /// - Phase 1 (0-33%): Initial upward movement with ease-out
        /// - Phase 2 (33-66%): Downward with overshoot using ease-in-ease-out
        /// - Phase 3 (66-100%): Return from overshoot with ease-out
        /// </summary>
        private static float CalculateDynamicTailAngle(TailProperties tailProps, bool isExtendedSegment)
        {
            // Initialize if needed
            if (tailProps.RandomCycleDuration == 0f)
            {
                tailProps.GenerateNewCycle(tailProps.AnimationTimer, isFirstInit: true);
            }
            
            float totalTime = tailProps.AnimationTimer;
            float cycleDuration = tailProps.RandomCycleDuration;
            float pauseDuration = tailProps.RandomPauseDuration;
            float totalPeriod = cycleDuration + pauseDuration;
            
            // Calculate position in the total period (cycle + pause)
            float timeInPeriod = totalTime % totalPeriod;
            
            // Check if we're in the active animation or pause
            float wave;
            if (timeInPeriod >= cycleDuration)
            {
                // During pause - return to rest (wave = 0)
                wave = 0f;
                
                // Generate new random values for next cycle when entering pause
                if (!tailProps.InPause)
                {
                    tailProps.GenerateNewCycle(totalTime, isFirstInit: false);
                    tailProps.LastPauseChangeTime = totalTime;
                }
            }
            else
            {
                // Exit pause mode
                if (tailProps.InPause)
                {
                    tailProps.LastPauseChangeTime = totalTime;
                }
                tailProps.InPause = false;
                
                // Normalize time within cycle: 0 to 1
                float normalizedTime = timeInPeriod / cycleDuration;
                
                // Define three phases
                // Phase 1: 0.0 - 0.33 (initial upward movement)
                // Phase 2: 0.33 - 0.66 (downward with overshoot)
                // Phase 3: 0.66 - 1.0 (return from overshoot to rest)
                
                if (normalizedTime < 0.33f)
                {
                    // PHASE 1: Initial upward movement (0 -> 1) with ease-out
                    float t = normalizedTime / 0.33f; // Normalize to 0-1 within this phase
                    
                    // Ease-out (light at the end): t * (2 - t)
                    // This gives a quick start and gentle finish
                    float easedT = t * (2.0f - t);
                    
                    // Map to wave value: 0 -> 1
                    wave = easedT;
                }
                else if (normalizedTime < 0.66f)
                {
                    // PHASE 2: Downward movement with overshoot (1 -> -overshoot) with ease-in-ease-out
                    float t = (normalizedTime - 0.33f) / 0.33f; // Normalize to 0-1 within this phase
                    
                    // Ease-in-ease-out using smoothstep
                    float easedT = SmoothStep(t);
                    
                    // Map from 1 to overshoot point (let's use -1.3 for 30% overshoot)
                    float overshootValue = -1.3f;
                    wave = 1.0f + easedT * (overshootValue - 1.0f);
                }
                else
                {
                    // PHASE 3: Return from overshoot to rest (-overshoot -> 0) with ease-out
                    float t = (normalizedTime - 0.66f) / 0.34f; // Normalize to 0-1 within this phase
                    
                    // Ease-out: t * (2 - t)
                    float easedT = t * (2.0f - t);
                    
                    // Map from overshoot (-1.3) back to 0
                    float overshootValue = -1.3f;
                    wave = overshootValue + easedT * (0.0f - overshootValue);
                }
            }
            
            // Smooth fade during pause transitions to prevent twitching
            float pauseFadeTime = 0.4f; // 400ms fade
            float timeSincePauseChange = totalTime - tailProps.LastPauseChangeTime;
            float pauseFadeFactor = MathHelper.Clamp(timeSincePauseChange / pauseFadeTime, 0f, 1f);
            
            // Apply fade: when entering pause, fade wave down to 0; when exiting, fade up from 0
            if (tailProps.InPause)
            {
                // Fading into pause (wave -> 0)
                wave *= (1.0f - pauseFadeFactor);
            }
            else if (timeSincePauseChange < pauseFadeTime)
            {
                // Fading out of pause (0 -> wave)
                wave *= pauseFadeFactor;
            }
            
            // Angle range depends on segment type
            // Base tail: -15° (rest) to random max angle
            // Extended segments: +15° (rest) to random max angle
            float minAngle = isExtendedSegment ? 0.26f : -0.26f; // 15° for extended, -15° for base
            float maxAngle = isExtendedSegment ? tailProps.RandomMaxAngleExtended : tailProps.RandomMaxAngleBase;
            
            // Map wave from [-1.3, 1] to angle range
            // wave = 0 -> rest position (minAngle)
            // wave = 1 -> max position (maxAngle)
            // wave = -1.3 -> overshoot beyond rest
            
            // Calculate overshoot angle (10% beyond rest position)
            float overshootAngle = minAngle - (maxAngle - minAngle) * 0.1f;
            
            // Map wave to angles:
            // wave in [-1.3, 0, 1] -> angle in [overshoot, rest, max]
            float dynamicAngle;
            if (wave < 0)
            {
                // Between overshoot and rest
                float t = (wave + 1.3f) / 1.3f; // 0 at overshoot, 1 at rest
                dynamicAngle = overshootAngle + t * (minAngle - overshootAngle);
            }
            else
            {
                // Between rest and max
                dynamicAngle = minAngle + wave * (maxAngle - minAngle);
            }
            
            return dynamicAngle;
        }


        /// <summary>
        /// Determines if a limb is a tail segment based on its type and name
        /// </summary>
        private static bool IsTailLimb(Limb limb, out bool isExtendedSegment)
        {
            isExtendedSegment = false;
            
            // Check standard Tail type
            if (limb.type == LimbType.Tail)
            {
                return true;
            }
            
            // Check by name for custom tail segments
            string limbName = limb.Name?.ToLowerInvariant() ?? "";
            if (limbName.Contains("tailmedium") || limbName.Contains("tailend"))
            {
                isExtendedSegment = true;
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Smooth step function for ease-in/ease-out effect
        /// </summary>
        private static float SmoothStep(float t)
        {
            // Clamp to [0, 1]
            t = MathHelper.Clamp(t, 0f, 1f);
            // Cubic smoothstep: 3t² - 2t³
            return t * t * (3.0f - 2.0f * t);
        }

        #region UpdateAnim Patches - Ground Movement (Standing/Walking/Running)

        /// <summary>
        /// Injects tail movement logic into HumanoidAnimController.UpdateStanding
        /// This handles tail movement when the character is on the ground
        /// </summary>
        [HarmonyPatch(typeof(HumanoidAnimController), "UpdateStanding")]
        [HarmonyPostfix]
        public static void UpdateStanding_TailMovement(HumanoidAnimController __instance)
        {
            try
            {
                // Get current animation params
                var currentGroundedParams = GetCurrentGroundedParams(__instance);
                if (currentGroundedParams == null) return;

                // Get tail properties (keyed by controller instance so all segments sync)
                var tailProps = GetOrCreateTailProps(__instance);
                
                // Get current time for frame tracking
                double currentTime = Barotrauma.Timing.TotalTime;
                
                // Only update timer once per frame to prevent twitching from multiple patch calls
                // Use epsilon comparison for floating point
                if (Math.Abs(currentTime - tailProps.LastUpdateTime) > 0.0001)
                {
                    // Update animation timer using game's delta time
                    float deltaTime = (float)Barotrauma.Timing.Step;
                    tailProps.AnimationTimer += deltaTime;
                    tailProps.LastUpdateTime = currentTime;
                }

                // Calculate movement angle
                var mainLimb = __instance.MainLimb;
                if (mainLimb == null) return;

                // Detect movement speed for animation blending
                // Blend factor: 1.0 when idle, 0.0 when moving fast
                float movementSpeed = __instance.TargetMovement.Length();
                float animationBlend = MathHelper.Clamp(1.0f - movementSpeed * 1.5f, 0f, 1f);
                
                // Calculate movement-based base angle for tail
                // Walking (slow movement): -40°, Running (fast): -60°
                float movementBaseAngle = 0f;
                if (movementSpeed > 0.01f)
                {
                    // Lerp between -40° (walking) and -60° (running) based on speed
                    // Speed 0.5 = walking, 1.0+ = running
                    float speedFactor = MathHelper.Clamp(movementSpeed / 0.5f, 0f, 1f);
                    float walkAngle = MathHelper.ToRadians(-40f);
                    float runAngle = MathHelper.ToRadians(-60f);
                    movementBaseAngle = MathHelper.Lerp(walkAngle, runAngle, speedFactor);
                }
                
                // Skip full animation if character is moving too much, but keep movement angle
                bool applyMovementAngle = movementSpeed > 0.01f;
                if (animationBlend < 0.05f && !applyMovementAngle)
                {
                    return; // Physics-only when moving fast without movement angle
                }

                float movementAngle = 0.0f;
                float tailTorque = tailProps.TailTorque;
                
                // Apply tail rotation to all tail limbs
                bool isAngleApplied = false;
                int tailLimbCount = 0; // Track how many tail limbs we find
                foreach (var limb in __instance.Limbs)
                {
                    if (limb.IsSevered) continue;
                    
                    // Check if this is a tail limb (by type or name)
                    if (!IsTailLimb(limb, out bool isExtendedSegment)) continue;
                    
                    // Found a tail limb
                    tailLimbCount++;
                    
                    // Calculate appropriate angle based on segment type using randomized parameters
                    float segmentAngle = CalculateDynamicTailAngle(tailProps, isExtendedSegment);
                    
                    // Track whether we're applying movement angle for this segment
                    float effectiveBlend = animationBlend;
                    
                    // For base tail segment, apply movement-based angle when moving
                    if (!isExtendedSegment && movementBaseAngle != 0f)
                    {
                        // Blend between animated angle and movement base angle
                        float movementBlend = MathHelper.Clamp(movementSpeed * 2.0f, 0f, 1f);
                        segmentAngle = MathHelper.Lerp(segmentAngle, movementBaseAngle, movementBlend);
                        
                        // Boost blend factor for base tail when moving so movement angle is actually applied
                        // Otherwise the low animationBlend will prevent the angle from showing
                        effectiveBlend = MathHelper.Max(animationBlend, movementBlend * 0.7f);
                    }
                    
                    RotateTail(limb, movementAngle, segmentAngle, __instance.Dir, mainLimb, tailTorque, effectiveBlend);
                    isAngleApplied = true;
                }

                // Fallback: try to get the primary tail limb
                if (!isAngleApplied)
                {
                    var tailLimb = __instance.GetLimb(LimbType.Tail);
                    if (tailLimb != null)
                    {
                        float fallbackAngle = CalculateDynamicTailAngle(tailProps, false);
                        RotateTail(tailLimb, movementAngle, fallbackAngle, __instance.Dir, mainLimb, tailTorque, animationBlend);
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently catch exceptions to prevent mod from breaking the game
                DebugConsole.ThrowError($"[ArcticFoxMod] Error in tail movement (ground): {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to rotate tail limb (ground movement version)
        /// Mirrors the logic from FishAnimController.UpdateWalkAnim
        /// </summary>
        private static void RotateTail(Limb tail, float movementAngle, 
            float tailAngle, float dir, Limb mainLimb, float tailTorque, float animationBlend)
        {
            if (tail == null) return;

            try
            {
                float targetAngle = movementAngle + tailAngle * dir;
                
                // Debug: Log rotation attempt
                if (tail.body == null)
                {
                    return;
                }
                
                // Wrap angle to same number of revolutions as reference limb
                if (mainLimb != null && mainLimb.body != null)
                {
                    targetAngle = mainLimb.body.WrapAngleToSameNumberOfRevolutions(targetAngle);
                }
                
                // Apply blended rotation - lerp between current rotation and target
                // When blend = 1.0 (idle), full animation
                // When blend = 0.0 (moving), no animation (physics only)
                float currentRotation = tail.body.Rotation;
                float blendedAngle = MathHelper.Lerp(currentRotation, targetAngle, animationBlend);
                float blendedTorque = tailTorque * animationBlend;
                
                // Smooth rotate the tail with blended values
                tail.body?.SmoothRotate(blendedAngle, blendedTorque, false);
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[ArcticFoxMod] Error rotating tail: {ex.Message}");
            }
        }

        #endregion

        #region UpdateAnim Patches - Swimming

        /// <summary>
        /// Injects tail movement logic into HumanoidAnimController.UpdateSwimming
        /// This handles tail movement when the character is swimming
        /// </summary>
        [HarmonyPatch(typeof(HumanoidAnimController), "UpdateSwimming")]
        [HarmonyPostfix]
        public static void UpdateSwimming_TailMovement(HumanoidAnimController __instance)
        {
            try
            {
                // Get current swim params
                var currentSwimParams = GetCurrentSwimParams(__instance);
                if (currentSwimParams == null) return;

                // Get tail properties (keyed by controller instance so all segments sync)
                var tailProps = GetOrCreateTailProps(__instance);
                
                // Get current time for frame tracking
                double currentTime = Barotrauma.Timing.TotalTime;
                
                // Only update timer once per frame to prevent twitching from multiple patch calls
                // Use epsilon comparison for floating point
                if (Math.Abs(currentTime - tailProps.LastUpdateTime) > 0.0001)
                {
                    // Update animation timer using game's delta time
                    float deltaTime = (float)Barotrauma.Timing.Step;
                    tailProps.AnimationTimer += deltaTime;
                    tailProps.LastUpdateTime = currentTime;
                }

                // Get movement direction
                Vector2 movement = __instance.TargetMovement;
                bool isMoving = movement.LengthSquared() > 0.00001f;
                
                // Detect movement speed for animation blending
                // Blend factor: 1.0 when idle, 0.0 when moving fast
                float movementSpeed = movement.Length();
                float animationBlend = MathHelper.Clamp(1.0f - movementSpeed * 1.5f, 0f, 1f);
                
                // Skip animation if character is moving too much (let physics handle it)
                if (animationBlend < 0.05f)
                {
                    return; // Physics-only when swimming
                }
                
                // Calculate movement angle
                float movementAngle = isMoving ? MathUtils.VectorToAngle(movement) - MathHelper.PiOver2 : 0f;
                float tailTorque = tailProps.TailTorque;
                
                var mainLimb = __instance.MainLimb;
                if (mainLimb == null) return;

                // Apply tail rotation to all tail limbs
                bool isAngleApplied = false;
                foreach (var limb in __instance.Limbs)
                {
                    if (limb.IsSevered) continue;
                    
                    // Check if this is a tail limb (by type or name)
                    if (!IsTailLimb(limb, out bool isExtendedSegment)) continue;
                    
                    // Calculate appropriate angle using randomized parameters
                    float segmentAngle = CalculateDynamicTailAngle(tailProps, isExtendedSegment);
                    
                    RotateTailSwim(limb, movementAngle, segmentAngle, __instance.Dir, mainLimb, tailTorque, animationBlend);
                    isAngleApplied = true;
                }

                // Fallback: try to get the primary tail limb
                if (!isAngleApplied)
                {
                    var tailLimb = __instance.GetLimb(LimbType.Tail);
                    if (tailLimb != null)
                    {
                        float fallbackAngle = CalculateDynamicTailAngle(tailProps, false);
                        RotateTailSwim(tailLimb, movementAngle, fallbackAngle, __instance.Dir, mainLimb, tailTorque, animationBlend);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[ArcticFoxMod] Error in tail movement (swim): {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to rotate tail limb (swimming version)
        /// Mirrors the logic from FishAnimController.UpdateSineAnim
        /// </summary>
        private static void RotateTailSwim(Limb tail, float movementAngle, 
            float tailAngle, float dir, Limb mainLimb, float tailTorque, float animationBlend)
        {
            if (tail == null) return;

            try
            {
                float targetAngle = movementAngle + tailAngle * dir;
                
                // Wrap angle to same number of revolutions as reference limb
                if (mainLimb != null && mainLimb.body != null)
                {
                    targetAngle = mainLimb.body.WrapAngleToSameNumberOfRevolutions(targetAngle);
                }
                
                // Apply blended rotation - lerp between current rotation and target
                // When blend = 1.0 (idle), full animation
                // When blend = 0.0 (swimming fast), no animation (physics only)
                if (tail.body != null)
                {
                    float currentRotation = tail.body.Rotation;
                    float blendedAngle = MathHelper.Lerp(currentRotation, targetAngle, animationBlend);
                    float blendedTorque = tailTorque * animationBlend; // Scale torque with blend
                    
                    // Smooth rotate the tail with blended values
                    tail.body.SmoothRotate(blendedAngle, blendedTorque, false);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError($"[ArcticFoxMod] Error rotating tail (swim): {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        ///Gets the current grounded animation parameters - direct access since it's a public property
        /// </summary>
        private static HumanGroundedParams GetCurrentGroundedParams(HumanoidAnimController controller)
        {
            try
            {
                return controller.CurrentGroundedParams;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the current swim animation parameters - direct access since it's a public property
        /// </summary>
        private static HumanSwimParams GetCurrentSwimParams(HumanoidAnimController controller)
        {
            try
            {
                return controller.CurrentSwimParams;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
