using LabFusion.Network;
using LabFusion.SDK.Modules;

namespace FusionDarkRP.Telephone;

public static class PhoneLiveTransport
{
    public static void Register()
    {
        if (ModuleMessageManager.GetHandlerByType(typeof(PhoneAccessMessage)) == null)
        {
            ModuleMessageManager.RegisterHandler<PhoneAccessMessage>();
        }

        if (ModuleMessageManager.GetHandlerByType(typeof(WindyLiveMessage)) == null)
        {
            ModuleMessageManager.RegisterHandler<WindyLiveMessage>();
        }

        if (ModuleMessageManager.GetHandlerByType(typeof(WindyLiveVoiceHost)) == null)
        {
            ModuleMessageManager.RegisterHandler<WindyLiveVoiceHost>();
        }

        if (ModuleMessageManager.GetHandlerByType(typeof(WindyLiveVoiceClient)) == null)
        {
            ModuleMessageManager.RegisterHandler<WindyLiveVoiceClient>();
        }
    }

    public static void Access(PhoneAccessView view, byte target)
    {
        MessageRelay.RelayModule<PhoneAccessMessage, PhonePacket>(
            PhonePacket.From(view),
            new MessageRoute(target, (NetworkChannel)0)
        );
    }

    public static void State(WindyLiveView view, byte target)
    {
        MessageRelay.RelayModule<WindyLiveMessage, PhonePacket>(
            PhonePacket.From(view),
            new MessageRoute(target, (NetworkChannel)0)
        );
    }

    public static void Send(PhoneVoicePacket packet)
    {
        MessageRelay.RelayModule<WindyLiveVoiceHost, PhoneVoicePacket>(
            packet,
            new MessageRoute((RelayType)1, (NetworkChannel)1)
        );
    }

    public static void Voice(PhoneVoicePacket packet, byte target)
    {
        MessageRelay.RelayModule<WindyLiveVoiceClient, PhoneVoicePacket>(
            packet,
            new MessageRoute(target, (NetworkChannel)1)
        );
    }
}
