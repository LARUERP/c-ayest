using LabFusion.Network;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public static class PhoneNearbyTransport
{
    public static void Register()
    {
        if (ModuleMessageManager.GetHandlerByType(typeof(PhoneNoticeMessage)) == null)
        {
            ModuleMessageManager.RegisterHandler<PhoneNoticeMessage>();
        }

        if (ModuleMessageManager.GetHandlerByType(typeof(PhoneSpeakerMessage)) == null)
        {
            ModuleMessageManager.RegisterHandler<PhoneSpeakerMessage>();
        }
    }

    public static void Notice(PhoneNotice notice, byte target)
    {
        MessageRelay.RelayModule<PhoneNoticeMessage, PhonePacket>(
            PhonePacket.From(notice),
            new MessageRoute(target, (NetworkChannel)0)
        );
    }

    public static void Speaker(PhoneSpeakerPacket packet, byte target)
    {
        MessageRelay.RelayModule<PhoneSpeakerMessage, PhoneSpeakerPacket>(
            packet,
            new MessageRoute(target, (NetworkChannel)1)
        );
    }
}
