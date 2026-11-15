using System;
using System.Collections.Generic;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Player;
using MelonLoader;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Imprimante à billets (style DarkRP), hôte autoritaire.
    /// Elle imprime en continu dans son bac (plafonné). Quiconque reste
    /// près du bac ~1,5 s récupère le contenu en espèces ; un policier
    /// SAISIT l'imprimante (bac vidé, prime de 20 % au policier, le reste
    /// est détruit). Réglages : constantes ci-dessous.
    /// </summary>
    public static class MoneyPrinterService
    {
        public const string ModelId = "MoneyPrinter";

        private const float TickInterval = 0.5f;
        private const float PrintEverySeconds = 20f;
        private const long PrintCents = 2500L;          // 25 € toutes les 20 s
        private const long CapacityCents = 100000L;     // bac plein : 1 000 €
        private const float ReachMeters = 1.15f;
        private const float HoldSeconds = 1.5f;
        private const float PoliceBonusRate = 0.20f;

        private sealed class Printer
        {
            public long Stored;
            public float NextPrint;
            public readonly Dictionary<byte, float> Near =
                new Dictionary<byte, float>();
            public float NextHint;
        }

        private static readonly Dictionary<string, Printer> Printers =
            new Dictionary<string, Printer>(StringComparer.Ordinal);

        private static float nextTick;

        public static void Update()
        {
            if (!NetworkInfo.HasServer || !NetworkInfo.IsHost)
                return;

            float now = Time.unscaledTime;

            if (now < nextTick)
                return;

            float dt = Mathf.Clamp(now - nextTick + TickInterval, 0.1f, 2f);
            nextTick = now + TickInterval;

            if (!EconomyRuntimeBootstrap.IsInitialized)
                return;

            EconomyRuntime runtime = EconomyRuntimeBootstrap.Runtime;

            if (runtime == null || runtime.Wallets == null)
                return;

            HashSet<string> alive = new HashSet<string>(StringComparer.Ordinal);

            foreach (string id in WorldModelRuntimeManager.GetSpawnedObjectIds())
            {
                string model;

                if (!WorldModelRuntimeManager.TryGetModelId(id, out model) ||
                    !string.Equals(model, ModelId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                GameObject body;

                if (!WorldModelRuntimeManager.TryGet(id, out body) ||
                    body == null)
                {
                    continue;
                }

                alive.Add(id);

                Printer printer;

                if (!Printers.TryGetValue(id, out printer))
                {
                    printer = new Printer { NextPrint = now + PrintEverySeconds };
                    Printers[id] = printer;
                }

                try
                {
                    Tick(runtime, id, body, printer, now, dt);
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Imprimante] " + exception.Message);
                }
            }

            List<string> gone = null;

            foreach (string id in Printers.Keys)
            {
                if (!alive.Contains(id))
                {
                    if (gone == null)
                        gone = new List<string>();

                    gone.Add(id);
                }
            }

            if (gone != null)
            {
                foreach (string id in gone)
                    Printers.Remove(id);
            }
        }

        private static void Tick(
            EconomyRuntime runtime, string id, GameObject body,
            Printer printer, float now, float dt)
        {
            while (now >= printer.NextPrint)
            {
                printer.NextPrint += PrintEverySeconds;

                if (printer.Stored < CapacityCents)
                {
                    printer.Stored = Math.Min(
                        CapacityCents, printer.Stored + PrintCents);
                }
            }

            Vector3 origin = body.transform.position + Vector3.up * 0.6f;

            foreach (PlayerID player in PlayerIDManager.PlayerIDs)
            {
                if (player == null || !player.IsValid)
                    continue;

                Transform head = PhysicalCashRigAnchorResolver.ResolveBody(player);

                if (head == null)
                    continue;

                bool near = (head.position - origin).magnitude <= ReachMeters
                    + 0.6f;

                float held;
                printer.Near.TryGetValue(player.SmallID, out held);

                if (!near)
                {
                    printer.Near.Remove(player.SmallID);
                    continue;
                }

                if (printer.Stored <= 0)
                {
                    if (now >= printer.NextHint)
                    {
                        printer.NextHint = now + 10f;
                        Say(player, "Imprimante vide : prochain billet dans "
                            + Mathf.CeilToInt(printer.NextPrint - now) + " s.", 0);
                    }

                    continue;
                }

                held += dt;
                printer.Near[player.SmallID] = held;

                if (held < HoldSeconds)
                    continue;

                printer.Near[player.SmallID] = 0f;
                Collect(runtime, id, printer, player);
            }
        }

        private static void Collect(
            EconomyRuntime runtime, string id, Printer printer, PlayerID player)
        {
            string pid;

            if (!PlayerIdentityService.TryGetPersistentId(player, out pid))
                return;

            long amount = printer.Stored;

            if (amount <= 0)
                return;

            bool police = string.Equals(
                EconomyJobResolver.GetJobIdFromPlayer(player),
                "police", StringComparison.OrdinalIgnoreCase);

            long pay = police
                ? (long)(amount * PoliceBonusRate)
                : amount;

            string error;

            if (pay > 0 &&
                !runtime.Wallets.TryGiveCash(
                    true,
                    "printer:" + id + ":" + Guid.NewGuid().ToString("N"),
                    pid,
                    pay,
                    out error))
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Imprimante] Versement refusé : " + error);
                return;
            }

            printer.Stored = 0;

            Say(player, police
                ? "Imprimante saisie : prime de "
                    + BankService.FormatAmount(pay) + " (le reste est détruit)."
                : "Vous récupérez " + BankService.FormatAmount(pay)
                    + " dans le bac de l'imprimante.", 1);
        }

        public static void Reset()
        {
            Printers.Clear();
        }

        private static void Say(PlayerID player, string text, byte level)
        {
            try
            {
                if (player.IsMe)
                    RpHub.Notify(text, level);
                else
                    RpHub.PushNotice(player, text, level);
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Imprimante] Message : " + exception.Message);
            }
        }
    }
}
