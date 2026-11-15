using System;
using FusionDarkRP.Economy;
using LabFusion.Player;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Convertit les metiers et grades DarkRP
    /// en donnees utilisables par le systeme economique.
    /// </summary>
    public static class EconomyJobResolver
    {
        public static string GetJobId(string teamName)
        {
            if (string.IsNullOrWhiteSpace(teamName))
                return "citizen";

            byte teamId = DarkRPJobs.TeamId(teamName);

            switch (teamId)
            {
                case 0:
                    return "citizen";

                case 1:
                    return "eboueurs";

                case 2:
                    return "banque";

                case 3:
                    return "mairie";

                case 4:
                    return "epicerie";

                case 5:
                    return "police";

                case 6:
                    return "medecins";

                case 7:
                    return "marseillais";

                case 8:
                    return "parisiens";

                case 9:
                    return "gens_du_voyage";

                case 10:
                    return "armurerie";

                case 11:
                    return "concessionnaire";

                default:
                    return "citizen";
            }
        }

        public static string GetJobIdFromPlayer(PlayerID player)
        {
            if (player == null || !player.IsValid)
                return "citizen";

            try
            {
                string teamName =
                    player.Metadata.Metadata.GetMetadata(
                        DarkRPJobs.TeamKey);

                return GetJobId(teamName);
            }
            catch
            {
                return "citizen";
            }
        }

        /// <summary>
        /// Retourne le rang du joueur dans son metier.
        /// Le premier grade correspond a 0.
        /// </summary>
        public static int GetGradeIndexFromPlayer(PlayerID player)
        {
            if (player == null || !player.IsValid)
                return 0;

            try
            {
                string teamName =
                    player.Metadata.Metadata.GetMetadata(
                        DarkRPJobs.TeamKey);

                string gradeName =
                    player.Metadata.Metadata.GetMetadata(
                        DarkRPJobs.GradeKey);

                byte teamId = DarkRPJobs.TeamId(teamName);
                byte gradeId = DarkRPJobs.GradeId(gradeName);

                int rank = DarkRPJobs.RankIndex(
                    teamId,
                    gradeId);

                return rank < 0 ? 0 : rank;
            }
            catch
            {
                return 0;
            }
        }

        public static bool TryPaySalary(
            EconomyRuntime runtime,
            PlayerID player,
            bool isHost,
            out long amountPaidCents)
        {
            amountPaidCents = 0;

            if (runtime == null ||
                player == null ||
                !player.IsValid ||
                !isHost)
            {
                return false;
            }

            string persistentId;

            if (!PlayerIdentityService.TryGetPersistentId(
                player,
                out persistentId))
            {
                return false;
            }

            string jobId = GetJobIdFromPlayer(player);

            int gradeIndex =
                GetGradeIndexFromPlayer(player);

            return runtime.TryPayPlayerSalary(
                persistentId,
                jobId,
                gradeIndex,
                true,
                out amountPaidCents);
        }
    }
}
