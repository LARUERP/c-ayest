using System;
using System.IO;
using FusionDarkRP.RP;

namespace FusionDarkRP.Economy
{
    public sealed class EconomyRuntime
    {
        private readonly string dataDirectory;
        private readonly long maxBalanceCents;

        public WalletService Wallets { get; private set; }
        public EconomyConfig Config { get; private set; }
        public BankService Bank { get; private set; }
        public TransactionHistoryService History { get; private set; }
        public BankOperationsService BankOperations { get; private set; }
        public AtmService Atm { get; private set; }
        public SalaryService Salaries { get; private set; }

        public PhysicalCashService PhysicalCash { get; private set; }
        public BankCardService BankCards { get; private set; }
        public MissionRuntime Missions { get; private set; }

        public EconomyRuntime(
            string directory,
            long maxBalanceCents)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException(
                    "Le dossier de sauvegarde est invalide.",
                    nameof(directory));

            if (maxBalanceCents <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maxBalanceCents));

            this.maxBalanceCents = maxBalanceCents;
            dataDirectory = directory;

            Directory.CreateDirectory(dataDirectory);

            string walletDirectory =
                Path.Combine(dataDirectory, "Wallets");

            string historyDirectory =
                Path.Combine(dataDirectory, "History");

            string configDirectory =
                Path.Combine(dataDirectory, "Config");

            string bankCardDirectory =
                Path.Combine(dataDirectory, "BankCards");

            Directory.CreateDirectory(walletDirectory);
            Directory.CreateDirectory(historyDirectory);
            Directory.CreateDirectory(configDirectory);
            Directory.CreateDirectory(bankCardDirectory);

            Config = new EconomyConfig(configDirectory);

            Wallets = new WalletService(
                walletDirectory,
                maxBalanceCents);

            Bank = new BankService(Wallets);

            History = new TransactionHistoryService(
                historyDirectory);

            BankOperations = new BankOperationsService(
                Bank,
                History);

            Atm = new AtmService(BankOperations);

            Salaries = new SalaryService(
                Wallets,
                Config,
                Path.Combine(configDirectory, "salary-schedule.json"));

            PhysicalCash = new PhysicalCashService(Wallets);

            BankCards = new BankCardService(
                bankCardDirectory);

            Missions = new MissionRuntime(
                Wallets,
                Path.Combine(dataDirectory, "Missions"));

            MelonLoader.MelonLogger.Msg(
                "[FusionDarkRP] Services economiques initialises.");
        }

        public WalletData InitializePlayer(
            string playerId,
            long startingBankCents = 0)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return null;

            if (startingBankCents < 0)
                startingBankCents = 0;

            WalletData wallet = Wallets.GetWallet(playerId);

            bool isNewPlayer = wallet == null;

            if (isNewPlayer)
            {
                wallet = Wallets.GetOrCreateWallet(
                    playerId,
                    Math.Min(startingBankCents, maxBalanceCents));

                if (wallet == null)
                    return null;

                long startingCash = Config.GetSnapshot()
                    .StartingCashCents;

                startingCash = Math.Max(
                    0,
                    Math.Min(startingCash, maxBalanceCents));

                if (startingCash > 0)
                {
                    string requestId =
                        "starting-cash:" + playerId;

                    string error;

                    if (!Wallets.TryGiveCash(
                        true,
                        requestId,
                        playerId,
                        startingCash,
                        out error))
                    {
                        MelonLoader.MelonLogger.Warning(
                            "[FusionDarkRP] Argent de depart non credite pour "
                            + playerId + " : " + error);
                    }
                }

                MelonLoader.MelonLogger.Msg(
                    "[FusionDarkRP] Nouveau compte cree pour "
                    + playerId);
            }

            Salaries.RegisterPlayer(playerId);

            return Wallets.GetWallet(playerId);
        }

        public void RemovePlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return;

            Salaries.UnregisterPlayer(playerId);
        }

        // Ancienne methode conservee pour compatibilite.
        public bool TryPayPlayerSalary(
            string playerId,
            string jobId,
            bool isHost,
            out long amountPaidCents)
        {
            return Salaries.TryPayDueSalary(
                playerId,
                jobId,
                isHost,
                out amountPaidCents);
        }

        // Nouvelle methode avec prise en compte du grade.
        public bool TryPayPlayerSalary(
            string playerId,
            string jobId,
            int gradeIndex,
            bool isHost,
            out long amountPaidCents)
        {
            return Salaries.TryPayDueSalary(
                playerId,
                jobId,
                gradeIndex,
                isHost,
                out amountPaidCents);
        }

        public void SaveAll()
        {
            try
            {
                Wallets.Save();
                History.Save();
                BankCards.Save();

                MelonLoader.MelonLogger.Msg(
                    "[FusionDarkRP] Donnees economiques sauvegardees.");
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Error(
                    "[FusionDarkRP] Erreur de sauvegarde economique : "
                    + exception);
            }
        }

        public string GetDataDirectory()
        {
            return dataDirectory;
        }
    }
}
