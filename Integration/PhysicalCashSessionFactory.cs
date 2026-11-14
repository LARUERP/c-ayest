using System;
using LabFusion.Player;
using FusionDarkRP.Economy;

namespace FusionDarkRP.Integration
{
    public static class PhysicalCashSessionFactory
    {
        public static bool TryCreate(
            EconomyRuntime runtime,
            PlayerID player,
            Func<bool> authorityProvider,
            out PlayerPhysicalCashSession session,
            out string error)
        {
            session = null;
            error = null;

            if (runtime == null)
            {
                error = "Le système économique n'est pas initialisé.";
                return false;
            }

            if (player == null || !player.IsValid)
            {
                error = "Joueur LabFusion invalide.";
                return false;
            }

            if (authorityProvider == null)
            {
                error = "Vérificateur d'autorité manquant.";
                return false;
            }

            if (!PlayerIdentityService.TryGetPersistentId(
                player,
                out string persistentId))
            {
                error = "Impossible de récupérer l'identifiant persistant du joueur.";
                return false;
            }

            try
            {
                session = new PlayerPhysicalCashSession(
                    runtime,
                    persistentId,
                    authorityProvider
                );

                return true;
            }
            catch (Exception exception)
            {
                session = null;
                error = "Création de session impossible : "
                    + exception.Message;

                return false;
            }
        }
    }
}
