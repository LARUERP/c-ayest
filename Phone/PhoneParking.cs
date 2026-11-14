using System.Collections.Generic;
using System.Linq;
using BoneLib;
using Il2CppSLZ.Marrow;
using LabFusion.Entities;
using LabFusion.Player;
using UnityEngine;

namespace FusionDarkRP.Telephone;

public sealed class PhoneParking
{
    private sealed class State
    {
        public GameObject Body;

        public Rigidbody[] Bodies;

        public bool[] OriginalGravity;

        public long Revision;

        public bool Suspended;

        public bool Released;
    }

    private readonly Dictionary<ushort, State> states =
        new Dictionary<ushort, State>();

    private static bool Holds(Hand hand, GameObject body)
    {
        GameObject val = hand != null ? hand.m_CurrentAttachedGO : null;

        return (Object)(object)val != (Object)null &&
            (
                (Object)(object)val == (Object)(object)body ||
                val.transform.IsChildOf(body.transform) ||
                body.transform.IsChildOf(val.transform)
            );
    }

    private static bool AnyoneHolds(GameObject body)
    {
        if (Holds(Player.LeftHand, body) || Holds(Player.RightHand, body))
        {
            return true;
        }

        foreach (PlayerID playerID in PlayerIDManager.PlayerIDs)
        {
            if (playerID == null || !playerID.IsValid)
            {
                continue;
            }

            if (NetworkPlayerManager.TryGetPlayer(
                playerID.SmallID,
                out NetworkPlayer player
            ) && player.HasRig)
            {
                if (Holds(player.RigRefs.LeftHand, body) ||
                    Holds(player.RigRefs.RightHand, body))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void Release(State state)
    {
        if (!state.Suspended)
        {
            return;
        }

        for (int i = 0; i < state.Bodies.Length; i++)
        {
            if ((Object)(object)state.Bodies[i] != (Object)null)
            {
                state.Bodies[i].useGravity = state.OriginalGravity[i];
            }
        }

        state.Suspended = false;
    }

    public void Hold(PhoneDeviceInfo device, GameObject supplied = null)
    {
        if (!states.TryGetValue(device.Device, out State state))
        {
            if (!device.Parked)
            {
                return;
            }

            GameObject body = supplied ?? PhoneDevices.Resolve(device.Device);

            if ((Object)(object)body == (Object)null)
            {
                return;
            }

            var components = body.GetComponentsInChildren<Rigidbody>();

            state = new State
            {
                Body = body,
                Bodies = components.ToArray(),
                OriginalGravity = components
                    .Select(rb => rb.useGravity)
                    .ToArray(),
                Revision = device.Placement
            };

            states[device.Device] = state;
        }

        if ((Object)(object)state.Body == (Object)null)
        {
            states.Remove(device.Device);
            return;
        }

        if (state.Revision != device.Placement)
        {
            Release(state);

            state.Revision = device.Placement;
            state.Released = false;

            state.OriginalGravity = state.Bodies
                .Select(rb =>
                    (Object)(object)rb != (Object)null && rb.useGravity
                )
                .ToArray();
        }

        if (!device.Parked || AnyoneHolds(state.Body))
        {
            Release(state);
            state.Released = true;
        }
        else
        {
            if (state.Released)
            {
                return;
            }

            if (state.Suspended)
            {
                foreach (Rigidbody body in state.Bodies)
                {
                    if ((Object)(object)body != (Object)null)
                    {
                        body.velocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                    }
                }

                return;
            }

            foreach (Rigidbody body in state.Bodies)
            {
                if ((Object)(object)body != (Object)null)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.useGravity = false;
                }
            }

            state.Suspended = true;
        }
    }

    public void Tick(IEnumerable<PhoneDeviceInfo> catalog)
    {
        PhoneDeviceInfo[] devices = catalog.ToArray();

        HashSet<ushort> active = devices
            .Select(device => device.Device)
            .ToHashSet();

        ushort[] removed = states.Keys
            .Where(id => !active.Contains(id))
            .ToArray();

        foreach (ushort id in removed)
        {
            Release(states[id]);
            states.Remove(id);
        }

        foreach (PhoneDeviceInfo device in devices)
        {
            Hold(device);
        }
    }

    public void Clear()
    {
        foreach (State state in states.Values)
        {
            Release(state);
        }

        states.Clear();
    }
}
