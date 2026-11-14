using FusionDarkRP.Integration;
using System;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public sealed class PhoneVoiceToHost : ModuleMessageHandler
{
    public override ExpectedReceiverType ExpectedReceiver => (ExpectedReceiverType)1;

    protected override void OnHandleMessage(ReceivedMessage message)
    {
        if (!NetworkInfo.IsHost ||
            !message.IsServerHandled ||
            !message.Sender.HasValue ||
            message.Bytes.Length > 50000)
        {
            return;
        }

        try
        {
            PlayerID playerID = PlatformIds.GetPlayerID(
                message.Sender.Value);

            if (playerID != null && playerID.IsValid)
            {
                PhoneVoicePacket phoneVoicePacket =
                    message.ReadData<PhoneVoicePacket>();

                if (phoneVoicePacket.Read(
                    out var call,
                    out var speaker,
                    out var sequence,
                    out var audio))
                {
                    PhoneMod.Instance?.EnqueueVoice(
                        PlatformIds.Of(playerID),
                        call,
                        sequence,
                        audio,
                        host: true);
                }
            }
        }
        catch (Exception)
        {
        }
    }
}
