/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Realistic Death Physics", "VisEntities", "1.2.0")]
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

            [JsonProperty("Impact Force Multiplier")]
            public float ImpactForceMultiplier { get; set; }

            [JsonProperty("Include NPC Corpses")]
            public bool IncludeNPCCorpses { get; set; }

            [JsonProperty("Allowed Killer Weapon Shortnames (leave empty to allow all)")]
            public List<string> AllowedKillerWeaponShortnames { get; set; }
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

            if (string.Compare(_config.Version, "1.2.0") < 0)
                _config.AllowedKillerWeaponShortnames = defaultConfig.AllowedKillerWeaponShortnames;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                ImpactForceMultiplier = 0.2f,
                IncludeNPCCorpses = true,
                AllowedKillerWeaponShortnames = new List<string>()
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

            if (victim.IsNpc && !_config.IncludeNPCCorpses)
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

            var impactInfo = new ImpactInfo
            {
                Direction = direction,
                Force = magnitude * _config.ImpactForceMultiplier
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
            if (corpse == null || !_config.IncludeNPCCorpses)
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
            if (_config.AllowedKillerWeaponShortnames == null || _config.AllowedKillerWeaponShortnames.Count == 0)
                return true;

            string foundName = GetWeaponShortnameFromHitInfo(deathInfo);
            if (string.IsNullOrEmpty(foundName))
                return false;

            for (int i = 0; i < _config.AllowedKillerWeaponShortnames.Count; i++)
            {
                string allowed = _config.AllowedKillerWeaponShortnames[i];
                if (!string.IsNullOrEmpty(allowed) && string.Equals(allowed, foundName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
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