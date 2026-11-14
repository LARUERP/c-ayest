using FusionDarkRP.Integration;
using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppSLZ.Marrow.Data;
using Il2CppSLZ.Marrow.Interaction;
using Il2CppSLZ.Marrow.Warehouse;
using LabFusion.Entities;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.RPC;
using LabFusion.SDK.Metadata;
using LabFusion.Scene;
using UnityEngine;

namespace FusionDarkRP.Telephone;

public sealed class PhoneDevices
{
    private sealed class Device
    {
        public NetworkEntity Entity;
        public GameObject Object;
        public bool Pending;
        public float Started;
        public int Token;
        public string Error = "";
        public string Barcode = "";
        public PhoneDeviceInfo Placement;
        public float Recalled;
    }

    private readonly Dictionary<ulong, Device> devices =
        new Dictionary<ulong, Device>();

    public Action<PhoneDeviceInfo, GameObject> Parked;

    private int generation;
    private long placements;
    private string cachedBarcode = "";
    private Spawnable cachedSpawnable;

    public ushort? Id(ulong player)
    {
        Device value;

        return devices.TryGetValue(player, out value) &&
               (Object)(object)value.Object != (Object)null &&
               value.Object.activeInHierarchy &&
               value.Entity != null &&
               value.Entity.IsRegistered
            ? new ushort?(value.Entity.ID)
            : (ushort?)null;
    }

    public PhoneDeviceInfo[] Catalog(Func<ulong, bool> pin)
    {
        return devices.Keys
            .Where(p => Id(p).HasValue)
            .Select(p => Info(p, Id(p).Value, pin(p)))
            .ToArray();
    }

    private PhoneDeviceInfo Info(ulong owner, ushort id, bool pin)
    {
        PhoneDeviceInfo placement = devices[owner].Placement;

        return new PhoneDeviceInfo
        {
            Owner = owner,
            Device = id,
            PinSet = pin,
            Parked = placement?.Parked ?? false,
            Placement = placement?.Placement ?? 0,
            X = placement?.X ?? 0f,
            Y = placement?.Y ?? 0f,
            Z = placement?.Z ?? 0f,
            Yaw = placement?.Yaw ?? 0f
        };
    }

    public void Tick(Func<ulong, ushort, bool> held)
    {
        foreach (KeyValuePair<ulong, Device> device in devices)
        {
            Device value = device.Value;
            ushort? id = Id(device.Key);

            if (id.HasValue)
            {
                bool isHeld = held(device.Key, id.Value);

                if ((value.Placement?.Parked ?? false) && isHeld)
                    value.Placement.Parked = false;
            }
        }
    }

    private void Park(
        Device d,
        Vector3 position,
        float yaw,
        ulong owner,
        bool preserveRotation = false)
    {
        d.Placement = new PhoneDeviceInfo
        {
            Owner = owner,
            Device = d.Entity.ID,
            Parked = true,
            Placement = ++placements,
            X = position.x,
            Y = position.y,
            Z = position.z,
            Yaw = yaw
        };

        d.Object.transform.position = position;

        if (!preserveRotation)
            d.Object.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        foreach (Rigidbody body in
                 d.Object.GetComponentsInChildren<Rigidbody>())
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        Parked?.Invoke(d.Placement, d.Object);
    }

    public string Status(ulong player)
    {
        if (!devices.TryGetValue(player, out var value))
            return "En attente du téléphone.";

        if (value.Error != "")
            return value.Error;

        if (value.Pending)
            return "Téléphone en préparation…";

        if (!Id(player).HasValue)
            return "Recréer le téléphone depuis BoneMenu.";

        PhoneDeviceInfo placement = value.Placement;

        return placement != null && placement.Parked
            ? "Téléphone en attente de prise en main."
            : "Téléphone disponible.";
    }

    public void Remove(ulong player)
    {
        if (devices.TryGetValue(player, out var value))
        {
            value.Token++;
            Despawn(value.Entity);
            devices.Remove(player);
        }
    }

    public void Reset()
    {
        generation++;
        cachedSpawnable = null;
        cachedBarcode = "";

        foreach (Device value in devices.Values)
        {
            value.Token++;
            Despawn(value.Entity);
        }

        devices.Clear();
    }

    public void Ensure(
        PlayerID player,
        PhoneConfiguration config,
        bool replace = false,
        Func<ulong, ushort, bool> held = null)
    {
        if (!NetworkInfo.IsHost ||
            player == null ||
            !player.IsValid ||
            !NetworkSceneManager.InCurrentLevel(player))
        {
            return;
        }

        if (replace &&
            devices.TryGetValue(PlatformIds.Of(player), out var value))
        {
            if (value.Pending)
                throw new InvalidOperationException(
                    "Téléphone déjà en préparation.");

            if (Id(PlatformIds.Of(player)).HasValue &&
                value.Barcode == config.Barcode)
            {
                if (held != null &&
                    held(PlatformIds.Of(player), Id(PlatformIds.Of(player)).Value))
                {
                    throw new InvalidOperationException(
                        "Relâcher le téléphone avant de le rappeler.");
                }

                if (Time.realtimeSinceStartup - value.Recalled < 2f)
                {
                    throw new InvalidOperationException(
                        "Attendre avant de rappeler le téléphone.");
                }

                NetworkPlayer networkPlayer = default(NetworkPlayer);

                if (NetworkPlayerManager.TryGetPlayer(
                        player.SmallID,
                        out networkPlayer) &&
                    networkPlayer.HasRig &&
                    !((Object)(object)networkPlayer.RigRefs.Head ==
                      (Object)null))
                {
                    NetworkEntityManager.TakeOwnership(value.Entity);

                    Transform head = networkPlayer.RigRefs.Head;

                    Park(
                        value,
                        head.position +
                        head.forward * 0.45f +
                        Vector3.down * 0.35f,
                        head.eulerAngles.y,
                        PlatformIds.Of(player));

                    value.Recalled = Time.realtimeSinceStartup;
                    value.Error = "";
                }

                return;
            }

            Remove(PlatformIds.Of(player));
        }

        if (devices.TryGetValue(PlatformIds.Of(player), out var value2))
        {
            if (value2.Pending &&
                Time.realtimeSinceStartup - value2.Started > 30f)
            {
                value2.Token++;
                value2.Pending = false;
                value2.Error =
                    "Téléphone indisponible : vérifier le pallet puis recréer.";
            }
        }
        else
        {
            NetworkPlayer networkPlayer = default(NetworkPlayer);

            if (((MetadataVariableT<bool>)(object)player.Metadata.Loading)
                    .GetValue() ||
                !NetworkPlayerManager.TryGetPlayer(
                    player.SmallID,
                    out networkPlayer) ||
                !networkPlayer.HasRig ||
                (Object)(object)networkPlayer.RigRefs.Head == (Object)null)
            {
                return;
            }

            AssetWarehouse instance = AssetWarehouse.Instance;

            if (instance == null)
                return;

            if (cachedSpawnable == null ||
                cachedBarcode != config.Barcode)
            {
                SpawnableCrateReference crateReference =
                    new SpawnableCrateReference(config.Barcode);

                SpawnableCrate crate = default(SpawnableCrate);

                if (!((CrateReferenceT<SpawnableCrate>)(object)crateReference)
                        .TryGetCrate(out crate))
                {
                    devices[PlatformIds.Of(player)] = new Device
                    {
                        Error =
                            "Pallet Phone absent : l'installer puis recréer le téléphone."
                    };

                    return;
                }

                cachedBarcode = config.Barcode;

                cachedSpawnable = new Spawnable
                {
                    crateRef = crateReference
                };
            }

            Device d = new Device
            {
                Pending = true,
                Started = Time.realtimeSinceStartup,
                Barcode = config.Barcode
            };

            devices[PlatformIds.Of(player)] = d;

            int epoch = generation;
            int token = d.Token;

            Transform head = networkPlayer.RigRefs.Head;

            Vector3 spawnPosition =
                head.position +
                head.forward * 0.45f +
                Vector3.down * 0.35f;

            float spawnYaw = head.eulerAngles.y;

            NetworkAssetSpawner.Spawn(
                new NetworkAssetSpawner.SpawnRequestInfo
                {
                    Spawnable = cachedSpawnable,
                    Position = spawnPosition,
                    Rotation = Quaternion.Euler(0f, spawnYaw, 0f),
                    SpawnEffect = false,
                    SpawnSource = (EntitySource)1,

                    SpawnCallback =
                        delegate(NetworkAssetSpawner.SpawnCallbackInfo info)
                        {
                            if (generation != epoch ||
                                token != d.Token ||
                                !devices.TryGetValue(
                                    PlatformIds.Of(player),
                                    out var currentDevice) ||
                                currentDevice != d)
                            {
                                Despawn(info.Entity);
                                return;
                            }

                            d.Pending = false;
                            d.Entity = info.Entity;
                            d.Object = info.Spawned;

                            if (info.Entity != null &&
                                (Object)(object)info.Spawned != (Object)null)
                            {
                                Park(
                                    d,
                                    spawnPosition,
                                    spawnYaw,
                                    PlatformIds.Of(player));
                            }

                            if (info.Entity == null ||
                                (Object)(object)info.Spawned == (Object)null)
                            {
                                d.Error =
                                    "Échec du spawn : recréer le téléphone.";
                            }
                        }
                });
        }
    }

    public static GameObject Resolve(ushort id)
    {
        NetworkEntity entity =
            NetworkEntityManager.IDManager.RegisteredEntities.GetEntity(id);

        if (entity == null)
            return null;

        IMarrowEntityExtender extender =
            entity.GetExtender<IMarrowEntityExtender>();

        if (extender == null)
            return null;

        MarrowEntity marrowEntity = extender.MarrowEntity;

        return marrowEntity != null
            ? ((Component)marrowEntity).gameObject
            : null;
    }

    private static void Despawn(NetworkEntity entity)
    {
        if (entity != null &&
            entity.IsRegistered &&
            NetworkInfo.HasServer &&
            NetworkInfo.IsHost)
        {
            NetworkAssetSpawner.Despawn(
                new NetworkAssetSpawner.DespawnRequestInfo
                {
                    EntityID = entity.ID,
                    DespawnEffect = false
                });
        }
    }
}
