using LabFusion.Network;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public static class PhoneTransport
{
    public static void Register()
    {
        if (ModuleMessageManager.GetHandlerByType(typeof(PhoneCommandMessage)) == null)
        {
            ModuleMessageManager.RegisterHandler<PhoneCommandMessage>();
        }

        if (ModuleMessageManager.GetHandlerByType(typeof(PhoneViewMessage)) == null)
        {
            ModuleMessageManager.RegisterHandler<PhoneViewMessage>();
        }
    }

    public static void Send(PhoneCommand command)
    {
        MessageRelay.RelayModule<PhoneCommandMessage, PhonePacket>(
            PhonePacket.From(command),
            CommonMessageRoutes.ReliableToServer
        );
    }

    public static void Reply(PhoneView view, byte id)
    {
        MessageRelay.RelayModule<PhoneViewMessage, PhonePacket>(
            PhonePacket.From(view),
            new MessageRoute(id, (NetworkChannel)0)
        );
    }
}
