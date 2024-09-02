﻿using Aetherium.Utils;
using BepInEx.Configuration;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using R2API;
using RoR2;
using RoR2.Projectile;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

using static Aetherium.AetheriumPlugin;
using static Aetherium.Utils.ItemHelpers;
using static Aetherium.Utils.MathHelpers;

using System.Runtime.CompilerServices;

namespace Aetherium.Items.Tier3
{
    public class BlasterSword : ItemBase<BlasterSword>
    {
        public static ConfigOption<bool> UseAlternateModel;
        public static ConfigOption<bool> EnableParticleEffects;
        public static ConfigOption<bool> UseImpaleProjectile;
        public static ConfigOption<float> BaseSwordDamageMultiplier;
        public static ConfigOption<float> AdditionalSwordDamageMultiplier;
        public static ConfigOption<bool> HomingProjectiles;
        public static ConfigOption<bool> AnyBarrierGrantsActivationBuff;

        public override string ItemName => "Blaster Sword";
        public override string ItemLangTokenName => "BLASTER_SWORD";
        public override string ItemPickupDesc => $"When your health bar is full <style=cStack>(from health, shields, barrier, or a combination of any)</style>, most attacks will <style=cIsDamage>fire out a sword beam that {(UseImpaleProjectile ? "impales an enemy, crippling them, and exploding shortly after." : "explodes and cripples an enemy on impact.")}</style>";

        public override string ItemFullDescription => $"When your health bar is greater than or equal to 100% full <style=cStack>(from health, shields, barrier, or a combination of any)</style>, most attacks will <style=cIsDamage>fire out a sword beam</style> that has <style=cIsDamage>{FloatToPercentageString(BaseSwordDamageMultiplier)} of your damage</style> <style=cStack>(+{FloatToPercentageString(AdditionalSwordDamageMultiplier)} per stack)</style> " +
            $"when it <style=cIsDamage>{(UseImpaleProjectile ? "explodes after having impaled an enemy for a short duration." : "explodes on contact with an enemy.")}</style>";

        public override string ItemLore => "<style=cMono>. . . . . . . . . .</style>\n" +
            "\n<style=cMono>THEY</style> have chosen to <style=cMono>LISTEN</style> to our words.\n" +
            "\n<style=cMono>WE</style> have chosen to <style=cMono>GRANT</style> upon you an exceptional <style=cMono>WEAPON</style> to <style=cMono>UTILIZE</style> your <style=cMono>SOULS TRUE STRENGTH</style>.\n" +
            "\nThe weapon will <style=cMono>ADAPT</style> to fit the needs of the <style=cMono>WIELDER</style>. Once wielded, it is no different than their very soul.\n" +
            "\nShould the <style=cMono>WIELDER</style> survive their journey, they <style=cMono>WILL</style> discard the frail form of what they once were and <style=cMono>ASCEND</style>.\n" +
            "\n<style=cMono>. . . . . . . . . .</style>";

        public override ItemTier Tier => ItemTier.Tier3;
        public override ItemTag[] ItemTags => new ItemTag[] { ItemTag.Damage };

        public override GameObject ItemModel => UseAlternateModel ? MainAssets.LoadAsset<GameObject>("PickupBlasterSwordAlt.prefab") : MainAssets.LoadAsset<GameObject>("PickupBlasterSword.prefab");
        public override Sprite ItemIcon => UseAlternateModel ? MainAssets.LoadAsset<Sprite>("BlasterKatanaIcon.png") : MainAssets.LoadAsset<Sprite>("BlasterSwordIcon.png");

        public static GameObject ItemBodyModelPrefab;
        public static GameObject SwordProjectile;

        public bool RecursionPrevention;

        public static HashSet<string> BlacklistedProjectiles = new HashSet<string>()
        {
            "LightningStake",
            "StickyBomb",
            "FireworkProjectile",
            "NailBombProjectile",

        };

        public static HashSet<string> BlacklistedBulletAttackWeapons = new HashSet<string>()
        {
            "NailBombProjectile"
        };

        public static RoR2.BuffDef BlasterSwordActiveBuff;

        //Provided as a courtesy for other modders.
        public delegate void BlasterSwordActivationHandler(FireProjectileInfo fireProjectileInfo);
        public static event BlasterSwordActivationHandler onBlasterSwordFired;


        public override void Init(ConfigFile config)
        {
            CreateConfig(config);
            CreateLang();
            //CreateAchievement();
            CreateBuff();

            CreateProjectile();
            CreateItem();
            Hooks();
        }

        private void CreateConfig(ConfigFile config)
        {
            UseAlternateModel = config.ActiveBind<bool>("Item: " + ItemName, "Use Alternate Blaster Sword Model", false, "Do you wish to start feeling motivated now?");
            EnableParticleEffects = config.ActiveBind<bool>("Item: " + ItemName, "Enable Particle Effects", true, "Should the particle effects for the models be enabled?");
            UseImpaleProjectile = config.ActiveBind<bool>("Item: " + ItemName, "Use Impale Projectile Variant?", true, "Should the swords impale and stick to targets (true), or pierce and explode on world collision (false)?");
            BaseSwordDamageMultiplier = config.ActiveBind<float>("Item: " + ItemName, "Base Damage Inheritance Multiplier", 2f, "In percentage, how much of the wielder's damage should we have for the sword projectile? (2 = 200%)");
            AdditionalSwordDamageMultiplier = config.ActiveBind<float>("Item: " + ItemName, "Damage Multiplier Gained per Additional Stacks", 0.5f, "In percentage, how much of the wielder's damage should we add per additional stack? (0.5 = 50%)");
            HomingProjectiles = config.ActiveBind<bool>("Item: " + ItemName, "Homing Projectiles", false, "Should the Blaster Sword projectiles home in on enemies?");
            AnyBarrierGrantsActivationBuff = config.ActiveBind<bool>("Item: " + ItemName, "Any Barrier Grants Blaster Sword Activation Buff", false, "Should having any amount of barrier allow you to use the effect of Blaster Sword?");
        }

        /*private void CreateAchievement()
        {
            LanguageAPI.Add("AETHERIUM_" + ItemLangTokenName + "_ACHIEVEMENT_NAME", "Sword from the Stone");
            LanguageAPI.Add("AETHERIUM_" + ItemLangTokenName + "_ACHIEVEMENT_DESC", "High up in the Abyssal Depths, find the legendary sword stuck in the stone and prove you possess the knightly valor required to wield it.");
            LanguageAPI.Add("AETHERIUM_" + ItemLangTokenName + "_UNLOCKABLE_NAME", "Sword from the Stone");

            UnlockablesAPI.AddUnlockable<BlasterSwordAchievement>(true);
        }*/

        private void CreateBuff()
        {
            BlasterSwordActiveBuff = ScriptableObject.CreateInstance<RoR2.BuffDef>();
            BlasterSwordActiveBuff.name = "Aetherium: Blaster Sword Active";
            BlasterSwordActiveBuff.buffColor = Color.white;
            BlasterSwordActiveBuff.canStack = false;
            BlasterSwordActiveBuff.isDebuff = false;
            BlasterSwordActiveBuff.iconSprite = MainAssets.LoadAsset<Sprite>("BlasterSwordBuffIcon.png");

            ContentAddition.AddBuffDef(BlasterSwordActiveBuff);

        }

        private void CreateProjectile()
        {
            SwordProjectile = UseImpaleProjectile ? PrefabAPI.InstantiateClone(LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/Thermite"), "SwordProjectile", true) : PrefabAPI.InstantiateClone(LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/FMJ"), "SwordProjectile", true);

            var model = UseAlternateModel ? MainAssets.LoadAsset<GameObject>("BlasterSwordAltProjectile.prefab") : MainAssets.LoadAsset<GameObject>("BlasterSwordProjectile.prefab");
            model.AddComponent<NetworkIdentity>();
            model.AddComponent<RoR2.Projectile.ProjectileGhostController>();

            var controller = SwordProjectile.GetComponent<RoR2.Projectile.ProjectileController>();
            controller.procCoefficient = 0.5f;
            controller.ghostPrefab = model;

            SwordProjectile.GetComponent<RoR2.TeamFilter>().teamIndex = TeamIndex.Player;

            var damage = SwordProjectile.GetComponent<RoR2.Projectile.ProjectileDamage>();
            damage.damageType = DamageType.CrippleOnHit;
            damage.damage = 0;

            var intervalController = SwordProjectile.GetComponent<ProjectileIntervalOverlapAttack>();
            UnityEngine.Object.Destroy(intervalController);

            var impactEffect = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/ImpactEffects/VagrantCannonExplosion");

            if (UseImpaleProjectile)
            {
                var impactExplosion = SwordProjectile.GetComponent<RoR2.Projectile.ProjectileImpactExplosion>();
                impactExplosion.impactEffect = impactEffect;
                impactExplosion.blastRadius = 2;
                impactExplosion.blastProcCoefficient = 0f;
                impactExplosion.lifetimeAfterImpact = 1.5f;
                impactExplosion.timerAfterImpact = true;
                impactExplosion.blastDamageCoefficient = 1;

                var stickOnImpact = SwordProjectile.GetComponent<RoR2.Projectile.ProjectileStickOnImpact>();
                stickOnImpact.alignNormals = false;
            }
            else
            {
                var overlapAttack = SwordProjectile.GetComponent<ProjectileOverlapAttack>();
                overlapAttack.impactEffect = impactEffect;

                var applyTorqueOnStart = SwordProjectile.AddComponent<RoR2.ApplyTorqueOnStart>();
                applyTorqueOnStart.localTorque = new Vector3(0, 1500, 0);
            }

            if (HomingProjectiles)
            {
                var projectileTarget = SwordProjectile.AddComponent<ProjectileTargetComponent>();

                var projectileDirectionalTargetFinder = SwordProjectile.AddComponent<ProjectileDirectionalTargetFinder>();
                projectileDirectionalTargetFinder.lookRange = 25;
                projectileDirectionalTargetFinder.lookCone = 20;
                projectileDirectionalTargetFinder.targetSearchInterval = 0.1f;
                projectileDirectionalTargetFinder.onlySearchIfNoTarget = true;
                projectileDirectionalTargetFinder.allowTargetLoss = false;
                projectileDirectionalTargetFinder.testLoS = false;
                projectileDirectionalTargetFinder.ignoreAir = false;
                projectileDirectionalTargetFinder.flierAltitudeTolerance = float.PositiveInfinity;
                projectileDirectionalTargetFinder.targetComponent = projectileTarget;

                var projectileHoming = SwordProjectile.AddComponent<ProjectileSteerTowardTarget>();
                projectileHoming.targetComponent = projectileTarget;
                projectileHoming.rotationSpeed = 90;
                projectileHoming.yAxisOnly = false;

                var projectileSimple = SwordProjectile.GetComponent<ProjectileSimple>();
                projectileSimple.enableVelocityOverLifetime = true;
                projectileSimple.updateAfterFiring = true;
                projectileSimple.velocityOverLifetime = new AnimationCurve(new Keyframe[] { new Keyframe(0, 0), new Keyframe(2, 70) });
            }

            // register it for networking
            if (SwordProjectile) PrefabAPI.RegisterNetworkPrefab(SwordProjectile);
            
            // add it to the projectile catalog or it won't work in multiplayer
            ContentAddition.AddProjectile(SwordProjectile);
        }

        public override ItemDisplayRuleDict CreateItemDisplayRules()
        {
            ItemBodyModelPrefab = UseAlternateModel ? MainAssets.LoadAsset<GameObject>("BlasterSwordAlt.prefab") : MainAssets.LoadAsset<GameObject>("BlasterSword.prefab");

            var itemDisplay = ItemBodyModelPrefab.AddComponent<RoR2.ItemDisplay>();
            itemDisplay.rendererInfos = ItemDisplaySetup(ItemBodyModelPrefab);

            if (EnableParticleEffects) { itemDisplay.gameObject.AddComponent<SwordGlowHandler>(); }

            ItemDisplayRuleDict rulesNormal = new ItemDisplayRuleDict();
            rulesNormal.Add("mdlCommandoDualies", new RoR2.ItemDisplayRule[]
            {
               new RoR2.ItemDisplayRule
               {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleLeft",
                    localPos = new Vector3(0, 0, 0.4f),
                    localAngles = new Vector3(-90, 0, 0),
                    localScale = new Vector3(0.1f, 0.1f, 0.1f)
                },

                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleRight",
                    localPos = new Vector3(0, 0, 0.4f),
                    localAngles = new Vector3(-90, 0, 0),
                    localScale = new Vector3(0.1f, 0.1f, 0.1f)
                }
            });
            rulesNormal.Add("mdlHuntress", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "Arrow",
                    localPos = new Vector3(0.35491F, 0F, -0.01299F),
                    localAngles = new Vector3(90F, 270F, 0F),
                    localScale = new Vector3(0.0436F, 0.0356F, 0.05146F)
                }
            });
            rulesNormal.Add("mdlToolbot", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleNailgun",
                    localPos = new Vector3(-2.62379F, 0.84595F, 1.41736F),
                    localAngles = new Vector3(60F, 0.8F, 270F),
                    localScale = new Vector3(1F, 1F, 1F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleNailgun",
                    localPos = new Vector3(-2.58983F, 0.77911F, -1.3535F),
                    localAngles = new Vector3(300F, 0F, 270F),
                    localScale = new Vector3(1F, 1F, 1F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleNailgun",
                    localPos = new Vector3(-2.60018F, -1.62175F, -0.00003F),
                    localAngles = new Vector3(0F, 0F, 270F),
                    localScale = new Vector3(1F, 1F, 1F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleSpear",
                    localPos = new Vector3(0f, 5.9f, 0f),
                    localAngles = new Vector3(0f, 0f, 180f),
                    localScale = new Vector3(1.5f, 1.5f, 1.5f)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleBuzzsaw",
                    localPos = new Vector3(0f, 1f, 1f),
                    localAngles = new Vector3(0f, 0f, 180f),
                    localScale = new Vector3(2f, 1f, 1.5f)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleBuzzsaw",
                    localPos = new Vector3(0f, 1f, -1f),
                    localAngles = new Vector3(0f, 0f, 180f),
                    localScale = new Vector3(2f, 1f, 1.5f)
                }
            });
            rulesNormal.Add("mdlEngi", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "CannonHeadL",
                    localPos = new Vector3(0f, 1.04975f, 0f),
                    localAngles = new Vector3(-180f, 45f, 0f),
                    localScale = new Vector3(0.15f, 0.15f, 0.15f)
                },

                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "CannonHeadR",
                    localPos = new Vector3(0f, 1.04975f, 0f),
                    localAngles = new Vector3(-180f, -45f, 0f),
                    localScale = new Vector3(0.15f, 0.15f, 0.15f)
                }
            });
            rulesNormal.Add("mdlMage", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "UpperArmL",
                    localPos = new Vector3(0.1f, 0.2f, 0),
                    localAngles = new Vector3(0f, 90f, 190f),
                    localScale = new Vector3(0.07f, 0.025f, 0.07f)
                },

                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "UpperArmR",
                    localPos = new Vector3(-0.1f, 0.2f, 0),
                    localAngles = new Vector3(0f, -90f, -190f),
                    localScale = new Vector3(0.07f, 0.025f, 0.07f)
                }
            });
            rulesNormal.Add("mdlMerc", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "HandR",
                    localPos = new Vector3(0.52334F, 0.27322F, 0.02221F),
                    localAngles = new Vector3(0F, 0F, 99.99999F),
                    localScale = new Vector3(0.11F, 0.11F, 0.11F)
                }
            });
            rulesNormal.Add("mdlTreebot", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "FlowerBase",
                    localPos = new Vector3(-0.3f, 1.6f, 0f),
                    localAngles = new Vector3(0f, 0f, 15f),
                    localScale = new Vector3(0.3f, 0.3f, 0.3f)
                }
            });
            rulesNormal.Add("mdlLoader", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MechHandL",
                    localPos = new Vector3(0.6f, 0.25f, 0.02f),
                    localAngles = new Vector3(20f, -4f, 90f),
                    localScale = new Vector3(0.15f, 0.1f, 0.15f)
                },

                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MechHandR",
                    localPos = new Vector3(-0.6f, 0.25f, 0.02f),
                    localAngles = new Vector3(20f, 4f, -90f),
                    localScale = new Vector3(0.15f, 0.1f, 0.15f)
                }
            });
            rulesNormal.Add("mdlCroco", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MouthMuzzle",
                    localPos = new Vector3(-8.05507F, 2.0004F, 2.99987F),
                    localAngles = new Vector3(73.66542F, 0F, 270F),
                    localScale = new Vector3(1.5F, 1.60597F, 1.5F)
                }
            });
            rulesNormal.Add("mdlCaptain", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleGun",
                    localPos = new Vector3(-0.00001F, 0F, 0.6046F),
                    localAngles = new Vector3(270F, 0F, 0F),
                    localScale = new Vector3(0.12F, 0.12F, 0.12F)
                }
            });
            rulesNormal.Add("mdlBrother", new RoR2.ItemDisplayRule[]
            {
               new RoR2.ItemDisplayRule
               {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleLeft",
                    localPos = new Vector3(0, 0, 0f),
                    localAngles = new Vector3(0, 0, 0),
                    localScale = new Vector3(0f, 0f, 0f)
                },

                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleRight",
                    localPos = new Vector3(0, 0, 0f),
                    localAngles = new Vector3(0, 0, 0),
                    localScale = new Vector3(0f, 0f, 0f)
                },

                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "chest",
                    localPos = new Vector3(0, 0.15f, -0.1f),
                    localAngles = new Vector3(90, 0, 0),
                    localScale = new Vector3(0.1f, 0.1f, 0.1f)
                }
            });
            rulesNormal.Add("mdlBandit2", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MainWeapon",
                    localPos = new Vector3(-0.0182F, 1.0776F, -0.0001F),
                    localAngles = new Vector3(0F, 180F, 180F),
                    localScale = new Vector3(0.0386F, 0.0312F, 0.05F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "SideWeapon",
                    localPos = new Vector3(0F, -0.3997F, 0.0889F),
                    localAngles = new Vector3(0F, 180F, 0F),
                    localScale = new Vector3(0.015F, 0.0179F, 0.0179F)
                }
            });
            rulesNormal.Add("CHEF", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "Cleaver",
                    localPos = new Vector3(-0.00341F, 0.03122F, 0F),
                    localAngles = new Vector3(0F, 0F, 185.6668F),
                    localScale = new Vector3(0.01453F, 0.00627F, 0.01F)
                }
            });
            rulesNormal.Add("RobPaladinBody", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "HandL",
                    localPos = new Vector3(-0.0074F, 0.27164F, 0.75613F),
                    localAngles = new Vector3(22.33475F, 277.4532F, 100.4245F),
                    localScale = new Vector3(0.15F, 0.15F, 0.15F)
                }
            });
            rulesNormal.Add("RedMistBody", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "HandR",
                    localPos = new Vector3(0.00002F, 0.03892F, 0F),
                    localAngles = new Vector3(0F, 74.50383F, 0F),
                    localScale = new Vector3(0.00416F, 0.00416F, 0.00416F)
                }
            });
            rulesNormal.Add("ArbiterBody", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "HandR",
                    localPos = new Vector3(0.07474F, 0.09314F, -0.30959F),
                    localAngles = new Vector3(346.5526F, 77.80733F, 87.9118F),
                    localScale = new Vector3(0.05F, 0.05974F, 0.05F)
                }
            });
            rulesNormal.Add("EnforcerBody", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName =  "ShotgunItems",
                    localPos =   new Vector3(1.14057F, -0.01415F, 0.05927F),
                    localAngles =new Vector3(0F, 0F, 90F),
                    localScale = new Vector3(0.07379F, 0.07379F, 0.07379F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName =  "SSGBarrelItems",
                    localPos =   new Vector3(-0.00527F, 1.13731F, 0.0219F),
                    localAngles =new Vector3(0F, 90F, 180F),
                    localScale = new Vector3(0.07379F, 0.07379F, 0.07379F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName =  "HMGItems",
                    localPos =   new Vector3(1.07765F, -0.01496F, 0.05527F), 
                    localAngles =new Vector3(0F, 0F, 90F),
                    localScale = new Vector3(0.06306F, 0.07538F, 0.06306F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName =  "HammerHeadFront",
                    localPos =   new Vector3(0.00013F, 0.01796F, -0.38599F), 
                    localAngles =new Vector3(0F, 90F, 90F),
                    localScale = new Vector3(0.14598F, 0.13813F, 0.14598F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "NeedlerItems",
                    localPos = new Vector3(0.43976F, -0.17905F, -0.03793F),
                    localAngles = new Vector3(3.90256F, 4.49459F, 90.19315F),
                    localScale = new Vector3(0.14598F, 0.13813F, 0.14598F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "NeedlerItems",
                    localPos = new Vector3(0.44118F, 0.32155F, -0.00379F),
                    localAngles = new Vector3(3.90256F, 4.4946F, 87.44079F),
                    localScale = new Vector3(0.14598F, 0.13813F, 0.14598F)
                },
            });
            rulesNormal.Add("NemesisEnforcerBody", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "Hammer",
                    localPos = new Vector3(0F, -0.05731F, 0F),
                    localAngles = new Vector3(0F, 0F, 0F),
                    localScale = new Vector3(0.00262F, 0.00157F, 0.00262F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MinigunMuzzle",
                    localPos = new Vector3(0F, 0F, 0.00747F),
                    localAngles = new Vector3(270F, 0F, 0F),
                    localScale = new Vector3(0.00262F, 0.00157F, 0.00262F)
                }
            });

            ItemDisplayRuleDict rulesAlt = new ItemDisplayRuleDict();
            rulesAlt.Add("mdlCommandoDualies", new RoR2.ItemDisplayRule[]
            {
               new RoR2.ItemDisplayRule
               {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleLeft",
                    localPos = new Vector3(0, 0, 0.4f),
                    localAngles = new Vector3(-90, 0, 0),
                    localScale = new Vector3(0.1f, 0.1f, 0.1f)
                },

                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleRight",
                    localPos = new Vector3(0, 0, 0.4f),
                    localAngles = new Vector3(-90, 0, 0),
                    localScale = new Vector3(0.1f, 0.1f, 0.1f)
                }
            });
            rulesAlt.Add("mdlHuntress", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "Arrow",
                    localPos = new Vector3(0.3f, 0f, 0f),
                    localAngles = new Vector3(90f, 270f, 0f),
                    localScale = new Vector3(0.08f, 0.045f, 0.1f)
                }
            });
            rulesAlt.Add("mdlToolbot", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleNailgun",
                    localPos = new Vector3(-2.6f, 0.8f, 1.3f),
                    localAngles = new Vector3(60f, 0.8f, -90f),
                    localScale = new Vector3(1f, 1f, 1f)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleNailgun",
                    localPos = new Vector3(-2.6f, 0.8f, -1.3f),
                    localAngles = new Vector3(-60f, 0f, -90f),
                    localScale = new Vector3(1f, 1f, 1f)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleNailgun",
                    localPos = new Vector3(-2.6f, -1.5f, 0f),
                    localAngles = new Vector3(180f, 0f, -90f),
                    localScale = new Vector3(1f, 1f, 1f)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleSpear",
                    localPos = new Vector3(0f, 5.9f, 0f),
                    localAngles = new Vector3(0f, 0f, 180f),
                    localScale = new Vector3(1.5f, 1.5f, 1.5f)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleBuzzsaw",
                    localPos = new Vector3(0f, 1f, 1f),
                    localAngles = new Vector3(0f, 0f, 180f),
                    localScale = new Vector3(2f, 1f, 1.5f)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleBuzzsaw",
                    localPos = new Vector3(0f, 1f, -1f),
                    localAngles = new Vector3(0f, 0f, 180f),
                    localScale = new Vector3(2f, 1f, 1.5f)
                }
            });
            rulesAlt.Add("mdlEngi", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "CannonHeadL",
                    localPos = new Vector3(0F, 1.2F, 0F),
                    localAngles = new Vector3(180F, 45F, 0F),
                    localScale = new Vector3(0.15F, 0.15F, 0.15F)
                },

                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "CannonHeadR",
                    localPos = new Vector3(0f, 1.2f, 0f),
                    localAngles = new Vector3(-180f, -45f, 0f),
                    localScale = new Vector3(0.15f, 0.15f, 0.15f)
                }
            });
            rulesAlt.Add("mdlMage", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "UpperArmL",
                    localPos = new Vector3(0.1f, 0.2f, 0),
                    localAngles = new Vector3(0f, 90f, 190f),
                    localScale = new Vector3(0.07f, 0.025f, 0.07f)
                },

                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "UpperArmR",
                    localPos = new Vector3(-0.1F, 0.2F, 0F),
                    localAngles = new Vector3(0F, 90F, 190F),
                    localScale = new Vector3(0.07F, 0.025F, 0.07F)
                }
            });
            rulesAlt.Add("mdlMerc", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "HandR",
                    localPos = new Vector3(-0.5643F, -0.5281F, 0.0101F),
                    localAngles = new Vector3(346.9501F, 343.9273F, 321.9142F),
                    localScale = new Vector3(0.1357F, 0.1357F, 0.1357F)
                }
            });
            rulesAlt.Add("mdlTreebot", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "FlowerBase",
                    localPos = new Vector3(-0.3f, 1.6f, 0f),
                    localAngles = new Vector3(0f, 0f, 15f),
                    localScale = new Vector3(0.3f, 0.3f, 0.3f)
                }
            });
            rulesAlt.Add("mdlLoader", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MechHandL",
                    localPos = new Vector3(0.5997F, 0.2481F, 0.0293F),
                    localAngles = new Vector3(347.6521F, 176F, 271.5197F),
                    localScale = new Vector3(0.15F, 0.1F, 0.15F)
                },

                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MechHandR",
                    localPos = new Vector3(-0.5576F, 0.224F, 0.1835F),
                    localAngles = new Vector3(19.4644F, 17.8299F, 274.6895F),
                    localScale = new Vector3(0.15F, 0.1F, 0.15F)
                }
            });
            rulesAlt.Add("mdlCroco", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MouthMuzzle",
                    localPos = new Vector3(-9.2f, 2f, 3f),
                    localAngles = new Vector3(90f, 90f, 0f),
                    localScale = new Vector3(1.5f, 1.5f, 1.5f)
                }
            });
            rulesAlt.Add("mdlCaptain", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleGun",
                    localPos = new Vector3(-0.0002F, -0.0003F, 0.5533F),
                    localAngles = new Vector3(270F, 0F, 0F),
                    localScale = new Vector3(0.12F, 0.0916F, 0.12F)
                }
            });
            rulesAlt.Add("mdlBrother", new RoR2.ItemDisplayRule[]
            {
               new RoR2.ItemDisplayRule
               {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleLeft",
                    localPos = new Vector3(0, 0, 0f),
                    localAngles = new Vector3(0, 0, 0),
                    localScale = new Vector3(0f, 0f, 0f)
                },

                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MuzzleRight",
                    localPos = new Vector3(0, 0, 0f),
                    localAngles = new Vector3(0, 0, 0),
                    localScale = new Vector3(0f, 0f, 0f)
                },

                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "chest",
                    localPos = new Vector3(0, 0.15f, -0.1f),
                    localAngles = new Vector3(90, 0, 0),
                    localScale = new Vector3(0.1f, 0.1f, 0.1f)
                }
            });
            rulesAlt.Add("mdlBandit2", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MainWeapon",
                    localPos = new Vector3(-0.0113F, 1.1339F, 0.0013F),
                    localAngles = new Vector3(0.0624F, 5.493F, 179.5722F),
                    localScale = new Vector3(0.05F, 0.0353F, 0.0537F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "SideWeapon",
                    localPos = new Vector3(0F, -0.3728F, 0.0889F),
                    localAngles = new Vector3(0F, 300F, 0F),
                    localScale = new Vector3(0.0179F, 0.0115F, 0.0179F)
                }
            });
            rulesAlt.Add("CHEF", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "Cleaver",
                    localPos = new Vector3(-0.00341F, 0.03122F, 0F),
                    localAngles = new Vector3(0F, 0F, 185.6668F),
                    localScale = new Vector3(0.01453F, 0.00627F, 0.01F)
                }
            });
            rulesAlt.Add("RobPaladinBody", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "HandL",
                    localPos = new Vector3(-0.0074F, 0.27164F, 0.75613F),
                    localAngles = new Vector3(22.33475F, 277.4532F, 100.4245F),
                    localScale = new Vector3(0.15F, 0.15F, 0.15F)
                }
            });
            rulesAlt.Add("RedMistBody", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "HandR",
                    localPos = new Vector3(0.00002F, 0.03892F, 0F),
                    localAngles = new Vector3(0F, 74.50383F, 0F),
                    localScale = new Vector3(0.00416F, 0.00416F, 0.00416F)
                }
            });
            rulesAlt.Add("ArbiterBody", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "HandR",
                    localPos = new Vector3(0.07474F, 0.09314F, -0.30959F),
                    localAngles = new Vector3(346.5526F, 77.80733F, 87.9118F),
                    localScale = new Vector3(0.05F, 0.05974F, 0.05F)
                }
            });
            rulesAlt.Add("EnforcerBody", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName =  "ShotgunItems",
                    localPos =   new Vector3(1.14057F, -0.01415F, 0.05927F),
                    localAngles =new Vector3(0F, 0F, 90F),
                    localScale = new Vector3(0.07379F, 0.07379F, 0.07379F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName =  "SSGBarrelItems",
                    localPos =   new Vector3(-0.00527F, 1.13731F, 0.0219F),
                    localAngles =new Vector3(0F, 90F, 180F),
                    localScale = new Vector3(0.07379F, 0.07379F, 0.07379F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName =  "HMGItems",
                    localPos =   new Vector3(1.07765F, -0.01496F, 0.05527F),
                    localAngles =new Vector3(0F, 0F, 90F),
                    localScale = new Vector3(0.06306F, 0.07538F, 0.06306F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName =  "HammerHeadFront",
                    localPos =   new Vector3(0.00013F, 0.01796F, -0.38599F),
                    localAngles =new Vector3(0F, 90F, 90F),
                    localScale = new Vector3(0.14598F, 0.13813F, 0.14598F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "NeedlerItems",
                    localPos = new Vector3(0.43976F, -0.17905F, -0.03793F),
                    localAngles = new Vector3(3.90256F, 4.49459F, 90.19315F),
                    localScale = new Vector3(0.14598F, 0.13813F, 0.14598F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "NeedlerItems",
                    localPos = new Vector3(0.44118F, 0.32155F, -0.00379F),
                    localAngles = new Vector3(3.90256F, 4.4946F, 87.44079F),
                    localScale = new Vector3(0.14598F, 0.13813F, 0.14598F)
                },
            });
            rulesAlt.Add("NemesisEnforcerBody", new RoR2.ItemDisplayRule[]
            {
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "Hammer",
                    localPos = new Vector3(0F, -0.05731F, 0F),
                    localAngles = new Vector3(0F, 0F, 0F),
                    localScale = new Vector3(0.00262F, 0.00157F, 0.00262F)
                },
                new RoR2.ItemDisplayRule
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = ItemBodyModelPrefab,
                    childName = "MinigunMuzzle",
                    localPos = new Vector3(0F, 0F, 0.00747F),
                    localAngles = new Vector3(270F, 0F, 0F),
                    localScale = new Vector3(0.00262F, 0.00157F, 0.00262F)
                }
            });
            return UseAlternateModel ? rulesAlt : rulesNormal;
        }

        public override void Hooks()
        {
            On.RoR2.CharacterBody.FixedUpdate += ApplyBuffAsIndicatorForReady;
            IL.EntityStates.Merc.Evis.FixedUpdate += Anime;
            IL.EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile.RootPulse += FireSwordsFromFlower;
            On.RoR2.Orbs.GenericDamageOrb.Begin += FireSwordOnOrbs;
            On.RoR2.OverlapAttack.Fire += FireSwordOnMelee;
            On.RoR2.BulletAttack.Fire += FireTheSwordOnBulletAttack;
            On.RoR2.Projectile.ProjectileManager.FireProjectile_FireProjectileInfo += FireTheSwordOnProjectiles;
        }

        private void FireSwordsFromFlower(ILContext il)
        {
            var c = new ILCursor(il);

            int damageInfoIndex = 15;
            c.GotoNext(x => x.MatchNewobj<RoR2.DamageInfo>());
            c.GotoNext(x => x.MatchStloc(out damageInfoIndex));
            c.GotoNext(MoveType.After, x => x.MatchLdfld<RoR2.HurtBox>("hurtBoxGroup"));
            c.Emit(OpCodes.Ldloc, damageInfoIndex);
            c.EmitDelegate<Action<RoR2.DamageInfo>>((damageInfo) =>
            {
                if (damageInfo.attacker)
                {
                    var body = damageInfo.attacker.GetComponent<RoR2.CharacterBody>();
                    if (body)
                    {
                        var InventoryCount = GetCount(body);
                        if (InventoryCount > 0)
                        {
                            if (body.HasBuff(BlasterSwordActiveBuff))
                            {
                                var swordsPerFlower = (int)body.attackSpeed * 2;
                                for (int i = 1; i <= swordsPerFlower; i++)
                                {
                                    var newProjectileInfo = new FireProjectileInfo
                                    {
                                        owner = body.gameObject,
                                        projectilePrefab = SwordProjectile,
                                        speedOverride = 150.0f,
                                        damage = body.damage * (BaseSwordDamageMultiplier + (AdditionalSwordDamageMultiplier * (InventoryCount - 1))),
                                        damageTypeOverride = null,
                                        damageColorIndex = DamageColorIndex.Default,
                                        procChainMask = default
                                    };
                                    var theta = (Math.PI * 2) / swordsPerFlower;
                                    var angle = theta * i;
                                    var radius = 3;
                                    var positionChosen = new Vector3((float)(radius * Math.Cos(angle) + damageInfo.position.x), damageInfo.position.y + 3, (float)(radius * Math.Sin(angle) + damageInfo.position.z));
                                    newProjectileInfo.position = positionChosen;
                                    newProjectileInfo.rotation = RoR2.Util.QuaternionSafeLookRotation(damageInfo.position - positionChosen);

                                    onBlasterSwordFired?.Invoke(newProjectileInfo);

                                    try
                                    {
                                        RecursionPrevention = true;
                                        RoR2.Projectile.ProjectileManager.instance.FireProjectile(newProjectileInfo);
                                    }
                                    finally
                                    {
                                        RecursionPrevention = false;
                                    }
                                }
                            }
                        }
                    }
                }
            });
        }

        private void Anime(ILContext il)
        {
            var c = new ILCursor(il);

            int damageInfoIndex = 4;
            c.GotoNext(x => x.MatchNewobj<RoR2.DamageInfo>(), x => x.MatchStloc(out damageInfoIndex));
            c.GotoNext(MoveType.After, x => x.MatchCallOrCallvirt<RoR2.GlobalEventManager>("OnHitAll"));
            c.Emit(OpCodes.Ldloc, damageInfoIndex);
            c.EmitDelegate<Action<RoR2.DamageInfo>>((damageInfo) =>
            {
                if (damageInfo.attacker)
                {
                    var body = damageInfo.attacker.GetComponent<RoR2.CharacterBody>();
                    if (body)
                    {
                        var InventoryCount = GetCount(body);
                        if (InventoryCount > 0)
                        {
                            if (body.HasBuff(BlasterSwordActiveBuff))
                            {
                                var newProjectileInfo = new FireProjectileInfo
                                {
                                    owner = body.gameObject,
                                    projectilePrefab = SwordProjectile,
                                    speedOverride = 100.0f,
                                    damage = body.damage * (BaseSwordDamageMultiplier + (AdditionalSwordDamageMultiplier * (InventoryCount - 1))),
                                    damageTypeOverride = null,
                                    damageColorIndex = DamageColorIndex.Default,
                                    procChainMask = default
                                };
                                var positionChosen = damageInfo.position + new Vector3(RoR2.Run.instance.stageRng.RangeFloat(-10, 10), RoR2.Run.instance.stageRng.RangeFloat(0, 10), RoR2.Run.instance.stageRng.RangeFloat(-10, 10)).normalized * 4;
                                newProjectileInfo.position = positionChosen;
                                newProjectileInfo.rotation = RoR2.Util.QuaternionSafeLookRotation(damageInfo.position - positionChosen);
                                onBlasterSwordFired?.Invoke(newProjectileInfo);

                                try
                                {
                                    RecursionPrevention = true;
                                    RoR2.Projectile.ProjectileManager.instance.FireProjectile(newProjectileInfo);
                                }
                                finally
                                {
                                    RecursionPrevention = false;
                                }
                            }
                        }
                    }
                }
            });
        }

        private void ApplyBuffAsIndicatorForReady(On.RoR2.CharacterBody.orig_FixedUpdate orig, RoR2.CharacterBody self)
        {
            var InventoryCount = GetCount(self);
            if (InventoryCount > 0)
            {
                if (self.healthComponent.combinedHealthFraction >= 1 && !self.HasBuff(BlasterSwordActiveBuff) || AnyBarrierGrantsActivationBuff && self.healthComponent.barrier > 0 && !self.HasBuff(BlasterSwordActiveBuff))
                {
                    self.AddBuff(BlasterSwordActiveBuff);
                }
                if (self.healthComponent.combinedHealthFraction < 1 && self.HasBuff(BlasterSwordActiveBuff) || AnyBarrierGrantsActivationBuff && self.healthComponent.barrier <= 0 && !self.HasBuff(BlasterSwordActiveBuff))
                {
                    self.RemoveBuff(BlasterSwordActiveBuff);
                }
            }
            else
            {
                if (self.HasBuff(BlasterSwordActiveBuff))
                {
                    self.RemoveBuff(BlasterSwordActiveBuff);
                }
            }
            orig(self);
        }

        private void FireSwordOnOrbs(On.RoR2.Orbs.GenericDamageOrb.orig_Begin orig, RoR2.Orbs.GenericDamageOrb self)
        {
            var owner = self.attacker;
            if (owner)
            {
                var ownerBody = owner.GetComponent<RoR2.CharacterBody>();
                if (ownerBody)
                {
                    var InventoryCount = GetCount(ownerBody);
                    if (InventoryCount > 0)
                    {
                        if (ownerBody.HasBuff(BlasterSwordActiveBuff))
                        {
                            var newProjectileInfo = new FireProjectileInfo
                            {
                                owner = owner,
                                projectilePrefab = SwordProjectile,
                                speedOverride = 100.0f,
                                damage = ownerBody.damage * (BaseSwordDamageMultiplier + (AdditionalSwordDamageMultiplier * (InventoryCount - 1))),
                                damageTypeOverride = null,
                                damageColorIndex = DamageColorIndex.Default,
                                procChainMask = default,
                                position = self.origin,
                                rotation = RoR2.Util.QuaternionSafeLookRotation(self.target.transform.position - self.origin)
                            };

                            onBlasterSwordFired?.Invoke(newProjectileInfo);

                            try
                            {
                                RecursionPrevention = true;
                                RoR2.Projectile.ProjectileManager.instance.FireProjectile(newProjectileInfo);
                            }
                            finally
                            {
                                RecursionPrevention = false;
                            }
                        }
                    }
                }
            }
            orig(self);
        }

        /*private bool FireSwordOnMelee(On.RoR2.OverlapAttack.orig_Fire orig, RoR2.OverlapAttack self, List<RoR2.HealthComponent> hitResults)
        {
            var owner = self.inflictor;
            if (owner)
            {
                var body = owner.GetComponent<RoR2.CharacterBody>();
                if (body)
                {
                    var InventoryCount = GetCount(body);
                    if (InventoryCount > 0)
                    {
                        if (body.HasBuff(BlasterSwordActiveBuff))
                        {
                            Vector3 HitPositionSums = Vector3.zero;
                            if (self.overlapList.Count > 0)
                            {
                                for (int i = 0; i < self.overlapList.Count; i++)
                                {
                                    HitPositionSums += self.overlapList[i].hitPosition;
                                }

                                HitPositionSums /= self.overlapList.Count;
                            }
                            else
                            {
                                HitPositionSums += body.corePosition;
                            }
                            var inputBank = body.inputBank;

                            var cooldownHandler = owner.GetComponent<SwordCooldownHandlerIDunno>();
                            if (!cooldownHandler) { cooldownHandler = owner.AddComponent<SwordCooldownHandlerIDunno>(); }

                            if (!cooldownHandler.MeleeTracker.ContainsKey(self))
                            {
                                cooldownHandler.MeleeTracker.Add(self, 0);
                                var newProjectileInfo = new FireProjectileInfo
                                {
                                    owner = self.inflictor,
                                    projectilePrefab = SwordProjectile,
                                    speedOverride = 100.0f,
                                    damage = body.damage * BaseSwordDamageMultiplier + (body.damage * AdditionalSwordDamageMultiplier * (InventoryCount - 1)),
                                    damageTypeOverride = null,
                                    damageColorIndex = DamageColorIndex.Default,
                                    procChainMask = default,
                                    position = HitPositionSums,
                                    rotation = RoR2.Util.QuaternionSafeLookRotation(inputBank ? inputBank.aimDirection : body.transform.forward)
                                };

                                try
                                {
                                    RecursionPrevention = true;
                                    RoR2.Projectile.ProjectileManager.instance.FireProjectile(newProjectileInfo);
                                }
                                finally
                                {
                                    RecursionPrevention = false;
                                }
                            }
                        }
                    }
                }
            }
            return orig(self, hitResults);
        }
        */

        private bool FireSwordOnMelee(On.RoR2.OverlapAttack.orig_Fire orig, RoR2.OverlapAttack self, List<RoR2.HurtBox> hitResults)
        {
            var owner = self.inflictor;
            if (owner)
            {
                var body = owner.GetComponent<RoR2.CharacterBody>();
                if (body)
                {
                    var InventoryCount = GetCount(body);
                    if (InventoryCount > 0)
                    {
                        if (body.HasBuff(BlasterSwordActiveBuff))
                        {
                            Vector3 HitPositionSums = Vector3.zero;
                            if (self.overlapList.Count > 0)
                            {
                                for (int i = 0; i < self.overlapList.Count; i++)
                                {
                                    HitPositionSums += self.overlapList[i].hitPosition;
                                }

                                HitPositionSums /= self.overlapList.Count;
                            }
                            else
                            {
                                HitPositionSums += body.corePosition;
                            }
                            var inputBank = body.inputBank;

                            var cooldownHandler = owner.GetComponent<SwordCooldownHandlerIDunno>();
                            if (!cooldownHandler) { cooldownHandler = owner.AddComponent<SwordCooldownHandlerIDunno>(); }

                            if (!cooldownHandler.MeleeTracker.ContainsKey(self))
                            {
                                cooldownHandler.MeleeTracker.Add(self, 0);
                                var newProjectileInfo = new FireProjectileInfo
                                {
                                    owner = self.inflictor,
                                    projectilePrefab = SwordProjectile,
                                    speedOverride = 100.0f,
                                    damage = body.damage * (BaseSwordDamageMultiplier + (AdditionalSwordDamageMultiplier * (InventoryCount - 1))),
                                    damageTypeOverride = null,
                                    damageColorIndex = DamageColorIndex.Default,
                                    procChainMask = default,
                                    position = HitPositionSums,
                                    rotation = RoR2.Util.QuaternionSafeLookRotation(inputBank ? inputBank.aimDirection : body.transform.forward)
                                };

                                onBlasterSwordFired?.Invoke(newProjectileInfo);

                                try
                                {
                                    RecursionPrevention = true;
                                    RoR2.Projectile.ProjectileManager.instance.FireProjectile(newProjectileInfo);
                                }
                                finally
                                {
                                    RecursionPrevention = false;
                                }
                            }
                        }
                    }
                }
            }
            return orig(self, hitResults);
        }

        private void FireTheSwordOnBulletAttack(On.RoR2.BulletAttack.orig_Fire orig, RoR2.BulletAttack self)
        {
            if(self != null && self.weapon && BlacklistedBulletAttackWeapons.Contains(self.weapon.name.Replace("(Clone)", "")))
            {
                orig(self);
                return;
            }

            var projectileOwner = self.owner;
            if (projectileOwner)
            {
                var projectileBody = projectileOwner.GetComponent<RoR2.CharacterBody>();
                if (projectileBody)
                {
                    var InventoryCount = GetCount(projectileBody);
                    if (InventoryCount > 0)
                    {
                        if (projectileBody.HasBuff(BlasterSwordActiveBuff))
                        {
                            var newProjectileInfo = new FireProjectileInfo
                            {
                                owner = projectileOwner,
                                projectilePrefab = SwordProjectile,
                                speedOverride = 100.0f,
                                damage = projectileBody.damage * (BaseSwordDamageMultiplier + (AdditionalSwordDamageMultiplier * (InventoryCount - 1))),
                                damageTypeOverride = null,
                                damageColorIndex = DamageColorIndex.Default,
                                procChainMask = default
                            };

                            Vector3 MuzzleTransform = self.origin;
                            var weapon = self.weapon;
                            if (weapon)
                            {
                                var weaponModelLocator = weapon.GetComponent<RoR2.ModelLocator>();
                                if (weaponModelLocator && weaponModelLocator.transform)
                                {
                                    ChildLocator childLocator = weaponModelLocator.modelTransform.GetComponent<ChildLocator>();
                                    if (childLocator)
                                    {
                                        if (!string.IsNullOrWhiteSpace(self.muzzleName))
                                        {
                                            var child = childLocator.FindChild(self.muzzleName);
                                            if (child)
                                            {
                                                MuzzleTransform = child.position;
                                            }
                                        }
                                    }
                                }
                            }
                            newProjectileInfo.position = MuzzleTransform;
                            newProjectileInfo.rotation = RoR2.Util.QuaternionSafeLookRotation(self.aimVector);

                            onBlasterSwordFired?.Invoke(newProjectileInfo);

                            try
                            {
                                RecursionPrevention = true;
                                RoR2.Projectile.ProjectileManager.instance.FireProjectile(newProjectileInfo);
                            }
                            finally
                            {
                                RecursionPrevention = false;
                            }
                        }
                    }
                }
            }
            orig(self);
        }

        private void FireTheSwordOnProjectiles(On.RoR2.Projectile.ProjectileManager.orig_FireProjectile_FireProjectileInfo orig, RoR2.Projectile.ProjectileManager self, FireProjectileInfo fireProjectileInfo)
        {
            if (!RecursionPrevention && !BlacklistedProjectiles.Contains(fireProjectileInfo.projectilePrefab.name))
            {
                var projectileOwner = fireProjectileInfo.owner;
                if (projectileOwner)
                {
                    var body = projectileOwner.GetComponent<RoR2.CharacterBody>();
                    if (body)
                    {
                        var InventoryCount = GetCount(body);
                        if (InventoryCount > 0)
                        {
                            if (body.HasBuff(BlasterSwordActiveBuff))
                            {
                                var newProjectileInfo = fireProjectileInfo;
                                newProjectileInfo.owner = projectileOwner;
                                newProjectileInfo.projectilePrefab = SwordProjectile;
                                newProjectileInfo.speedOverride = 100.0f;
                                newProjectileInfo.damage = body.damage * (BaseSwordDamageMultiplier + (AdditionalSwordDamageMultiplier * (InventoryCount - 1)));
                                newProjectileInfo.damageTypeOverride = null;
                                newProjectileInfo.damageColorIndex = DamageColorIndex.Default;
                                newProjectileInfo.procChainMask = default;

                                onBlasterSwordFired?.Invoke(newProjectileInfo);

                                try
                                {
                                    RecursionPrevention = true;
                                    RoR2.Projectile.ProjectileManager.instance.FireProjectile(newProjectileInfo);
                                }
                                finally
                                {
                                    RecursionPrevention = false;
                                }
                            }
                        }
                    }
                }
            }
            orig(self, fireProjectileInfo);
        }

        public class SwordCooldownHandlerIDunno : MonoBehaviour
        {
            public Dictionary<RoR2.OverlapAttack, float> MeleeTracker = new Dictionary<RoR2.OverlapAttack, float>();

            public void FixedUpdate()
            {
                foreach (RoR2.OverlapAttack attack in MeleeTracker.Keys.ToList())
                {
                    var time = MeleeTracker[attack];
                    time += Time.fixedDeltaTime;

                    if (time > 5)
                    {
                        MeleeTracker.Remove(attack);
                    }
                    else
                    {
                        MeleeTracker[attack] = time;
                    }
                }
            }
        }

        public class SwordGlowHandler : NetworkBehaviour
        {
            public RoR2.ItemDisplay ItemDisplay;
            public List<ParticleSystemHandler> ParticleSystems = new List<ParticleSystemHandler>();
            public Renderer Renderer;

            [SyncVar]
            public float GlowCounter;

            public RoR2.CharacterMaster OwnerMaster;
            public RoR2.CharacterBody OwnerBody;

            public class ParticleSystemHandler
            {
                public ParticleSystem ParticleSystem;
                public bool HasPlayed;

                public ParticleSystemHandler(ParticleSystem particleSystem, bool hasPlayed)
                {
                    ParticleSystem = particleSystem;
                    HasPlayed = hasPlayed;
                }
            }

            public void SetupFields()
            {
                ItemDisplay = GetComponentInParent<RoR2.ItemDisplay>();
                if (ItemDisplay)
                {
                    var tempParticleSystems = GetComponentsInChildren<ParticleSystem>();

                    foreach(ParticleSystem particleSystem in tempParticleSystems)
                    {
                        ParticleSystems.Add(new ParticleSystemHandler(particleSystem, false));
                    }

                    var characterModel = ItemDisplay.GetComponentInParent<RoR2.CharacterModel>();

                    if (characterModel)
                    {
                        var body = characterModel.body;
                        if (body)
                        {
                            OwnerMaster = body.master;
                        }
                    }

                    Renderer = GetComponentsInChildren<Renderer>().Where(x => x.gameObject.name.Contains("Blade") && (x.material.name.Contains("BlasterSwordBladeMain") || x.material.name.Contains("BlasterSwordAltBladeSharp"))).FirstOrDefault();
                }
            }

            public void OnDestroy()
            {
                if (ParticleSystems.Any())
                {
                    foreach (ParticleSystemHandler particleSystemHandler in ParticleSystems)
                    {
                        UnityEngine.Object.Destroy(particleSystemHandler.ParticleSystem);
                    }
                }
            }

            public void FixedUpdate()
            {

                if (!OwnerMaster || !ItemDisplay || !ParticleSystems.Any() || !Renderer)
                {
                    SetupFields();
                }

                if (OwnerMaster && !OwnerBody)
                {
                    var body = OwnerMaster.GetBody();
                    if (body)
                    {
                        OwnerBody = body;
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(this);
                    }
                }

                if (OwnerBody && ParticleSystems.Any())
                {
                    foreach(ParticleSystemHandler particleSystemHandler in ParticleSystems)
                    {
                        var particleSystem = particleSystemHandler.ParticleSystem;

                        if (particleSystem)
                        {
                            if (OwnerBody.HasBuff(BlasterSwordActiveBuff))
                            {
                                if (!particleSystem.isPlaying && !particleSystemHandler.HasPlayed && ItemDisplay.visibilityLevel != VisibilityLevel.Invisible)
                                {
                                    particleSystem.Play();
                                    particleSystemHandler.HasPlayed = true;
                                }
                                else
                                {
                                    if (particleSystem.isPlaying && ItemDisplay.visibilityLevel == VisibilityLevel.Invisible)
                                    {
                                        particleSystem.Stop();
                                        particleSystem.Clear();
                                    }
                                }
                            }
                            else
                            {
                                if (particleSystem.isPlaying)
                                {
                                    particleSystem.Stop();
                                }
                                particleSystemHandler.HasPlayed = false;
                            }
                        }
                    }
                }

                if(OwnerBody && Renderer != null)
                {
                    if (OwnerBody.HasBuff(BlasterSwordActiveBuff))
                    {
                        GlowCounter += Time.fixedDeltaTime * 8;
                    }
                    else
                    {
                        GlowCounter -= Time.fixedDeltaTime * 8;
                    }
                    GlowCounter = Mathf.Clamp(GlowCounter, 0, Renderer.materials[0].name.Contains("BlasterSwordBladeMain") ? 6 : 5);

                    Renderer.materials[0].SetFloat("_EmPower", GlowCounter);
                }

            }
        }
    }
}