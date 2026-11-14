using FusionDarkRP.Integration;
using System;
using System.Text.Json;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public sealed class PhoneCallStateMessage : ModuleMessageHandler
{
    public override ExpectedReceiverType ExpectedReceiver => (ExpectedReceiverType)2;

    protected override void OnHandleMessage(ReceivedMessage message)
    {
        if (NetworkInfo.IsHost ||
            message.IsServerHandled ||
            message.Sender != 0 ||
            message.Bytes.Length > 4096)
        {
            return;
        }

        try
        {
            byte[] payload = message.ReadData<PhonePacket>().Payload;

            if (payload == null || payload.Length > 4000)
                return;

            PhoneCallUpdate update =
                JsonSerializer.Deserialize<PhoneCallUpdate>(payload);

            if (update != null &&
                update.Protocol == 16 &&
                update.Player == PlatformIds.Local)
            {
                PhoneMod.Instance?.EnqueueCall(update);
            }
        }
        catch (Exception)
        {
            // Message invalide : on l'ignore sans interrompre le téléphone.
        }
    }
}
