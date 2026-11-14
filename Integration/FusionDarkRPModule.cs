using System;
using LabFusion.SDK.Modules;
using MelonLoader;

namespace FusionDarkRP.Integration
{
    public sealed class FusionDarkRPModule : Module
    {
        private static bool handlersRegistered;

        public override string Name
        {
            get { return "Fusion DarkRP"; }
        }

        public override string Author
        {
            get { return "LARUERP"; }
        }

        public override Version Version
        {
            get { return new Version(1, 0, 0); }
        }

        public override ConsoleColor Color
        {
            get { return ConsoleColor.Yellow; }
        }

        protected override void OnModuleRegistered()
        {
            try
            {
                if (!handlersRegistered)
                {
                    ModuleMessageManager.LoadHandlers(
                        typeof(FusionDarkRPModule).Assembly
                    );

                    handlersRegistered = true;

                    MelonLogger.Msg(
                        "[Fusion DarkRP] Gestionnaires réseau enregistrés."
                    );
                }

                MelonLogger.Msg(
                    "[Fusion DarkRP] Module réseau Fusion enregistré."
                );
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[Fusion DarkRP] Enregistrement des gestionnaires réseau impossible : "
                    + exception
                );
            }
        }

        protected override void OnModuleUnregistered()
        {
            MelonLogger.Msg(
                "[Fusion DarkRP] Module réseau Fusion désenregistré."
            );
        }
    }
}
