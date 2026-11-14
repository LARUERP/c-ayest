using FusionDarkRP.Integration;
using BoneLib;
using LabFusion.Audio;
using LabFusion.Player;
using LabFusion.Voice;
using LabFusion.Voice.Unity;
using UnityEngine;

namespace FusionDarkRP.Telephone;

public sealed class PhoneAmbientCapture
{
    private readonly PhoneAmbientBuffer buffer = new PhoneAmbientBuffer();

    private string scope = "";

    private string Scope(PhoneMod mod)
    {
        PhoneVoiceNotes notes = mod.Notes;
        return (notes != null && notes.Recording)
            ? ("note/" + mod.ScreenOwner + "/" + mod.Access.Token)
            : (mod.IsCasting
                ? ("live/" + mod.Live.Active.Id)
                : ((mod.CallActive && mod.View.Call.Speaker)
                    ? ("call/" + mod.View.Call.Id)
                    : ""));
    }

    private bool Nearby(PhoneMod mod, ulong actor)
    {
        PlayerID playerID = PlatformIds.GetPlayerID(actor);

        if (playerID == null || !playerID.IsValid || playerID.IsMe ||
            (mod.CallActive && mod.View.Call.Peer == actor) ||
            !PhoneMod.VoiceHead(playerID, out var head))
        {
            return false;
        }

        Vector3 position;

        if (mod.ScreenDevice.HasValue)
        {
            GameObject device = PhoneDevices.Resolve(mod.ScreenDevice.Value);

            if ((Object)(object)device == (Object)null)
            {
                return false;
            }

            position = device.transform.position;
        }
        else
        {
            if ((Object)(object)Player.Head == (Object)null)
            {
                return false;
            }

            position = Player.Head.position;
        }

        return PhoneMod.Within(head, position, 2f);
    }

    private bool Ready(PhoneMod mod)
    {
        string currentScope = Scope(mod);

        if (currentScope != scope)
        {
            scope = currentScope;
            buffer.Clear();
        }

        return currentScope != "" && !VoiceInfo.IsDeafened;
    }

    public void Receive(PhoneMod mod, UnityVoiceSpeaker speaker, byte[] data)
    {
        if (Ready(mod) &&
            ((VoiceSpeaker)speaker).ID != null &&
            Nearby(mod, PlatformIds.Of(((VoiceSpeaker)speaker).ID)) &&
            PhoneVoiceGuard.Valid(data))
        {
            buffer.Add(
                PlatformIds.Of(((VoiceSpeaker)speaker).ID),
                VoiceConverter.Decode(data),
                AudioInfo.OutputSampleRate,
                ((VoiceSpeaker)speaker).Volume * 0.5f,
                Time.realtimeSinceStartup
            );
        }
    }

    public short[] Mix(PhoneMod mod, short[] own, int rate)
    {
        return Ready(mod)
            ? buffer.Mix(
                own,
                rate,
                Time.realtimeSinceStartup,
                (ulong id) => Nearby(mod, id)
            )
            : own;
    }
}
