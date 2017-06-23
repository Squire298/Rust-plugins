using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Facepunch;
using System;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using System.Reflection;
using System.Collections;
using System.IO;
using Rust;

namespace Oxide.Plugins {
    [Info("NukePlayer", "Oxy", 1.0)]
    [Description("Nuke players.")]

    class NukePlayer : RustPlugin {
      private static readonly int playerLayer = LayerMask.GetMask("Player (Server)");
      private static readonly Collider[] colBuffer = (Collider[])typeof(Vis).GetField("colBuffer", (BindingFlags.Static | BindingFlags.NonPublic))?.GetValue(null);

      private List<ZoneList> RadiationZones = new List<ZoneList>();

      void Loaded() {
        permission.RegisterPermission("nukeplayer.use", this);
      }

      #region Core
      [ChatCommand("nuke")]
      void chatNuke(BasePlayer player, string command, string[] args) {
        if (permission.UserHasPermission(player.UserIDString, "nukeplayer.use")) {
          var foundPlayer = rust.FindPlayer(args[0]);
          if (foundPlayer == null) {
            PrintToChat(player, $"We couldn't find a player named {args[0]}");
            return;
          } else {
			DoNuke(foundPlayer);
          }
        }
      }

      void DoNuke(BasePlayer player) {
        System.Random r = new System.Random();
        int intensityValue = r.Next(800, 1500);
        int durationValue = r.Next(50, 150);
        int radiusValue = r.Next(164, 165);
        int timeLeft = 0;

		string intensityMsg = $"<color=#e67e22>{intensityValue.ToString()}</color> <color=#f1c40f>kiloton nuke incoming on {player.displayName}</color>";
               
        // Let everyone know doom is coming.
        ConsoleNetwork.BroadcastToAllClients("chat.add", new object[] { null, intensityMsg});

         timer.Once(timeLeft, () => {
          // Do damage and add radiation
          Detonate(player);
          // Set off effects around the player
          NukeEffects(player);
        });
      }
      #endregion Core
    
      #region Effects
      private void Detonate(BasePlayer player) {
        Rust.DamageTypeEntry dmg = new Rust.DamageTypeEntry();
        dmg.amount = 70;
        dmg.type = Rust.DamageType.Generic;
        
        HitInfo hitInfo = new HitInfo() {
            Initiator = null,
            WeaponPrefab = null
        };

        hitInfo.damageTypes.Add(new List<Rust.DamageTypeEntry> { dmg });
        DamageUtil.RadiusDamage(null, null, player.transform.position, 5, 10, new List<Rust.DamageTypeEntry> { dmg }, 133376, true);

        // Init Radiation on the players location
        InitializeZone(player.transform.position, 10, 150, 60, false);
      }

      private void NukeEffects(BasePlayer player) {
        System.Random r = new System.Random();
        // How many explosions within radius to set off
        int explosionCount = 35;
        int explosionRadius = 15;



        for (int i = 1; i <= explosionCount; i++) {
          Vector3 plusOffset = new Vector3(r.Next(1, explosionRadius), r.Next(1, explosionRadius), r.Next(1, explosionRadius));
          Vector3 minusOffset = new Vector3(r.Next(1, explosionRadius), r.Next(1, explosionRadius), r.Next(1, explosionRadius));
          Vector3 explosionLocation = player.transform.position + plusOffset - minusOffset;

          string[] prefabEffects = new string[12] {
            "assets/bundled/prefabs/fx/fire/fire_v2.prefab",
            "assets/bundled/prefabs/fx/ricochet/ricochet1.prefab",
            "assets/bundled/prefabs/fx/ricochet/ricochet2.prefab",
            "assets/bundled/prefabs/fx/ricochet/ricochet3.prefab",
            "assets/bundled/prefabs/fx/ricochet/ricochet4.prefab",
            "assets/prefabs/tools/c4/effects/c4_explosion.prefab",
            "assets/bundled/prefabs/fx/weapons/landmine/landmine_explosion.prefab",
            "assets/prefabs/npc/patrol helicopter/effects/rocket_explosion.prefab",
            "assets/bundled/prefabs/fx/explosions/explosion_01.prefab",
            "assets/bundled/prefabs/fx/explosions/explosion_02.prefab",
            "assets/bundled/prefabs/fx/explosions/explosion_03.prefab",
            "assets/bundled/prefabs/fx/explosions/explosion_core.prefab"
          };

          foreach (string prefabEffect in prefabEffects){
            Effect.server.Run(prefabEffect, explosionLocation, Vector3.up);
          }
        }
      }
      #endregion Effetcs

      // Credits to k1lly0u for code within this region
      #region Radiation Control
      private void InitializeZone(Vector3 Location, float intensity, float duration, float radius, bool explosionType = false) {
        if (!ConVar.Server.radiation)
            ConVar.Server.radiation = true;
        if (explosionType) Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", Location);
        else Effect.server.Run("assets/prefabs/npc/patrol helicopter/effects/rocket_explosion.prefab", Location);

        var newZone = new GameObject().AddComponent<RadZones>();
        newZone.Activate(Location, radius, intensity);

        var listEntry = new ZoneList { zone = newZone };
        listEntry.time = timer.Once(duration, () => DestroyZone(listEntry));

        RadiationZones.Add(listEntry);
      }
      private void DestroyZone(ZoneList zone){
        if (RadiationZones.Contains(zone)) {
            var index = RadiationZones.FindIndex(a => a.zone == zone.zone);
            RadiationZones[index].time.Destroy();
            UnityEngine.Object.Destroy(RadiationZones[index].zone);
            RadiationZones.Remove(zone);
        }            
      }
      public class ZoneList {
        public RadZones zone;
        public Timer time;
      }

      public class RadZones : MonoBehaviour {
        private int ID;
        private Vector3 Position;
        private float ZoneRadius;
        private float RadiationAmount;

        private List<BasePlayer> InZone;

        private void Awake() {
          gameObject.layer = (int)Layer.Reserved1;
          gameObject.name = "NukeZone";

          var rigidbody = gameObject.AddComponent<Rigidbody>();
          rigidbody.useGravity = false;
          rigidbody.isKinematic = true;
        }

        public void Activate(Vector3 pos, float radius, float amount) {
          ID = UnityEngine.Random.Range(0, 999999999);
          Position = pos;
          ZoneRadius = radius;
          RadiationAmount = amount;

          gameObject.name = $"RadZone {ID}";
          transform.position = Position;
          transform.rotation = new Quaternion();
          UpdateCollider();
          gameObject.SetActive(true);
          enabled = true;

          var Rads = gameObject.GetComponent<TriggerRadiation>();
          Rads = Rads ?? gameObject.AddComponent<TriggerRadiation>();
          Rads.RadiationAmountOverride = RadiationAmount;
          Rads.radiationSize = ZoneRadius;
          Rads.interestLayers = playerLayer;
          Rads.enabled = true;

          if (IsInvoking("UpdateTrigger")) CancelInvoke("UpdateTrigger");
          InvokeRepeating("UpdateTrigger", 5f, 5f);
        }

        private void OnDestroy() {
          CancelInvoke("UpdateTrigger");
          Destroy(gameObject);
        }

        private void UpdateCollider() {
          var sphereCollider = gameObject.GetComponent<SphereCollider>();
          {
            if (sphereCollider == null) {
              sphereCollider = gameObject.AddComponent<SphereCollider>();
              sphereCollider.isTrigger = true;
            }
            sphereCollider.radius = ZoneRadius;
          }
        }
        private void UpdateTrigger()
        {
          InZone = new List<BasePlayer>();
          int entities = Physics.OverlapSphereNonAlloc(Position, ZoneRadius, colBuffer, playerLayer);
          for (var i = 0; i < entities; i++) {
            var player = colBuffer[i].GetComponentInParent<BasePlayer>();
            if (player != null)
                InZone.Add(player);
          }
        }
      }
      #endregion
    }
};
