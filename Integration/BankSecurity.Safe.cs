using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FusionDarkRP.Economy;
using Il2CppTMPro;
using LabFusion.Player;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace FusionDarkRP.Integration
{
    // ====================================================================
    // Coffre mural PHYSIQUE
    //  - code saisi touche par touche sur un vrai clavier 3D (visez la touche
    //    avec la main, appuyez sur la gâchette comme avec un doigt) ;
    //  - porte sur charnière : on saisit la poignée (gâchette) et on tire / pousse ;
    //  - la porte se verrouille toute seule dès qu'elle est refermée ;
    //  - le contenu est le contenu RÉEL : les objets posés dans la cavité y restent
    //    (corps creux + étagère + porte avec collision).
    // L'hôte reste seul autorité (code haché, essais limités, état diffusé).
    // Géométrie : voir Tools/GenerateModelsV4.py.
    // ====================================================================
    public static partial class BankSecurity
    {
        private const float SafeUnlockSeconds = 12f;
        private const float SafeRelockSeconds = 2f;
        private const float SafeMaxAngle = 115f;
        private const int SafeMaxFails = 3;
        private const float SafeLockoutSeconds = 60f;

        private static readonly Vector3 SafeHinge = new Vector3(-0.41f, 0f, -0.375f);
        private static readonly Vector3 SafeHandle = new Vector3(0.15f, 0.40f, -0.44f);
        private const float SafeKeyCenterX = 0.36f;
        private const float SafeKeyCenterY = 0.31f;
        private const float SafeKeyPitch = 0.033f;
        private const float SafeKeyZ = -0.3735f;

        private static readonly Dictionary<byte, float> LastAngleRequest =
            new Dictionary<byte, float>();

        private static MelonPreferences_Entry<float> safeDirectionEntry;

        private static float SafeSign()
        {
            try
            {
                if (safeDirectionEntry == null)
                {
                    MelonPreferences_Category category =
                        MelonPreferences.GetCategory("FusionDarkRP");

                    if (category == null)
                        category = MelonPreferences.CreateCategory("FusionDarkRP");

                    safeDirectionEntry = category.CreateEntry<float>(
                        "SafeDoorDirection", 1f);
                }

                return safeDirectionEntry.Value >= 0f ? 1f : -1f;
            }
            catch
            {
                return 1f;
            }
        }

        private static bool IsSafeAction(SecurityAction action)
        {
            return action == SecurityAction.Code ||
                   action == SecurityAction.DoorAngle ||
                   action == SecurityAction.ResetCode;
        }

        // ------------------------------------------------------------
        // Persistance (code haché + propriétaire)
        // ------------------------------------------------------------

        internal sealed class SafeRecord
        {
            public string OwnerId { get; set; }
            public string OwnerName { get; set; }
            public string Salt { get; set; }
            public string Hash { get; set; }
            public int Length { get; set; }
        }

        private static readonly object SafeFileSync = new object();
        private static Dictionary<string, SafeRecord> safeRecords;

        private static string SafeFilePath()
        {
            return Path.Combine(
                MelonEnvironment.UserDataDirectory,
                "FusionDarkRP", "wall-safes.json");
        }

        private static void EnsureSafeFile()
        {
            lock (SafeFileSync)
            {
                if (safeRecords != null)
                    return;

                safeRecords = new Dictionary<string, SafeRecord>(StringComparer.Ordinal);

                try
                {
                    string path = SafeFilePath();

                    if (!File.Exists(path))
                        return;

                    Dictionary<string, SafeRecord> read =
                        JsonSerializer.Deserialize<Dictionary<string, SafeRecord>>(
                            File.ReadAllText(path));

                    if (read != null)
                    {
                        foreach (KeyValuePair<string, SafeRecord> pair in read)
                        {
                            if (pair.Value != null)
                                safeRecords[pair.Key] = pair.Value;
                        }
                    }
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Securite] wall-safes.json illisible : "
                        + exception.Message);
                }
            }
        }

        private static string SafeRound(float value)
        {
            return (Mathf.Round(value * 4f) / 4f)
                .ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Clé stable d'un coffre : sa position arrondie à 25 cm. Un coffre reposé
        /// au même endroit retrouve son propriétaire et son code.
        /// </summary>
        private static string SafeKey(string objectId)
        {
            GameObject body;

            if (!WorldModelRuntimeManager.TryGet(objectId, out body) || body == null)
                return null;

            Vector3 p = body.transform.position;

            return "WallSafe@" + SafeRound(p.x) + "," + SafeRound(p.y) + "," + SafeRound(p.z);
        }

        private static void LoadSafeRecord(string objectId, HostState state)
        {
            try
            {
                string key = SafeKey(objectId);
                state.RecordKey = key;

                if (key == null)
                    return;

                EnsureSafeFile();

                SafeRecord record;

                lock (SafeFileSync)
                {
                    if (!safeRecords.TryGetValue(key, out record))
                        return;
                }

                if (record == null || string.IsNullOrEmpty(record.Hash) ||
                    string.IsNullOrEmpty(record.OwnerId))
                {
                    return;
                }

                state.OwnerId = record.OwnerId;
                state.OwnerName = record.OwnerName;
                state.CodeSalt = record.Salt;
                state.CodeHash = record.Hash;
                state.CodeLength = record.Length;
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Securite] Chargement du coffre : " + exception.Message);
            }
        }

        private static void SaveSafeRecord(HostState state)
        {
            try
            {
                if (string.IsNullOrEmpty(state.RecordKey))
                    return;

                EnsureSafeFile();

                lock (SafeFileSync)
                {
                    if (string.IsNullOrEmpty(state.OwnerId) ||
                        string.IsNullOrEmpty(state.CodeHash))
                    {
                        safeRecords.Remove(state.RecordKey);
                    }
                    else
                    {
                        safeRecords[state.RecordKey] = new SafeRecord
                        {
                            OwnerId = state.OwnerId,
                            OwnerName = state.OwnerName,
                            Salt = state.CodeSalt,
                            Hash = state.CodeHash,
                            Length = state.CodeLength
                        };
                    }

                    string path = SafeFilePath();
                    Directory.CreateDirectory(Path.GetDirectoryName(path));

                    string temp = path + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(
                        safeRecords,
                        new JsonSerializerOptions { WriteIndented = true }));

                    if (File.Exists(path))
                        File.Delete(path);

                    File.Move(temp, path);
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Securite] Sauvegarde du coffre : " + exception.Message);
            }
        }

        // ------------------------------------------------------------
        // Code
        // ------------------------------------------------------------

        private static string HashCode(string salt, string code)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(
                    Encoding.UTF8.GetBytes((salt ?? "") + ":" + code));

                return Convert.ToBase64String(bytes);
            }
        }

        /// <summary>Le code voyage sous la forme longueur * 1 000 000 + valeur (zéros de tête conservés).</summary>
        private static bool DecodeCode(long encoded, out string code)
        {
            code = null;

            long length = encoded / 1000000L;
            long value = encoded % 1000000L;

            if (length < 4 || length > 6)
                return false;

            long limit = 1;

            for (int i = 0; i < length; i++)
                limit *= 10;

            if (value < 0 || value >= limit)
                return false;

            code = value.ToString(CultureInfo.InvariantCulture)
                .PadLeft((int)length, '0');

            return true;
        }

        private static void SetSafeCode(HostState state, string code)
        {
            byte[] salt = new byte[12];

            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
                generator.GetBytes(salt);

            state.CodeSalt = Convert.ToBase64String(salt);
            state.CodeHash = HashCode(state.CodeSalt, code);
            state.CodeLength = code.Length;
        }

        private static bool CheckSafeCode(HostState state, string code)
        {
            if (string.IsNullOrEmpty(state.CodeHash) || code == null)
                return false;

            return string.Equals(
                state.CodeHash, HashCode(state.CodeSalt, code),
                StringComparison.Ordinal);
        }

        private static long PayLegacyStock(HostState state, string persistentId)
        {
            try
            {
                string walletId = WalletFor(state);

                if (string.IsNullOrEmpty(walletId) ||
                    !EconomyRuntimeBootstrap.IsInitialized ||
                    EconomyRuntimeBootstrap.Runtime == null)
                {
                    return 0;
                }

                long stock = ReadStock(walletId);

                if (stock <= 0)
                    return 0;

                string error;

                if (EconomyRuntimeBootstrap.Runtime.Wallets.TryMoveBankToWalletCash(
                        true,
                        PhysicalCashRequestId.Create(),
                        walletId,
                        persistentId,
                        stock,
                        out error))
                {
                    return stock;
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[FusionDarkRP Securite] Ancien contenu du coffre : " + exception.Message);
            }

            return 0;
        }

        private static bool DoCode(
            PlayerID sender, HostState state, string persistentId,
            long encoded, out string message, out bool changed)
        {
            changed = false;

            string code;

            if (!DecodeCode(encoded, out code))
            {
                message = "Code invalide : 4 à 6 chiffres.";
                return false;
            }

            if (string.IsNullOrEmpty(persistentId))
            {
                message = "Identité du joueur introuvable.";
                return false;
            }

            float now = Time.realtimeSinceStartup;

            // Coffre libre : le premier code saisi le réserve.
            if (string.IsNullOrEmpty(state.OwnerId) ||
                string.IsNullOrEmpty(state.CodeHash))
            {
                state.OwnerId = persistentId;
                state.OwnerName = PlayerName(sender);
                SetSafeCode(state, code);
                state.Locked = false;
                state.Open = false;
                state.Angle = 0f;
                state.Fails = 0;
                state.UnlockUntil = now + SafeUnlockSeconds;
                SaveSafeRecord(state);

                changed = true;
                message = "Coffre réservé, code enregistré. Retenez-le : "
                    + "il se verrouille tout seul à la fermeture.";
                return true;
            }

            if (now < state.LockoutUntil)
            {
                message = "Clavier bloqué encore "
                    + Mathf.CeilToInt(state.LockoutUntil - now) + " s.";
                return false;
            }

            bool isOwner = string.Equals(
                state.OwnerId, persistentId, StringComparison.Ordinal);

            // Déverrouillé + propriétaire : le code saisi devient le nouveau code.
            if (!state.Locked)
            {
                if (!isOwner)
                {
                    message = "Ce coffre est déjà déverrouillé.";
                    return false;
                }

                SetSafeCode(state, code);
                state.UnlockUntil = now + SafeUnlockSeconds;
                SaveSafeRecord(state);

                message = "Nouveau code enregistré.";
                return true;
            }

            if (CheckSafeCode(state, code))
            {
                state.Fails = 0;
                state.Locked = false;
                state.UnlockUntil = now + SafeUnlockSeconds;
                changed = true;

                message = "Déverrouillé : tirez la poignée.";

                if (isOwner)
                {
                    long legacy = PayLegacyStock(state, persistentId);

                    if (legacy > 0)
                        message += " Ancien solde du coffre versé en liquide : "
                            + BankService.FormatAmount(legacy) + ".";
                }

                return true;
            }

            state.Fails++;

            if (state.Fails >= SafeMaxFails)
            {
                state.Fails = 0;
                state.LockoutUntil = now + SafeLockoutSeconds;
                changed = true;

                Alert(PlayerName(sender)
                    + " s'acharne sur un coffre mural !", 2);

                message = "Trop d'erreurs : clavier bloqué "
                    + (int)SafeLockoutSeconds + " s.";
                return false;
            }

            message = "Code incorrect (" + (SafeMaxFails - state.Fails)
                + " essai(s) restant(s)).";
            return false;
        }

        private static void DoDoorAngle(
            HostState state, long tenths, out bool changed)
        {
            changed = false;

            // Verrou engagé : la porte ne bouge pas.
            if (state.Locked)
            {
                if (state.Angle > 0f || state.Open)
                {
                    state.Angle = 0f;
                    state.Open = false;
                    changed = true;
                }

                return;
            }

            float angle = Mathf.Clamp(tenths / 10f, 0f, SafeMaxAngle);

            if (Mathf.Abs(angle - state.Angle) < 0.4f)
                return;

            bool wasOpen = state.Open;

            state.Angle = angle;
            state.Open = angle > 1.5f;

            if (wasOpen && !state.Open)
                state.UnlockUntil = Time.realtimeSinceStartup + SafeRelockSeconds;

            changed = true;
        }

        private static bool DoResetCode(
            HostState state, bool manage,
            out string message, out bool changed)
        {
            changed = false;

            if (!manage)
            {
                message = "Seul le propriétaire peut libérer ce coffre.";
                return false;
            }

            if (state.Locked)
            {
                message = "Déverrouillez d'abord le coffre avec le code.";
                return false;
            }

            if (state.Open)
            {
                message = "Fermez la porte avant de libérer le coffre.";
                return false;
            }

            state.OwnerId = null;
            state.OwnerName = null;
            state.CodeSalt = null;
            state.CodeHash = null;
            state.CodeLength = 0;
            state.Fails = 0;
            state.Locked = true;
            state.Angle = 0f;
            SaveSafeRecord(state);

            changed = true;
            message = "Coffre libéré : un nouveau code peut être choisi. "
                + "Videz-le avant : le contenu reste dedans.";
            return true;
        }

        /// <summary>Verrouillage automatique : porte refermée depuis quelques secondes.</summary>
        private static void TickSafes()
        {
            List<KeyValuePair<string, HostState>> due =
                new List<KeyValuePair<string, HostState>>();

            float now = Time.realtimeSinceStartup;

            lock (Sync)
            {
                foreach (KeyValuePair<string, HostState> pair in HostStates)
                {
                    HostState state = pair.Value;

                    if (state.Kind == SecurityKind.Safe &&
                        !state.Locked && !state.Open &&
                        now >= state.UnlockUntil)
                    {
                        due.Add(pair);
                    }
                }
            }

            foreach (KeyValuePair<string, HostState> pair in due)
            {
                pair.Value.Locked = true;
                pair.Value.Angle = 0f;
                BroadcastState(pair.Key, pair.Value);
            }
        }

        private static void FillSafeState(SecurityState result, HostState state)
        {
            result.Ang = state.Angle;
            result.CodeSet = !string.IsNullOrEmpty(state.CodeHash);

            float remaining = state.LockoutUntil - Time.realtimeSinceStartup;
            result.Lockout = remaining > 0f ? remaining : 0f;
        }

        private sealed partial class HostState
        {
            public string RecordKey;
            public string CodeSalt;
            public string CodeHash;
            public int CodeLength;
            public int Fails;
            public float LockoutUntil;
            public float Angle;
            public float UnlockUntil;
        }

        // ------------------------------------------------------------
        // Client : clavier, poignée, porte et afficheur
        // ------------------------------------------------------------

        private sealed partial class SecurityObject
        {
            private GameObject doorObject;
            private Transform doorTransform;
            private bool doorFailed;

            private GameObject displayRoot;
            private TextMeshProUGUI displayLabel;
            private bool displayFailed;
            private string displayShown;

            private float doorAngle;
            private float targetAngle;
            private bool codeSet;
            private float lockoutUntilLocal;

            private readonly StringBuilder entry = new StringBuilder();
            private readonly bool[] triggerWas = new bool[2];
            private int grabHand = -1;
            private float lastSentAngle = -999f;
            private float lastSentTime;
            private float nextLockedNotice;
            private float lastKeyTime;
            private float starHeldSince = -1f;
            private int starHand = -1;

            private static bool TryHand(
                bool right, out Vector3 palm, out bool trigger)
            {
                palm = Vector3.zero;
                trigger = false;

                try
                {
                    var hand = right ? BoneLib.Player.RightHand : BoneLib.Player.LeftHand;
                    var controller = right
                        ? BoneLib.Player.RightController
                        : BoneLib.Player.LeftController;

                    if (hand == null)
                        return false;

                    Transform t = hand.palmPositionTransform != null
                        ? hand.palmPositionTransform
                        : hand.transform;

                    palm = t.position;
                    trigger = controller != null &&
                              controller.GetPrimaryInteractionButton();

                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private void TickSafe(float deltaTime)
            {
                EnsureSafeVisuals();

                Camera camera = Camera.main;

                bool near = camera != null && body != null &&
                    Vector3.Distance(
                        camera.transform.position,
                        body.transform.position) < 3.2f;

                if (near)
                {
                    Vector3 palmLeft;
                    Vector3 palmRight;
                    bool triggerLeft;
                    bool triggerRight;

                    bool hasLeft = TryHand(false, out palmLeft, out triggerLeft);
                    bool hasRight = TryHand(true, out palmRight, out triggerRight);

                    HandleKeypad(0, hasLeft, palmLeft, triggerLeft);
                    HandleKeypad(1, hasRight, palmRight, triggerRight);
                    HandleDoor(deltaTime, 0, hasLeft, palmLeft, triggerLeft);
                    HandleDoor(deltaTime, 1, hasRight, palmRight, triggerRight);

                    triggerWas[0] = hasLeft && triggerLeft;
                    triggerWas[1] = hasRight && triggerRight;
                }
                else
                {
                    if (grabHand >= 0)
                        ReleaseDoor();

                    triggerWas[0] = false;
                    triggerWas[1] = false;
                    starHeldSince = -1f;
                }

                if (grabHand < 0)
                {
                    doorAngle = Mathf.MoveTowards(
                        doorAngle, targetAngle, 240f * deltaTime);
                }

                ApplyDoorPose();
                RefreshDisplay();
            }

            // ---------------- Clavier ----------------

            private void HandleKeypad(
                int hand, bool has, Vector3 palm, bool trigger)
            {
                // Maintien de « * » : libération du coffre (déverrouillé).
                if (starHeldSince >= 0f && starHand == hand)
                {
                    if (!has || !trigger)
                    {
                        starHeldSince = -1f;
                    }
                    else if (Time.unscaledTime - starHeldSince >= 2.5f)
                    {
                        starHeldSince = -1f;
                        Send(SecurityAction.ResetCode, 0);
                    }
                }

                // Appui = front montant de la gâchette, main devant la touche.
                if (!has || !trigger || triggerWas[hand] || grabHand >= 0)
                    return;

                float now = Time.unscaledTime;

                if (now - lastKeyTime < 0.2f)
                    return;

                Vector3 local = body.transform.InverseTransformPoint(palm);

                if (local.z > SafeKeyZ + 0.02f || local.z < SafeKeyZ - 0.16f)
                    return;

                int best = -1;
                float bestDistance = 0.026f;

                for (int i = 0; i < 12; i++)
                {
                    float kx = SafeKeyCenterX + ((i % 3) - 1) * SafeKeyPitch;
                    float ky = SafeKeyCenterY + (1.5f - (i / 3)) * SafeKeyPitch;

                    float dx = local.x - kx;
                    float dy = local.y - ky;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }

                if (best < 0)
                    return;

                lastKeyTime = now;
                PressKey(best, hand);
            }

            private void PressKey(int index, int hand)
            {
                if (index == 9)
                {
                    // « * » : efface la saisie ; maintenu 2,5 s : libère le coffre.
                    entry.Length = 0;
                    starHeldSince = Time.unscaledTime;
                    starHand = hand;
                    return;
                }

                if (index == 11)
                {
                    SubmitCode();
                    return;
                }

                int digit = index == 10 ? 0 : index + 1;

                if (entry.Length < 6)
                    entry.Append(digit.ToString(CultureInfo.InvariantCulture));
            }

            private void SubmitCode()
            {
                int length = entry.Length;

                if (length < 4 || length > 6)
                {
                    entry.Length = 0;
                    RpHub.Notify("Le code doit faire 4 à 6 chiffres.", 2);
                    return;
                }

                long value;

                if (!long.TryParse(
                        entry.ToString(), NumberStyles.None,
                        CultureInfo.InvariantCulture, out value))
                {
                    entry.Length = 0;
                    return;
                }

                entry.Length = 0;

                Send(SecurityAction.Code, length * 1000000L + value);
            }

            // ---------------- Porte ----------------

            private void HandleDoor(
                float deltaTime, int hand, bool has, Vector3 palm, bool trigger)
            {
                if (grabHand == hand)
                {
                    if (!has || !trigger)
                    {
                        ReleaseDoor();
                        return;
                    }

                    Vector3 local = body.transform.InverseTransformPoint(palm);

                    float vx = local.x - SafeHinge.x;
                    float vz = local.z - SafeHinge.z;

                    // Trop près de la charnière : angle indéterminé.
                    if (vx * vx + vz * vz < 0.0225f)
                        return;

                    float desired = Mathf.Atan2(-vz, vx) * Mathf.Rad2Deg * SafeSign();
                    desired = Mathf.Clamp(desired, 0f, SafeMaxAngle);

                    if (locked)
                        desired = 0f;

                    doorAngle = Mathf.MoveTowards(doorAngle, desired, 500f * deltaTime);
                    targetAngle = doorAngle;

                    SendAngle(false);
                    return;
                }

                if (!has || !trigger || triggerWas[hand] || grabHand >= 0)
                    return;

                Vector3 handle = doorTransform != null
                    ? doorTransform.TransformPoint(SafeHandle)
                    : body.transform.TransformPoint(SafeHandle);

                if (Vector3.Distance(palm, handle) > 0.13f)
                    return;

                if (locked)
                {
                    float now = Time.unscaledTime;

                    if (now >= nextLockedNotice)
                    {
                        nextLockedNotice = now + 2f;
                        RpHub.Notify(
                            codeSet
                                ? "Coffre verrouillé : saisissez le code sur le clavier."
                                : "Coffre libre : choisissez un code (4 à 6 chiffres) puis #.",
                            2);
                    }

                    return;
                }

                grabHand = hand;
                lastSentAngle = -999f;
            }

            private void ReleaseDoor()
            {
                grabHand = -1;
                SendAngle(true);
                targetAngle = doorAngle;
            }

            private void SendAngle(bool force)
            {
                float now = Time.unscaledTime;

                if (!force &&
                    (now - lastSentTime < 0.08f ||
                     Mathf.Abs(doorAngle - lastSentAngle) < 1f))
                {
                    return;
                }

                lastSentTime = now;
                lastSentAngle = doorAngle;

                Request(
                    objectId, SecurityAction.DoorAngle,
                    (long)Mathf.Round(doorAngle * 10f));
            }

            private void ApplyDoorPose()
            {
                if (doorTransform == null)
                    return;

                Quaternion rotation = Quaternion.AngleAxis(
                    SafeSign() * doorAngle, Vector3.up);

                doorTransform.localRotation = rotation;
                doorTransform.localPosition = SafeHinge - rotation * SafeHinge;
            }

            private void ApplySafeState(SecurityState state)
            {
                codeSet = state.CodeSet;

                lockoutUntilLocal = state.Lockout > 0f
                    ? Time.unscaledTime + state.Lockout
                    : 0f;

                if (grabHand < 0)
                    targetAngle = state.Ang;
            }

            // ---------------- Éléments visuels ----------------

            private void EnsureSafeVisuals()
            {
                if (disposed || body == null)
                    return;

                if (doorObject == null && !doorFailed)
                {
                    GameObject created;
                    string error;

                    if (RuntimeObjWorldObjectFactory.TryCreate(
                            "WallSafeDoor", false, out created, out error) &&
                        created != null)
                    {
                        doorObject = created;
                        doorTransform = created.transform;
                        doorTransform.SetParent(body.transform, false);
                        doorTransform.localPosition = Vector3.zero;
                        doorTransform.localRotation = Quaternion.identity;
                        doorTransform.localScale = Vector3.one;
                    }
                    else
                    {
                        doorFailed = true;

                        MelonLogger.Warning(
                            "[FusionDarkRP Securite] Porte du coffre mural impossible : "
                            + error);
                    }
                }

                if (displayRoot == null && !displayFailed)
                    CreateDisplay();
            }

            private void CreateDisplay()
            {
                try
                {
                    displayRoot = new GameObject("FusionDarkRP_SafeDisplay");
                    displayRoot.transform.SetParent(body.transform, false);
                    displayRoot.transform.localPosition =
                        new Vector3(0.36f, 0.475f, -0.3725f);
                    displayRoot.transform.localRotation = Quaternion.identity;
                    displayRoot.transform.localScale = Vector3.one;

                    GameObject canvasObject =
                        new GameObject("FusionDarkRP_SafeCanvas");

                    canvasObject.layer = 5;

                    RectTransform rect =
                        canvasObject.AddComponent<RectTransform>();

                    rect.SetParent(displayRoot.transform, false);
                    rect.sizeDelta = new Vector2(300f, 140f);
                    rect.localPosition = Vector3.zero;
                    rect.localRotation = Quaternion.identity;

                    float inv = 1f / Mathf.Max(
                        0.0001f, Mathf.Abs(body.transform.lossyScale.x));

                    rect.localScale = new Vector3(
                        0.15f / 300f * inv,
                        0.07f / 140f * inv,
                        0.15f / 300f * inv);

                    Canvas canvas = canvasObject.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.WorldSpace;
                    canvas.sortingOrder = 22;

                    GameObject labelObject = new GameObject("SAFE_TEXT");
                    labelObject.layer = 5;

                    RectTransform labelRect =
                        labelObject.AddComponent<RectTransform>();

                    labelRect.SetParent(canvasObject.transform, false);
                    labelRect.sizeDelta = new Vector2(300f, 140f);

                    displayLabel = labelObject.AddComponent<TextMeshProUGUI>();
                    displayLabel.font = TMP_Settings.defaultFontAsset;
                    displayLabel.text = "";
                    displayLabel.fontSize = 34f;
                    displayLabel.enableAutoSizing = true;
                    displayLabel.fontSizeMin = 16f;
                    displayLabel.fontSizeMax = 44f;
                    displayLabel.richText = false;
                    displayLabel.enableWordWrapping = false;
                    displayLabel.alignment = TextAlignmentOptions.Center;
                    displayLabel.raycastTarget = false;
                }
                catch (Exception exception)
                {
                    displayFailed = true;

                    MelonLogger.Warning(
                        "[FusionDarkRP Securite] Afficheur du coffre mural : "
                        + exception.Message);
                }
            }

            private void RefreshDisplay()
            {
                if (displayLabel == null)
                    return;

                float now = Time.unscaledTime;
                string head;
                Color color;

                if (lockoutUntilLocal > now)
                {
                    head = "BLOQUE " + Mathf.CeilToInt(lockoutUntilLocal - now) + "s";
                    color = new Color(1f, 0.3f, 0.25f);
                }
                else if (!codeSet)
                {
                    head = "LIBRE";
                    color = new Color(0.35f, 0.85f, 1f);
                }
                else if (locked)
                {
                    head = "VERROUILLE";
                    color = new Color(1f, 0.3f, 0.25f);
                }
                else if (doorAngle > 1.5f || open)
                {
                    head = "OUVERT";
                    color = new Color(0.3f, 1f, 0.45f);
                }
                else
                {
                    head = "DEVERROUILLE";
                    color = new Color(0.3f, 1f, 0.45f);
                }

                string second = entry.Length > 0
                    ? new string('*', entry.Length)
                    : (codeSet ? "CODE ?" : "NOUVEAU CODE");

                string shown = head + "\n" + second;

                if (string.Equals(shown, displayShown, StringComparison.Ordinal))
                    return;

                displayShown = shown;
                displayLabel.text = shown;
                displayLabel.color = color;
            }

            private void DisposeSafe()
            {
                try
                {
                    if (displayRoot != null)
                        UnityEngine.Object.Destroy(displayRoot);

                    if (doorObject != null)
                        RuntimeObjWorldObjectFactory.Destroy(doorObject);
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP Securite] Nettoyage du coffre mural : "
                        + exception.Message);
                }

                displayRoot = null;
                displayLabel = null;
                doorObject = null;
                doorTransform = null;
            }
        }
    }
}
