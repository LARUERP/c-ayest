using FusionDarkRP.Integration;
using System;
using System.Text.Json;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public sealed class PhoneViewMessage : ModuleMessageHandler
{
    public override ExpectedReceiverType ExpectedReceiver => (ExpectedReceiverType)2;

    protected override void OnHandleMessage(ReceivedMessage message)
    {
        if (NetworkInfo.IsHost ||
            message.IsServerHandled ||
            message.Sender != 0 ||
            message.Bytes.Length > 262144)
        {
            return;
        }

        try
        {
            byte[] payload =
                message.ReadData<PhonePacket>().Payload;

            if (payload != null && payload.Length <= 260000)
            {
                PhoneView phoneView =
                    JsonSerializer.Deserialize<PhoneView>(payload);

                if (phoneView != null &&
                    phoneView.Player == PlatformIds.Local &&
                    phoneView.Protocol == 16)
                {
                    PhoneMod.Instance?.Enqueue(phoneView);
                }
            }
        }
        catch (Exception)
        {
        }
    }
}
