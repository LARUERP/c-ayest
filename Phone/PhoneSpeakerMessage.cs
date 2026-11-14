
using System;
using LabFusion.Network;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public sealed class PhoneSpeakerMessage : ModuleMessageHandler
{
    public override ExpectedReceiverType ExpectedReceiver => (ExpectedReceiverType)2;

    protected override void OnHandleMessage(ReceivedMessage message)
    {
        if (NetworkInfo.IsHost ||
            message.IsServerHandled ||
            message.Sender != 0 ||
            message.Bytes.Length > 50000)
        {
            return;
        }

        try
        {
            PhoneSpeakerPacket phoneSpeakerPacket =
                message.ReadData<PhoneSpeakerPacket>();

            if (phoneSpeakerPacket.Read(
                out var token,
                out var device,
                out var call,
                out var actor,
                out var sequence,
                out var audio))
            {
                PhoneMod.Instance?.EnqueueSpeaker(phoneSpeakerPacket);
            }
        }
        catch (Exception)
        {
        }
    }
}
