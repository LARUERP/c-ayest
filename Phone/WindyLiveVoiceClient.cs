using System;
using LabFusion.Network;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public sealed class WindyLiveVoiceClient : ModuleMessageHandler
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
            PhoneVoicePacket phoneVoicePacket =
                message.ReadData<PhoneVoicePacket>();

            if (phoneVoicePacket.Read(
                out var call,
                out var speaker,
                out var sequence,
                out var audio))
            {
                PhoneMod.Instance?.EnqueueLiveVoice(
                    speaker,
                    call,
                    sequence,
                    audio,
                    host: false);
            }
        }
        catch (Exception)
        {
        }
    }
}
