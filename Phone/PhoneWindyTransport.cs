using LabFusion.Network;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public static class PhoneWindyTransport
{
    public static void Register()
    {
        if (ModuleMessageManager.GetHandlerByType(typeof(PhoneWindyViewMessage)) == null)
        {
            ModuleMessageManager.RegisterHandler<PhoneWindyViewMessage>();
        }
    }

    public static void Reply(WindyView view, byte target)
    {
        MessageRelay.RelayModule<PhoneWindyViewMessage, PhonePacket>(
            PhonePacket.From(view),
            new MessageRoute(target, (NetworkChannel)0)
        );
    }
}
