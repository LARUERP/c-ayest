
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using FusionDarkRP.Economy;
using FusionDarkRP.Integration;
using LabFusion.Player;
using LabFusion.Network;
using BoneLib.BoneMenu;
using UnityEngine;
using MelonLoader;

namespace FusionDarkRP
{
    public static class BankMenu
    {
        private static Page banquePage;
        private static Page clientPage;

        private static Color white;
        private static Color yellow;
        private static Color green;
        private static Color red;
        private static Color cyan;

        private static byte selectedCustomerId;
        private static long activeConsentToken;
        private static bool initialized;
        private static bool consentRequestPending;

        private static readonly Dictionary<byte, string> playerNames =
            new Dictionary<byte, string>();

        private static readonly Dictionary<byte, PlayerID> connectedPlayers =
            new Dictionary<byte, PlayerID>();

        private static readonly HashSet<byte> bankerGrades =
            new HashSet<byte> { 4, 5, 6, 7, 32 };

        private static long lastCash;
        private static long lastBank;

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
                green = Color.green;
                red = Color.red;
                cyan = Color.cyan;

                banquePage = NewPage(
                    mainPage,
                    "Banque",
                    yellow
                );

                clientPage = NewPage(
                    mainPage,
                    "Espace client - Banque",
                    cyan
                );

                AddInfo(banquePage, "SERVICE BANCAIRE", yellow);
                AddInfo(banquePage, "Selectionne un joueur connecte.", white);

                AddFunction(
                    banquePage,
                    "Actualiser les joueurs",
                    cyan,
                    RefreshPlayers
                );

                AddInfo(
                    clientPage,
                    "Les demandes bancaires apparaitront ici.",
                    white
                );

                WorldModelAdminMenu.Initialize(mainPage);

                AtmMenu.Initialize(mainPage);

                BankNetwork.ResponseReceived -= OnBankResponse;
                BankNetwork.ResponseReceived += OnBankResponse;

                initialized = true;

                MelonLogger.Msg(
                    "[Fusion DarkRP Banque] Interface initialisee."
                );

                MelonLogger.Msg(
                    "[Fusion DarkRP ATM] Interface raccordee au menu principal."
                );
            }
            catch (Exception ex)
            {
                AtmMenu.Shutdown();
                WorldModelAdminMenu.Shutdown();

                MelonLogger.Error(
                    "[Fusion DarkRP Banque] Initialisation : " + ex
                );
            }
        }

        public static void Shutdown()
        {
            AtmMenu.Shutdown();
            WorldModelAdminMenu.Shutdown();

            BankNetwork.ResponseReceived -= OnBankResponse;

            playerNames.Clear();
            connectedPlayers.Clear();

            selectedCustomerId = 0;
            activeConsentToken = 0;
            consentRequestPending = false;

            lastCash = 0;
            lastBank = 0;

            banquePage = null;
            clientPage = null;

            initialized = false;
        }

        // IDENTIFICATION DU BANQUIER

        private static bool IsLocalBanker()
        {
            try
            {
                PlayerID local = PlayerIDManager.LocalID;

                if (local == null || !local.IsValid)
                    return false;

                string team = DarkRPNetwork.GetMetadata(
                    local,
                    DarkRPNetwork.TeamKey
                );

                string grade = DarkRPNetwork.GetMetadata(
                    local,
                    DarkRPNetwork.GradeKey
                );

                byte teamId = DarkRPJobs.TeamId(team);
                byte gradeId = DarkRPJobs.GradeId(grade);

                return teamId == 2 &&
                    bankerGrades.Contains(gradeId);
            }
            catch
            {
                return false;
            }
        }

        // LISTE DES JOUEURS

        private static void RefreshPlayers()
        {
            if (!initialized)
                return;

            if (!IsLocalBanker())
            {
                AddInfo(
                    banquePage,
                    "Acces reserve aux membres autorises de la Banque.",
                    red
                );
                return;
            }

            try
            {
                playerNames.Clear();
                connectedPlayers.Clear();

                int count = 0;
                PlayerID local = PlayerIDManager.LocalID;

                foreach (PlayerID connectedPlayer in PlayerIDManager.PlayerIDs)
                {
                    if (connectedPlayer == null || !connectedPlayer.IsValid)
                        continue;

                    byte id = connectedPlayer.SmallID;

                    if (local != null &&
                        local.IsValid &&
                        local.SmallID == id)
                        continue;

                    connectedPlayers[id] = connectedPlayer;

                    string name = DarkRPNetwork.GetPlayerName(id);

                    if (string.IsNullOrWhiteSpace(name))
                        name = "Joueur " + id;

                    playerNames[id] = name;

                    Page page = NewPage(
                        banquePage,
                        name,
                        cyan
                    );

                    AddInfo(page, "Client : " + name, white);
                    AddInfo(page, "Identifiant : " + id, white);

                    AddFunction(
                        page,
                        "Envoyer une demande bancaire",
                        green,
                        () => RequestConsent(id)
                    );

                    AddFunction(
                        page,
                        "Delivrer une carte bancaire",
                        cyan,
                        () => IssueBankCard(id)
                    );

                    count++;
                }

                AddInfo(
                    banquePage,
                    "Joueurs connectes : " + count,
                    yellow
                );
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    "[Fusion DarkRP Banque] Actualisation : " + ex
                );
            }
        }

        // DELIVRANCE D'UNE CARTE BANCAIRE

        private static void IssueBankCard(byte customerId)
        {
            if (!IsLocalBanker())
            {
                AddInfo(
                    banquePage,
                    "Acces reserve aux banquiers autorises.",
                    red
                );
                return;
            }

            if (!NetworkInfo.HasServer)
            {
                AddInfo(
                    banquePage,
                    "Aucune session LabFusion active.",
                    red
                );
                return;
            }

            PlayerID targetPlayer;

            if (!connectedPlayers.TryGetValue(
                customerId,
                out targetPlayer) ||
                targetPlayer == null ||
                !targetPlayer.IsValid)
            {
                AddInfo(
                    banquePage,
                    "Le joueur destinataire n'est plus connecte. Actualise la liste.",
                    red
                );
                return;
            }

            if (!BankCardIssueNetwork.RequestIssue(customerId))
            {
                AddInfo(
                    banquePage,
                    "Impossible d'envoyer la demande de carte bancaire.",
                    red
                );
                return;
            }

            AddInfo(
                banquePage,
                "Demande de delivrance envoyee pour "
                + GetName(customerId)
                + ".",
                green
            );

            AddInfo(
                banquePage,
                "Le serveur verifie les droits du banquier et cree la carte.",
                white
            );
        }

        // DEMANDE DE CONSENTEMENT

        private static void RequestConsent(byte customerId)
        {
            if (!IsLocalBanker())
            {
                AddInfo(
                    banquePage,
                    "Tu n'es pas autorise a utiliser cette fonction.",
                    red
                );
                return;
            }

            if (!BankNetwork.IsInitialized)
            {
                AddInfo(
                    banquePage,
                    "Le reseau bancaire n'est pas initialise.",
                    red
                );
                return;
            }

            if (consentRequestPending)
            {
                AddInfo(
                    banquePage,
                    "Une demande est deja en attente.",
                    yellow
                );
                return;
            }

            selectedCustomerId = customerId;
            activeConsentToken = 0;
            consentRequestPending = true;

            BankNetwork.RequestConsent(customerId);

            AddInfo(
                banquePage,
                "Demande envoyee a " + GetName(customerId) + ".",
                yellow
            );
        }

        // RECEPTION DES REPONSES RESEAU

        private static void OnBankResponse(
            BankNetworkResponseData response)
        {
            if (response == null || !initialized)
                return;

            try
            {
                BankNetworkResult result =
                    (BankNetworkResult)response.Result;

                switch (result)
                {
                    case BankNetworkResult.ConsentRequested:
                        HandleConsentRequested(response);
                        break;

                    case BankNetworkResult.ConsentAccepted:
                        HandleConsentAccepted(response);
                        break;

                    case BankNetworkResult.ConsentRefused:
                        HandleConsentRefused(response);
                        break;

                    case BankNetworkResult.BalancesAvailable:
                        HandleBalances(response);
                        break;

                    case BankNetworkResult.OperationRequested:
                        HandleOperationRequested(response);
                        break;

                    case BankNetworkResult.OperationCompleted:
                        HandleOperationCompleted(response);
                        break;

                    case BankNetworkResult.OperationRefused:
                        HandleOperationRefused(response);
                        break;

                    case BankNetworkResult.Failed:
                        HandleFailure();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    "[Fusion DarkRP Banque] Traitement reponse : " + ex
                );
            }
        }

        // DEMANDE RECUE PAR LE CLIENT OU LE BANQUIER

        private static void HandleConsentRequested(
            BankNetworkResponseData response)
        {
            if (IsLocalBanker())
            {
                consentRequestPending = true;

                AddInfo(
                    banquePage,
                    "Demande bancaire envoyee. En attente du client.",
                    yellow
                );

                return;
            }

            activeConsentToken = response.Token;

            AddInfo(
                clientPage,
                "UNE DEMANDE BANCAIRE EST EN ATTENTE",
                yellow
            );

            AddInfo(
                clientPage,
                "Le banquier souhaite acceder a tes soldes.",
                white
            );

            AddFunction(
                clientPage,
                "ACCEPTER LA DEMANDE",
                green,
                () => BankNetwork.ResolveConsent(response.Token, true)
            );

            AddFunction(
                clientPage,
                "REFUSER LA DEMANDE",
                red,
                () => BankNetwork.ResolveConsent(response.Token, false)
            );
        }

        // CONSENTEMENT ACCEPTE

        private static void HandleConsentAccepted(
            BankNetworkResponseData response)
        {
            if (IsLocalBanker())
            {
                activeConsentToken = response.Token;
                consentRequestPending = false;

                AddInfo(
                    banquePage,
                    "Le client a accepte la consultation.",
                    green
                );

                AddFunction(
                    banquePage,
                    "Consulter les soldes",
                    cyan,
                    () =>
                    {
                        if (activeConsentToken <= 0 ||
                            selectedCustomerId == 0)
                            return;

                        BankNetwork.RequestBalances(
                            selectedCustomerId,
                            activeConsentToken
                        );

                        AddInfo(
                            banquePage,
                            "Consultation des soldes demandee.",
                            yellow
                        );
                    }
                );

                return;
            }

            AddInfo(
                clientPage,
                "Tu as accepte la demande bancaire.",
                green
            );
        }

        // CONSENTEMENT REFUSE

        private static void HandleConsentRefused(
            BankNetworkResponseData response)
        {
            activeConsentToken = 0;
            consentRequestPending = false;

            if (IsLocalBanker())
            {
                AddInfo(
                    banquePage,
                    "Le client a refuse la consultation.",
                    red
                );
            }
            else
            {
                AddInfo(
                    clientPage,
                    "Tu as refuse la demande bancaire.",
                    red
                );
            }
        }

        // AFFICHAGE DES SOLDES

        private static void HandleBalances(
            BankNetworkResponseData response)
        {
            if (!IsLocalBanker())
                return;

            lastCash = response.CashCents;
            lastBank = response.BankCents;

            AddInfo(banquePage, "SOLDES DU CLIENT", yellow);
            AddInfo(banquePage, "Especes : " + FormatMoney(lastCash), green);
            AddInfo(banquePage, "Compte bancaire : " + FormatMoney(lastBank), green);
            AddInfo(banquePage, "Operations disponibles :", cyan);

            AddFunction(
                banquePage,
                "Deposer 10 EUR",
                green,
                () => RequestOperation(BankOperationType.Deposit, 1000)
            );

            AddFunction(
                banquePage,
                "Deposer 50 EUR",
                green,
                () => RequestOperation(BankOperationType.Deposit, 5000)
            );

            AddFunction(
                banquePage,
                "Deposer 100 EUR",
                green,
                () => RequestOperation(BankOperationType.Deposit, 10000)
            );

            AddFunction(
                banquePage,
                "Retirer 10 EUR",
                yellow,
                () => RequestOperation(BankOperationType.Withdraw, 1000)
            );

            AddFunction(
                banquePage,
                "Retirer 50 EUR",
                yellow,
                () => RequestOperation(BankOperationType.Withdraw, 5000)
            );

            AddFunction(
                banquePage,
                "Retirer 100 EUR",
                yellow,
                () => RequestOperation(BankOperationType.Withdraw, 10000)
            );
        }

        // DEMANDE D'OPERATION

        private static void RequestOperation(
            BankOperationType operation,
            long amountCents)
        {
            if (!IsLocalBanker() || !BankNetwork.IsInitialized)
                return;

            if (activeConsentToken <= 0 || selectedCustomerId == 0)
            {
                AddInfo(
                    banquePage,
                    "Aucun consentement bancaire actif.",
                    red
                );
                return;
            }

            if (amountCents <= 0)
                return;

            BankNetwork.RequestOperation(
                selectedCustomerId,
                activeConsentToken,
                operation,
                amountCents
            );

            string operationName =
                operation == BankOperationType.Deposit
                    ? "Depot"
                    : "Retrait";

            AddInfo(
                banquePage,
                operationName + " de " +
                FormatMoney(amountCents) +
                " : confirmation demandee au client.",
                yellow
            );
        }

        // CONFIRMATION D'OPERATION PAR LE CLIENT

        private static void HandleOperationRequested(
            BankNetworkResponseData response)
        {
            if (IsLocalBanker())
            {
                AddInfo(
                    banquePage,
                    "En attente de la confirmation du client.",
                    yellow
                );
                return;
            }

            string operationName =
                response.Operation == (byte)BankOperationType.Deposit
                    ? "DEPOT"
                    : "RETRAIT";

            AddInfo(clientPage, "CONFIRMATION D'OPERATION", yellow);
            AddInfo(
                clientPage,
                operationName + " de " + FormatMoney(response.AmountCents),
                white
            );

            AddFunction(
                clientPage,
                "CONFIRMER L'OPERATION",
                green,
                () => BankNetwork.ResolveOperation(response.Token, true)
            );

            AddFunction(
                clientPage,
                "REFUSER L'OPERATION",
                red,
                () => BankNetwork.ResolveOperation(response.Token, false)
            );
        }

        // OPERATION TERMINEE

        private static void HandleOperationCompleted(
            BankNetworkResponseData response)
        {
            if (response.Success == 0)
            {
                HandleOperationRefused(response);
                return;
            }

            if (IsLocalBanker())
            {
                lastCash = response.CashCents;
                lastBank = response.BankCents;

                AddInfo(banquePage, "OPERATION EFFECTUEE", green);
                AddInfo(banquePage, "Especes : " + FormatMoney(lastCash), green);
                AddInfo(banquePage, "Compte bancaire : " + FormatMoney(lastBank), green);
            }
            else
            {
                AddInfo(
                    clientPage,
                    "L'operation bancaire a ete effectuee.",
                    green
                );
            }
        }

        // OPERATION REFUSEE

        private static void HandleOperationRefused(
            BankNetworkResponseData response)
        {
            if (IsLocalBanker())
            {
                AddInfo(
                    banquePage,
                    "L'operation a ete refusee ou a echoue.",
                    red
                );
            }
            else
            {
                AddInfo(
                    clientPage,
                    "L'operation a ete refusee ou a echoue.",
                    red
                );
            }
        }

        // ERREUR RESEAU

        private static void HandleFailure()
        {
            if (IsLocalBanker())
            {
                consentRequestPending = false;

                AddInfo(
                    banquePage,
                    "La demande bancaire a echoue.",
                    red
                );
            }
            else
            {
                AddInfo(
                    clientPage,
                    "Une erreur bancaire est survenue.",
                    red
                );
            }
        }

        // OUTILS

        private static string FormatMoney(long cents)
        {
            decimal amount = cents / 100m;

            return amount.ToString(
                "N2",
                CultureInfo.GetCultureInfo("fr-FR")
            ) + " EUR";
        }

        private static string GetName(byte id)
        {
            string name;

            if (playerNames.TryGetValue(id, out name))
                return name;

            return DarkRPNetwork.GetPlayerName(id);
        }

        

        

        private static Page NewPage(Page parent, string name, Color color)
        {
            return MenuKit.NewPage(parent, name, color);
        }

        private static void AddInfo(Page page, string label, Color color)
        {
            MenuKit.AddInfo(page, label, color, "[Fusion DarkRP Banque]");
        }

        private static void AddFunction(
            Page page, string label, Color color, Action action)
        {
            MenuKit.AddFunction(page, label, color, action);
        }
    }
}
