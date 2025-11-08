/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Realistic Death Physics", "VisEntities", "1.3.0")]
    [Description("Launches player corpses in the direction of the killing blow.")]
    public class RealisticDeathPhysics : RustPlugin
    {
        #region Fields

        private static RealisticDeathPhysics _plugin;
        private static Configuration _config;
        private readonly Dictionary<ulong, ImpactInfo> _pendingImpacts = new Dictionary<ulong, ImpactInfo>();

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Default Launch Strength")]
            public float DefaultLaunchStrength { get; set; }

            [JsonProperty("Affect NPC Corpses")]
            public bool AffectNPCCorpses { get; set; }

            [JsonProperty("Enable For Unlisted Weapons")]
            public bool EnableForUnlistedWeapons { get; set; }

            [JsonProperty("Weapon Overrides")]
            public Dictionary<string, WeaponOverrideConfig> WeaponOverrides { get; set; }
        }

        private class WeaponOverrideConfig
        {
            [JsonProperty("Enabled")]
            public bool Enabled { get; set; }

            [JsonProperty("Launch Strength Override")]
            public float LaunchStrengthOverride { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            if (string.Compare(_config.Version, "1.3.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                DefaultLaunchStrength = 0.2f,
                AffectNPCCorpses = true,
                EnableForUnlistedWeapons = true,
                WeaponOverrides = new Dictionary<string, WeaponOverrideConfig>
                {
                    ["rifle.ak"] = new WeaponOverrideConfig
                    {
                        Enabled = true,
                        LaunchStrengthOverride = 0.25f
                    },
                    ["rocket_basic"] = new WeaponOverrideConfig
                    {
                        Enabled = true,
                        LaunchStrengthOverride = 0.60f
                    },
                    ["shotgun.spas.12"] = new WeaponOverrideConfig
                    {
                        Enabled = true,
                        LaunchStrengthOverride = 0.30f
                    },
                    ["rifle.semiauto"] = new WeaponOverrideConfig
                    {
                        Enabled = true,
                        LaunchStrengthOverride = 0.22f
                    },
                    ["crossbow"] = new WeaponOverrideConfig
                    {
                        Enabled = true,
                        LaunchStrengthOverride = 0.16f
                    },
                    ["smg.mp5"] = new WeaponOverrideConfig
                    {
                        Enabled = true,
                        LaunchStrengthOverride = 0.21f
                    }
                }
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
        }

        private void Unload()
        {
            _pendingImpacts.Clear();
            _config = null;
            _plugin = null;
        }

        private void OnEntityDeath(BasePlayer victim, HitInfo deathInfo)
        {
            if (victim == null || deathInfo == null)
                return;

            if (victim.IsNpc && !_config.AffectNPCCorpses)
                return;

            if (!CanWeaponTriggerLaunch(deathInfo))
                return;

            Vector3 direction = Vector3.zero;
            if (deathInfo.ProjectileVelocity != Vector3.zero)
            {
                direction = deathInfo.ProjectileVelocity.normalized;
            }
            else if (deathInfo.InitiatorPlayer != null)
            {
                direction = (victim.transform.position - deathInfo.InitiatorPlayer.transform.position).normalized;
            }

            if (direction == Vector3.zero)
                return;

            float magnitude = deathInfo.ProjectileVelocity.magnitude;
            if (magnitude <= 0f)
                magnitude = 50f;

            float multiplier = GetLaunchForceMultiplierForWeapon(deathInfo);

            var impactInfo = new ImpactInfo
            {
                Direction = direction,
                Force = magnitude * multiplier
            };

            _pendingImpacts[victim.userID] = impactInfo;
        }

        private void OnPlayerCorpseSpawned(BasePlayer player, PlayerCorpse corpse)
        {
            if (corpse == null || player == null)
                return;

            ImpactInfo impactInfo;
            if (!_pendingImpacts.TryGetValue(player.userID, out impactInfo))
                return;

            _pendingImpacts.Remove(player.userID);

            timer.Once(0f, delegate
            {
                ApplyForceToCorpse(corpse, impactInfo);
            });
        }

        private void OnEntitySpawned(NPCPlayerCorpse corpse)
        {
            if (corpse == null || !_config.AffectNPCCorpses)
                return;

            ImpactInfo impactInfo;
            if (!_pendingImpacts.TryGetValue(corpse.playerSteamID, out impactInfo))
                return;

            _pendingImpacts.Remove(corpse.playerSteamID);

            timer.Once(0f, delegate
            {
                ApplyForceToCorpse(corpse, impactInfo);
            });
        }

        #endregion Oxide Hooks

        #region Helpers

        private string GetWeaponShortnameFromHitInfo(HitInfo deathInfo)
        {
            if (deathInfo == null)
                return null;

            if (deathInfo.Weapon != null)
            {
                Item item = deathInfo.Weapon.GetItem();
                if (item != null && item.info != null && !string.IsNullOrEmpty(item.info.shortname))
                    return item.info.shortname;
            }

            string prefabName = null;
            if (deathInfo.WeaponPrefab != null && !string.IsNullOrEmpty(deathInfo.WeaponPrefab.name))
                prefabName = deathInfo.WeaponPrefab.name;

            if (!string.IsNullOrEmpty(prefabName))
            {
                int lastSlash = prefabName.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash + 1 < prefabName.Length)
                    prefabName = prefabName.Substring(lastSlash + 1);

                if (prefabName.EndsWith(".prefab"))
                    prefabName = prefabName.Substring(0, prefabName.Length - ".prefab".Length);

                return prefabName;
            }

            if (deathInfo.Weapon != null)
            {
                string shortPrefab = deathInfo.Weapon.ShortPrefabName;
                if (!string.IsNullOrEmpty(shortPrefab))
                    return shortPrefab;
            }

            return null;
        }

        private bool CanWeaponTriggerLaunch(HitInfo deathInfo)
        {
            string shortname = GetWeaponShortnameFromHitInfo(deathInfo);

            if (string.IsNullOrEmpty(shortname))
                return _config.EnableForUnlistedWeapons;

            if (_config.WeaponOverrides != null && _config.WeaponOverrides.Count > 0)
            {
                WeaponOverrideConfig weaponOverride;
                if (_config.WeaponOverrides.TryGetValue(shortname, out weaponOverride))
                    return weaponOverride != null && weaponOverride.Enabled;
            }

            return _config.EnableForUnlistedWeapons;
        }

        private float GetLaunchForceMultiplierForWeapon(HitInfo deathInfo)
        {
            float multiplier = _config.DefaultLaunchStrength;

            if (_config.WeaponOverrides == null || _config.WeaponOverrides.Count == 0)
                return multiplier;

            string shortname = GetWeaponShortnameFromHitInfo(deathInfo);
            if (string.IsNullOrEmpty(shortname))
                return multiplier;
            
            WeaponOverrideConfig weaponOverride;
            if (_config.WeaponOverrides.TryGetValue(shortname, out weaponOverride))
            {
                if (weaponOverride != null && weaponOverride.Enabled)
                {
                    if (weaponOverride.LaunchStrengthOverride > 0f)
                        multiplier = weaponOverride.LaunchStrengthOverride;
                }
            }

            return multiplier;
        }

        private void ApplyForceToCorpse(BaseCorpse corpse, ImpactInfo impactInfo)
        {
            if (corpse == null)
                return;

            Rigidbody rootBody = corpse.GetComponent<Rigidbody>();
            if (rootBody != null)
                rootBody.AddForce(impactInfo.Direction * impactInfo.Force, ForceMode.Impulse);

            var bodies = corpse.GetComponentsInChildren<Rigidbody>();
            foreach (var rb in bodies)
            {
                if (rb == null || rb == rootBody)
                    continue;

                rb.AddForce(impactInfo.Direction * impactInfo.Force, ForceMode.Impulse);
            }
        }

        private sealed class ImpactInfo
        {
            public Vector3 Direction;
            public float Force;
        }

        #endregion Helpers
    }
}