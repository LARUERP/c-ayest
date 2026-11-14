using FusionDarkRP.Integration;
using System;
using System.Text.Json;
using LabFusion.Network;
using LabFusion.Player;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public sealed class PhoneCommandMessage : ModuleMessageHandler
{
    public override ExpectedReceiverType ExpectedReceiver => (ExpectedReceiverType)1;

    protected override void OnHandleMessage(ReceivedMessage message)
    {
        if (!NetworkInfo.IsHost ||
            !message.IsServerHandled ||
            !message.Sender.HasValue ||
            message.Bytes.Length > 8192)
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

            if (payload != null && payload.Length <= 8000)
            {
                PhoneCommand phoneCommand =
                    JsonSerializer.Deserialize<PhoneCommand>(payload);

                if (phoneCommand != null)
                {
                    PhoneMod.Instance?.Enqueue(
                        PlatformIds.Of(playerID),
                        phoneCommand);
                }
            }
        }
        catch (Exception)
        {
        }
    }
}
