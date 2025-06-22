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
    [Info("Realistic Death Physics", "VisEntities", "1.0.0")]
    [Description("Launches player corpses in the direction of the killing blow.")]
    public class RealisticDeathPhysics : RustPlugin
    {
        #region Fields

        private static RealisticDeathPhysics _plugin;
        private static Configuration _config;
        private readonly Dictionary<ulong, ImpactData> _pendingImpacts = new Dictionary<ulong, ImpactData>();

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Impact Force Multiplier")]
            public float ImpactForceMultiplier { get; set; }
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

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                ImpactForceMultiplier = 0.2f,
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
            if (victim == null || victim.IsNpc || deathInfo == null)
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

            var data = new ImpactData
            {
                Direction = direction,
                Force = magnitude * _config.ImpactForceMultiplier
            };

            _pendingImpacts[victim.userID] = data;
        }

        private void OnPlayerCorpseSpawned(BasePlayer player, PlayerCorpse corpse)
        {
            if (corpse == null || player == null)
                return;

            ImpactData data;
            if (!_pendingImpacts.TryGetValue(player.userID, out data))
                return;

            _pendingImpacts.Remove(player.userID);

            timer.Once(0f, delegate
            {
                ApplyForceToCorpse(corpse, data);
            });
        }

        #endregion Oxide Hooks

        #region Helpers

        private void ApplyForceToCorpse(PlayerCorpse corpse, ImpactData data)
        {
            if (corpse == null) return;

            Rigidbody rootBody = corpse.GetComponent<Rigidbody>();
            if (rootBody != null)
                rootBody.AddForce(data.Direction * data.Force, ForceMode.Impulse);

            var bodies = corpse.GetComponentsInChildren<Rigidbody>();
            foreach (var rb in bodies)
            {
                if (rb == null || rb == rootBody)
                    continue;

                rb.AddForce(data.Direction * data.Force, ForceMode.Impulse);
            }
        }

        private sealed class ImpactData
        {
            public Vector3 Direction;
            public float Force;
        }

        #endregion Helpers
    }
}