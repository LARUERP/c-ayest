using System;
using System.Text.Json;
using LabFusion.Network;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public sealed class PhoneNoticeMessage : ModuleMessageHandler
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
            byte[] payload =
                message.ReadData<PhonePacket>().Payload;

            if (payload != null && payload.Length <= 4000)
            {
                PhoneNotice phoneNotice =
                    JsonSerializer.Deserialize<PhoneNotice>(payload);

                if (phoneNotice != null)
                {
                    PhoneMod.Instance?.EnqueueNotice(phoneNotice);
                }
            }
        }
        catch (Exception)
        {
        }
    }
}
