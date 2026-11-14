
using LabFusion.Network;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public static class PhoneMediaTransport
{
    public static void Register()
    {
        if (ModuleMessageManager.GetHandlerByType(typeof(PhoneMediaUploadMessage)) == null)
        {
            ModuleMessageManager.RegisterHandler<PhoneMediaUploadMessage>();
        }

        if (ModuleMessageManager.GetHandlerByType(typeof(PhoneMediaDownloadMessage)) == null)
        {
            ModuleMessageManager.RegisterHandler<PhoneMediaDownloadMessage>();
        }
    }

    public static void Send(PhoneMediaChunk chunk)
    {
        MessageRelay.RelayModule<PhoneMediaUploadMessage, PhonePacket>(
            PhonePacket.From(chunk),
            (MessageRoute)(PhoneMediaRules.Stream(chunk.Kind)
                ? new MessageRoute((RelayType)1, (NetworkChannel)1)
                : CommonMessageRoutes.ReliableToServer));
    }

    public static void Reply(PhoneMediaChunk chunk, byte id)
    {
        MessageRelay.RelayModule<PhoneMediaDownloadMessage, PhonePacket>(
            PhonePacket.From(chunk),
            new MessageRoute(
                id,
                (NetworkChannel)(PhoneMediaRules.Stream(chunk.Kind) ? 1 : 0)));
    }
}
