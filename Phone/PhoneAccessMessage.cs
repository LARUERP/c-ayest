
using FusionDarkRP.Integration;
using System;
using System.Text.Json;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public sealed class PhoneAccessMessage : ModuleMessageHandler
{
    public override ExpectedReceiverType ExpectedReceiver => (ExpectedReceiverType)2;

    protected override void OnHandleMessage(ReceivedMessage m)
    {
        if (NetworkInfo.IsHost ||
            m.IsServerHandled ||
            m.Sender != 0 ||
            m.Bytes.Length > 262144)
        {
            return;
        }

        try
        {
            PhoneAccessView phoneAccessView =
                JsonSerializer.Deserialize<PhoneAccessView>(
                    m.ReadData<PhonePacket>().Payload);

            if (phoneAccessView != null &&
                phoneAccessView.Player == PlatformIds.Local &&
                phoneAccessView.Protocol == 16)
            {
                PhoneMod.Instance?.EnqueueAccess(phoneAccessView);
            }
        }
        catch (Exception)
        {
        }
    }
}
