using System;
using System.Reflection;
using Barotrauma;
using HarmonyLib;
using System.Linq;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using System.Collections.Immutable;
using Microsoft.Xna.Framework; // just for color

namespace ConditionalSpritesNamespace 
{
    class ConditionalSpritesMod: IAssemblyPlugin 
    {
        public Harmony harmony;
        public void Initialize()
        {
            harmony = new Harmony("arctic.fox.conditional.sprites.mod");

            DebugConsole.NewMessage("Patch started", new Color(0, 255, 0));
            harmony.PatchAll();
        }

        public void OnLoadCompleted() { }
        public void PreInitPatching() { }

        public void Dispose()
        {
            // Unpatch all patches from this Harmony instance
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }

    [HarmonyPatch(typeof(Wearable))]
    class ConditionalSpritesPatch
    {
        [HarmonyPatch(nameof(Wearable.Equip))]
        [HarmonyPatch(new Type[] { typeof(Character) })]
        public static bool Prefix(Character character, ref Wearable __instance)
        {
            foreach (var allowedSlot in __instance.allowedSlots)
            {
                if (allowedSlot == InvSlotType.Any) { continue; }
                foreach (Enum value in Enum.GetValues(typeof(InvSlotType)))
                {
                    var slotType = (InvSlotType)value;
                    if (slotType == InvSlotType.Any || slotType == InvSlotType.None) { continue; }
                    if (allowedSlot.HasFlag(slotType) && !character.Inventory.IsInLimbSlot(__instance.item, slotType))
                    {
                        return false;
                    }
                }
            }

            __instance.picker = character;

            for (int i = 0; i < __instance.wearableSprites.Length; i++ )
            {
                var wearableSprite = __instance.wearableSprites[i];

                ConditionalSpriteTagRewrite(ref wearableSprite, ref __instance);

                if (!wearableSprite.IsInitialized) { wearableSprite.Init(__instance.picker); }
                // If the item is gender specific (it has a different textures for male and female), we have to change the gender here so that the texture is updated.
                wearableSprite.Picker = __instance.picker;

                Limb equipLimb  = character.AnimController.GetLimb(__instance.limbType[i]);
                if (equipLimb == null) { continue; }
                
                if (__instance.item.body != null)
                {
                    __instance.item.body.Enabled = false;
                }
                __instance.IsActive = true;
                if (wearableSprite.LightComponent != null)
                {
                    foreach (var light in wearableSprite.LightComponents)
                    {
                        light.ParentBody = equipLimb.body;
                    }
                }

                __instance.limb[i] = equipLimb;
                if (!equipLimb.WearingItems.Contains(wearableSprite))
                {
                    equipLimb.WearingItems.Add(wearableSprite);
                    equipLimb.WearingItems.Sort((wearable, nextWearable) =>
                    {
                        float depth = wearable?.Sprite?.Depth ?? 0;
                        float nextDepth = nextWearable?.Sprite?.Depth ?? 0;
                        return nextDepth.CompareTo(depth);
                    });
                    equipLimb.WearingItems.Sort((wearable, nextWearable) => 
                    {
                        var wearableComponent = wearable?.WearableComponent;
                        var nextWearableComponent = nextWearable?.WearableComponent;
                        if (wearableComponent == null && nextWearableComponent == null) { return 0; }
                        if (wearableComponent == null) { return -1; }
                        if (nextWearableComponent == null) { return 1; }
                        return wearableComponent.AllowedSlots.Contains(InvSlotType.OuterClothes).CompareTo(nextWearableComponent.AllowedSlots.Contains(InvSlotType.OuterClothes));
                    });
                }
#if CLIENT
                equipLimb.UpdateWearableTypesToHide();
#endif
            }
            character.OnWearablesChanged();
            return false;
        }

        public static void ConditionalSpriteTagRewrite(ref WearableSprite wearableSprite, ref Wearable wearable)
        {
            var element = wearableSprite.SourceElement;

            foreach(var subElement in element.Elements()) {
                if (subElement.Name.ToString().ToLowerInvariant() == "conditionalsprite") {
                    ISerializableEntity spriteTarget;
                    string target = subElement.GetAttributeString("target", null);

                    if (string.Equals(target, "person", StringComparison.OrdinalIgnoreCase)) {
                        spriteTarget = wearable.picker;
                    }
                    else {
                        spriteTarget = wearable.item;
                    }

                    ConditionalSprite conditionalSprite = new ConditionalSprite(subElement, spriteTarget);
                    conditionalSprite.CheckConditionals();

                    if (conditionalSprite.IsActive) {
                        var properties = subElement.GetChildElement("properties");
                        if (properties != null) {
                            foreach(var property in properties.Elements()) {
                                if (property.Name.ToString().ToLowerInvariant() == "property") {
                                    element.SetAttributeValue(property.GetAttributeString("name", null), property.ElementInnerText());
                                }
                            }
                        }
                    }
                }
            }

            wearableSprite.SourceElement = element;
        }
    }

    // [HarmonyPatch(typeof(WearableSprite))]
    // class WearableSpritePatch
    // {
    //     [HarmonyPatch(MethodType.Constructor)]
    //     [HarmonyPatch(new Type[] { typeof(ContentXElement), typeof(Wearable), typeof(int)})]
    //     public static void Prefix(ref WearableSprite __instance, ContentXElement subElement, ref Wearable wearable, int variant = 0)
    //     {
    //         foreach(var element in subElement.Elements()) {
    //             if (element.Name.ToString().ToLowerInvariant() == "conditionalsprite") {
    //                 ISerializableEntity spriteTarget;
    //                 string target = element.GetAttributeString("target", null);

    //                 if (string.Equals(target, "person", StringComparison.OrdinalIgnoreCase)) {
    //                     spriteTarget = __instance.Picker;
    //                 }
    //                 else {
    //                     spriteTarget = wearable.item;
    //                 }

    //                 ConditionalSprite conditionalSprite = new ConditionalSprite(element, spriteTarget);
    //                 conditionalSprite.CheckConditionals();
    //                 if (conditionalSprite.IsActive) {
    //                     DebugConsole.NewMessage("ConditionalSprite is active", new Color(0, 255, 0));
    //                     var properties = subElement.GetChildElement("properties");
    //                     if (properties != null) {
    //                         DebugConsole.NewMessage("Properties found", new Color(0, 255, 0));
    //                         foreach(var property in properties.Elements()) {
    //                             if (property.Name.ToString().ToLowerInvariant() == "property") {
    //                                 element.SetAttributeValue(property.GetAttributeString("name", null), property.ElementInnerText());
    //                                 DebugConsole.NewMessage(element.GetAttributeString(property.GetAttributeString("name", null), null), new Color(0, 255, 0));
    //                             }
    //                         }
    //                     }
    //                 }
    //             }
    //         }
    //     }
    //}
    
    /** Хакаем скелет для добавления новых костей */
    // [HarmonyPatch(typeof(Character))]
    // class CharacterPatch
    // {
    //     [HarmonyPatch(nameof(Character.Create))]
    //     [HarmonyPatch(new Type[] { typeof(CharacterPrefab), typeof(Vector2), typeof(string), typeof(CharacterInfo), typeof(ushort), typeof(bool), typeof(bool), typeof(bool), typeof(RagdollParams), typeof(bool)})]
    //     public static void Prefix(CharacterPrefab prefab, Vector2 position, string seed, CharacterInfo characterInfo = null, ushort id = Entity.NullEntityID, bool isRemotePlayer = false, bool hasAi = true, bool createNetworkEvent = true, RagdollParams ragdoll = null, bool spawnInitialItems = true)
    //     {
    //         var opt = new JsonSerializerOptions(){ WriteIndented=true };
    //         string[] headTags = characterInfo.Head.Preset.Tags.Split(',');

    //         foreach (var headTag in headTags) {
    //             if (headTag.ToLower() == "furry") {
    //                 characterInfo.CharacterConfigElement.GetChildElement("ragdolls").SetAttributeValue("folder", "%ModDir%/Characters/Human/furryRagdolls/");
    //             }
    //             if (headTag.ToLower() == "human") {
    //                 characterInfo.CharacterConfigElement.GetChildElement("ragdolls").SetAttributeValue("folder", "%ModDir%/Characters/Human/humanRagdolls/");
    //             }
    //         }
    //     }
    // }

    /** Добавляем расовое разнообразие для NPC */
    [HarmonyPatch(typeof(HumanPrefab))]
    class HumanPrefabPatch
    {
        [HarmonyPatch(nameof(HumanPrefab.CreateCharacterInfo))]
        [HarmonyPatch(new Type[] { typeof(Rand.RandSync)})]
        public static bool Prefix(ref HumanPrefab __instance, ref CharacterInfo __result, Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            var characterElement = ToolBox.SelectWeightedRandom(__instance.CustomCharacterInfos, info => info.commonness, randSync).element;
            CharacterInfo characterInfo;
            if (characterElement == null)
            {
                string[] speciesList = { "human", "felinid" };
                // Вероятность появления фелинида составляет 15%
                int i = Rand.Value(randSync) <= 0.15f ? 1 : 0;

                characterInfo = new CharacterInfo(speciesList[i].ToIdentifier(), jobOrJobPrefab: __instance.GetJobPrefab(randSync), npcIdentifier: __instance.Identifier, randSync: randSync);
            }
            else
            {
                characterInfo = new CharacterInfo(characterElement, __instance.Identifier);
            }
            if (characterInfo.Job != null && !MathUtils.NearlyEqual(__instance.SkillMultiplier, 1.0f))
            {
                foreach (var skill in characterInfo.Job.GetSkills())
                {
                    float newSkill = skill.Level * __instance.SkillMultiplier;
                    skill.IncreaseSkill(newSkill - skill.Level, canIncreasePastDefaultMaximumSkill: false);
                }
                characterInfo.Salary = characterInfo.CalculateSalary(__instance.BaseSalary, __instance.SalaryMultiplier);
            }
            characterInfo.HumanPrefabIds = (__instance.NpcSetIdentifier, __instance.Identifier);
            characterInfo.GiveExperience(__instance.ExperiencePoints);

            __result = characterInfo;
            return false;
        }
    }
}