using FusionDarkRP.Integration;
using System;
using System.Text.Json;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public sealed class PhoneWindyViewMessage : ModuleMessageHandler
{
    public override ExpectedReceiverType ExpectedReceiver => (ExpectedReceiverType)2;

    protected override void OnHandleMessage(ReceivedMessage message)
    {
        if (NetworkInfo.IsHost ||
            message.IsServerHandled ||
            message.Sender != 0 ||
            message.Bytes.Length > 16000)
        {
            return;
        }

        try
        {
            byte[] payload =
                message.ReadData<PhonePacket>().Payload;

            if (payload != null && payload.Length <= 15000)
            {
                WindyView windyView =
                    JsonSerializer.Deserialize<WindyView>(payload);

                if (windyView != null &&
                    windyView.Protocol == 16 &&
                    windyView.Player == PlatformIds.Local)
                {
                    PhoneMod.Instance?.EnqueueWindy(windyView);
                }
            }
        }
        catch (Exception)
        {
        }
    }
}
