using FusionDarkRP.Integration;
using System;
using System.Text.Json;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public sealed class WindyLiveMessage : ModuleMessageHandler
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
            WindyLiveView windyLiveView =
                JsonSerializer.Deserialize<WindyLiveView>(
                    message.ReadData<PhonePacket>().Payload);

            if (windyLiveView != null &&
                windyLiveView.Player == PlatformIds.Local &&
                windyLiveView.Protocol == 16)
            {
                PhoneMod.Instance?.EnqueueLive(windyLiveView);
            }
        }
        catch (Exception)
        {
        }
    }
}
