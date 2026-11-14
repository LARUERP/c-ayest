
using System;
using BoneLib.BoneMenu;
using UnityEngine;
using MelonLoader;

namespace FusionDarkRP.Economy
{
    /// <summary>
    /// Page d'information des distributeurs automatiques.
    /// Les opérations bancaires sont désormais réalisées
    /// exclusivement sur les distributeurs physiques 3D.
    /// </summary>
    public static class AtmMenu
    {
        private static Page atmPage;

        private static Color white;
        private static Color yellow;
        private static Color cyan;

        private static bool initialized;

        public static bool IsInitialized
        {
            get { return initialized; }
        }

        public static void Initialize(Page mainPage)
        {
            if (initialized)
                return;

            if (mainPage == null)
                return;

            try
            {

                white = Color.white;
                yellow = Color.yellow;
                cyan = Color.cyan;

                atmPage = NewPage(
                    mainPage,
                    "Distributeur automatique",
                    cyan);

                AddInfo(
                    atmPage,
                    "FUSION BANK",
                    yellow);

                AddInfo(
                    atmPage,
                    "Distributeurs bancaires physiques",
                    cyan);

                AddInfo(
                    atmPage,
                    "Les operations bancaires se font directement "
                    + "sur les distributeurs 3D.",
                    white);

                AddInfo(
                    atmPage,
                    "1. Rendez-vous devant un distributeur.",
                    white);

                AddInfo(
                    atmPage,
                    "2. Placez votre carte bancaire physique "
                    + "dans la fente.",
                    white);

                AddInfo(
                    atmPage,
                    "3. Votre compte et vos soldes "
                    + "s'afficheront automatiquement.",
                    white);

                AddInfo(
                    atmPage,
                    "4. Choisissez un depot ou un retrait.",
                    white);

                AddInfo(
                    atmPage,
                    "5. Retirez votre carte pour fermer "
                    + "la session bancaire.",
                    white);

                AddInfo(
                    atmPage,
                    "La validation des operations est effectuee "
                    + "par le serveur.",
                    yellow);

                initialized = true;

                MelonLogger.Msg(
                    "[FusionDarkRP ATM] Page d'information initialisee.");
            }
            catch (Exception exception)
            {
                initialized = false;

                MelonLogger.Error(
                    "[FusionDarkRP ATM] Initialisation impossible : "
                    + exception);
            }
        }

        public static void Open()
        {
            if (!initialized)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP ATM] Interface non initialisee.");
                return;
            }

            AddInfo(
                atmPage,
                "Utilisez un distributeur physique 3D "
                + "pour acceder a votre compte.",
                cyan);
        }

        /// <summary>
        /// Conservé pour compatibilité avec les anciens appels.
        /// Aucune consultation sans carte physique n'est envoyée.
        /// </summary>
        public static void RequestBalances()
        {
            if (!initialized)
                return;

            AddInfo(
                atmPage,
                "Inserez votre carte bancaire dans un "
                + "distributeur physique pour consulter votre solde.",
                yellow);
        }

        public static void Shutdown()
        {
            atmPage = null;



            initialized = false;
        }

        private static Page NewPage(Page parent, string name, Color color)
        {
            return MenuKit.NewPage(parent, name, color);
        }

        private static void AddInfo(Page page, string label, Color color)
        {
            MenuKit.AddInfo(page, label, color, "[FusionDarkRP ATM]");
        }

        private static void AddFunction(
            Page page, string label, Color color, Action action)
        {
            MenuKit.AddFunction(page, label, color, action);
        }
    }
}
