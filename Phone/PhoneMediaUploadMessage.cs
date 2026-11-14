using FusionDarkRP.Integration;
using System;
using System.Text.Json;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public sealed class PhoneMediaUploadMessage : ModuleMessageHandler
{
    public override ExpectedReceiverType ExpectedReceiver => (ExpectedReceiverType)1;

    protected override void OnHandleMessage(ReceivedMessage message)
    {
        if (!NetworkInfo.IsHost ||
            !message.IsServerHandled ||
            !message.Sender.HasValue ||
            message.Bytes.Length > 4096)
        {
            return;
        }

        try
        {
            PlayerID playerID = PlatformIds.GetPlayerID(
                message.Sender.Value);

            if (playerID == null || !playerID.IsValid)
            {
                return;
            }

            byte[] payload =
                message.ReadData<PhonePacket>().Payload;

            if (payload != null && payload.Length <= 3800)
            {
                PhoneMediaChunk chunk =
                    JsonSerializer.Deserialize<PhoneMediaChunk>(payload);

                if (PhoneMediaRules.ValidChunk(chunk))
                {
                    PhoneMod.Instance?.Media?.Enqueue(
                        PlatformIds.Of(playerID),
                        chunk,
                        host: true);
                }
            }
        }
        catch (Exception)
        {
        }
    }
}
