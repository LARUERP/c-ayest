using System;
using System.Text.Json;
using LabFusion.Network;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public sealed class PhoneMediaDownloadMessage : ModuleMessageHandler
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

            if (payload != null && payload.Length <= 3800)
            {
                PhoneMediaChunk chunk =
                    JsonSerializer.Deserialize<PhoneMediaChunk>(payload);

                if (PhoneMediaRules.ValidChunk(chunk))
                {
                    PhoneMod.Instance?.Media?.Enqueue(0uL, chunk, host: false);
                }
            }
        }
        catch (Exception)
        {
            // Les paquets invalides sont ignorés.
        }
    }
}
