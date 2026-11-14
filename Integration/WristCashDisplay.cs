using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using BoneLib;
using FusionDarkRP.Economy;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.SDK.Modules;
using UnityEngine;
using UnityEngine.UI;

namespace FusionDarkRP.Integration
{
    public sealed class WristCashBalanceRequest : INetSerializable
    {
        public byte Version = 1;

        public int? GetSize() { return 1; }

        public void Serialize(INetSerializer serializer)
        {
            serializer.SerializeValue(ref Version);
        }
    }

    public sealed class WristCashBalanceResponse : INetSerializable
    {
        public byte TargetId;
        public long CashCents;

        public int? GetSize() { return 9; }

        public void Serialize(INetSerializer serializer)
        {
            serializer.SerializeValue(ref TargetId);
            serializer.SerializeValue(ref CashCents);
        }
    }

    public sealed class WristCashBalanceRequestHandler : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ServerOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage message)
        {
            return message.Bytes != null && message.Bytes.Length == 1;
        }

        protected override void OnHandleMessage(ReceivedMessage message)
        {
            if (!NetworkInfo.HasServer ||
                !NetworkInfo.IsHost ||
                !message.Sender.HasValue)
                return;

            try
            {
                WristCashBalanceRequest request =
                    message.ReadData<WristCashBalanceRequest>();

                if (request == null || request.Version != 1)
                    return;

                PlayerID player =
                    PlatformIds.GetPlayerID(message.Sender.Value);

                if (player == null || !player.IsValid ||
                    !EconomyRuntimeBootstrap.IsInitialized)
                    return;

                EconomyRuntime runtime =
                    EconomyRuntimeBootstrap.Runtime;

                if (runtime == null || runtime.Wallets == null)
                    return;

                if (!PlayerIdentityService.TryGetPersistentId(
                    player, out string persistentId))
                    return;

                WalletData wallet =
                    runtime.Wallets.GetWallet(persistentId);

                if (wallet == null || wallet.CashCents < 0)
                    return;

                WristCashBalanceResponse response =
                    new WristCashBalanceResponse
                    {
                        TargetId = player.SmallID,
                        CashCents = wallet.CashCents
                    };

                MessageRoute route = new MessageRoute(
                    player.SmallID,
                    NetworkChannel.Reliable
                );

                MessageRelay.RelayModule<
                    WristCashBalanceResponseHandler,
                    WristCashBalanceResponse>(response, route);
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Error(
                    "[FusionDarkRP Wrist] Erreur serveur : " + exception
                );
            }
        }
    }

    public sealed class WristCashBalanceResponseHandler : ModuleMessageHandler
    {
        public override ExpectedReceiverType ExpectedReceiver =>
            ExpectedReceiverType.ClientsOnly;

        protected override bool OnPreRelayMessage(ReceivedMessage message)
        {
            return message.Bytes != null && message.Bytes.Length == 9;
        }

        protected override void OnHandleMessage(ReceivedMessage message)
        {
            try
            {
                if (!message.Sender.HasValue ||
                    message.Sender.Value != 0)
                    return;

                WristCashBalanceResponse response =
                    message.ReadData<WristCashBalanceResponse>();

                if (response == null || response.CashCents < 0)
                    return;

                PlayerID localPlayer = PlayerIDManager.LocalID;

                if (localPlayer == null || !localPlayer.IsValid ||
                    localPlayer.SmallID != response.TargetId)
                    return;

                WristCashDisplay.ReceiveBalance(response.CashCents);
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Error(
                    "[FusionDarkRP Wrist] Erreur réception : " + exception
                );
            }
        }
    }

    /// <summary>
    /// Affichage du solde liquide sur le dessus de l'avant-bras gauche.
    /// Le solde bancaire reste sur le téléphone.
    /// </summary>
    public static class WristCashDisplay
    {
        private const float RefreshInterval = 1f;

        // La recherche du squelette est volontairement limitée.
        // Elle ne doit pas être exécutée à chaque frame.
        private const float AnchorCheckInterval = 0.75f;

        private const float CanvasWidth = 800f;
        private const float CanvasHeight = 300f;
        private const float ScreenScale = 0.00014f;

        // Décalage vers l'extérieur du bras pour poser l'écran sur sa surface.
        private const float SurfaceOffset = 0.025f;

        // Si l'os de la main n'est pas trouvé sous celui de l'avant-bras,
        // on utilise une position intermédiaire estimée sur l'os.
        private static readonly Vector3 EstimatedForearmMiddle =
            new Vector3(0f, 0.08f, SurfaceOffset);

        private static readonly Quaternion DisplayLocalRotation =
            Quaternion.Euler(90f, 0f, 0f);

        private const string BackgroundResourceName =
            "FusionDarkRP.Assets.WristBackground.png";

        private const float BackgroundOpacity = 1f;

        private static readonly object Sync = new object();

        private static GameObject root;
        private static GameObject canvasObject;

        private static Text titleText;
        private static Text balanceText;

        private static Transform attachedAnchor;

        private static Texture2D backgroundTexture;
        private static Sprite backgroundSprite;
        private static bool backgroundLoadAttempted;
        private static bool backgroundEverLoaded;

        private static bool initialized;
        private static bool hasBalance;

        private static long cashCents;

        private static float nextRefresh;
        private static float nextAnchorCheck;

        private static string displayStatus = "Chargement...";

        public static bool IsInitialized
        {
            get
            {
                lock (Sync)
                    return initialized;
            }
        }

        public static void Initialize()
        {
            lock (Sync)
            {
                if (initialized)
                    return;

                initialized = true;
                hasBalance = false;
                cashCents = 0;
                nextRefresh = 0f;
                nextAnchorCheck = 0f;
                displayStatus = "Chargement...";
            }

            MelonLoader.MelonLogger.Msg(
                "[FusionDarkRP Wrist] Affichage initialisé."
            );
        }

        public static void Update()
        {
            if (!initialized)
                return;

            try
            {
                UpdateForearmAttachment();
                FollowWristIfNeeded();
                HandleWalletGestures();
                RefreshBalanceIfNeeded();
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Error(
                    "[FusionDarkRP Wrist] Mise à jour impossible : "
                    + exception
                );
            }
        }

        /// <summary>
        /// Recherche périodiquement l'os de l'avant-bras gauche.
        /// Aucun retour à la main : l'écran ne doit jamais être placé
        /// sous la paume ou au niveau des doigts.
        /// </summary>
        private static void UpdateForearmAttachment()
        {
            float now = Time.realtimeSinceStartup;

            if (now < nextAnchorCheck)
                return;

            nextAnchorCheck = now + AnchorCheckInterval;

            PlayerID localPlayer = PlayerIDManager.LocalID;

            if (localPlayer == null || !localPlayer.IsValid)
            {
                DestroyDisplay();
                return;
            }

            Transform forearm =
                PhysicalCashRigAnchorResolver.ResolveLeftForearm(
                    localPlayer
                );

            if (forearm == null)
            {
                DestroyDisplay();
                return;
            }

            if (root != null && attachedAnchor == forearm)
                return;

            DestroyDisplay();
            CreateDisplay(forearm);
        }

        /// <summary>
        /// Calcule une position située entre le coude et le poignet.
        /// On utilise la position de l'os de la main lorsqu'il est
        /// disponible dans la hiérarchie de l'avant-bras.
        /// </summary>
        private static Vector3 CalculateForearmDisplayPosition(
            Transform forearm)
        {
            if (forearm == null)
                return EstimatedForearmMiddle;

            Transform wrist = FindWristOrHandBone(forearm);

            if (wrist == null)
                return EstimatedForearmMiddle;

            Vector3 wristLocalPosition =
                forearm.InverseTransformPoint(wrist.position);

            // 48 % de la distance coude-poignet :
            // l'écran se trouve au milieu de l'avant-bras,
            // légèrement décalé vers le poignet.
            Vector3 middle = Vector3.Lerp(
                Vector3.zero,
                wristLocalPosition,
                0.48f
            );

            // Décalage vers la surface extérieure de l'avant-bras.
            middle += new Vector3(0f, 0f, SurfaceOffset);

            return middle;
        }

        private static Transform FindWristOrHandBone(Transform forearm)
        {
            if (forearm == null)
                return null;

            try
            {
                Transform[] children =
                    forearm.GetComponentsInChildren<Transform>(true);

                if (children == null)
                    return null;

                for (int i = 0; i < children.Length; i++)
                {
                    Transform candidate = children[i];

                    if (candidate == null || candidate == forearm)
                        continue;

                    string name = NormalizeBoneName(candidate.name);

                    if (name == "leftwrist" ||
                        name == "lefthand" ||
                        name == "mixamoriglefthand" ||
                        name == "mixamorigleftwrist" ||
                        name == "handl" ||
                        name == "wristl" ||
                        name == "left_hand" ||
                        name == "left_wrist")
                    {
                        return candidate;
                    }
                }

                // Deuxième recherche plus souple pour les rigs
                // dont les os portent des noms différents.
                for (int i = 0; i < children.Length; i++)
                {
                    Transform candidate = children[i];

                    if (candidate == null || candidate == forearm)
                        continue;

                    string name = NormalizeBoneName(candidate.name);

                    bool leftSide =
                        name.Contains("left") ||
                        name.EndsWith("l");

                    bool handOrWrist =
                        name.Contains("hand") ||
                        name.Contains("wrist");

                    if (leftSide && handOrWrist)
                        return candidate;
                }
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Warning(
                    "[FusionDarkRP Wrist] Recherche du poignet impossible : "
                    + exception.Message
                );
            }

            return null;
        }

        private static string NormalizeBoneName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .ToLowerInvariant()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "")
                .Replace(":", "")
                .Replace(".", "");
        }

        private static bool followWrist;

        // Billets puis pièces : A fait défiler toute la liste.
        private static readonly string[] WithdrawItems =
        {
            "Note_5EUR", "Note_10EUR", "Note_20EUR",
            "Note_50EUR", "Note_100EUR",
            "Coin_1Cent", "Coin_2Cent", "Coin_5Cent",
            "Coin_10Cent", "Coin_20Cent", "Coin_50Cent",
            "Coin_1EUR", "Coin_2EUR"
        };

        private static readonly string[] WithdrawLabels =
        {
            "Billet 5 €", "Billet 10 €", "Billet 20 €",
            "Billet 50 €", "Billet 100 €",
            "Pièce 1 c", "Pièce 2 c", "Pièce 5 c",
            "Pièce 10 c", "Pièce 20 c", "Pièce 50 c",
            "Pièce 1 €", "Pièce 2 €"
        };

        private static int withdrawIndex = 1;
        private const float WithdrawHoldSeconds = 0.7f;
        private static float triggerHeldSince = -1f;
        private static Text hintText;
        private static bool spawnResultHooked;
        private static float nextSpawnAllowed;

        /// <summary>
        /// Main droite près du portefeuille : A change le billet,
        /// la gâchette le sort en billet physique (débité du liquide).
        /// Pour ranger un billet : le saisir, il retourne au portefeuille.
        /// </summary>
        private static float nextStashScan;

        private static readonly System.Collections.Generic.Dictionary<string, float>
            stashRequested =
                new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>
        /// Ranger (geste volontaire) : bouton B (n'importe quelle main) avec un
        /// billet ou une pièce à moins de 22 cm du portefeuille. Sans appui,
        /// l'argent physique reste entièrement manipulable.
        /// </summary>
        private static void StashNearbyCash()
        {
            if (root == null || Time.unscaledTime < nextStashScan)
                return;

            var leftController = BoneLib.Player.LeftController;
            var rightController = BoneLib.Player.RightController;

            bool pressed =
                (rightController != null && rightController.GetBButtonDown()) ||
                (leftController != null && leftController.GetBButtonDown());

            if (!pressed)
                return;

            nextStashScan = Time.unscaledTime + 0.3f;

            Collider[] colliders =
                Physics.OverlapSphere(root.transform.position, 0.22f);

            if (colliders == null)
                return;

            int requested = 0;

            foreach (Collider collider in colliders)
            {
                if (collider == null)
                    continue;

                Transform current = collider.transform;
                string cashId = null;
                int depth = 0;

                while (current != null && depth++ < 16)
                {
                    if (PhysicalCashGrabDetector.TryExtractCashId(
                            current.gameObject.name, out cashId))
                    {
                        break;
                    }

                    cashId = null;
                    current = current.parent;
                }

                if (cashId == null)
                    continue;

                float last;

                if (stashRequested.TryGetValue(cashId, out last) &&
                    Time.unscaledTime - last < 3f)
                {
                    continue;
                }

                stashRequested[cashId] = Time.unscaledTime;
                requested++;

                MelonLoader.MelonLogger.Msg(
                    "[FusionDarkRP Cash] Rangement demandé (bouton B) : " + cashId);

                if (!PhysicalCashPickupNetwork.RequestPickup(cashId))
                {
                    MelonLoader.MelonLogger.Warning(
                        "[FusionDarkRP Cash] Rangement non envoyé : " + cashId);
                }
            }

            if (requested == 0)
            {
                RpHub.Notify(
                    "Rien à ranger : approche un billet ou une pièce du poignet, puis B.",
                    0);
            }
        }

        private static void HandleWalletGestures()
        {
            if (root == null)
                return;

            try
            {
                StashNearbyCash();
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Warning(
                    "[FusionDarkRP Cash] Rangement : " + exception.Message);
            }

            if (!spawnResultHooked)
            {
                spawnResultHooked = true;

                PhysicalCashSpawnNetwork.ResultReceived +=
                    result =>
                    {
                        if (result != null &&
                            !string.IsNullOrWhiteSpace(result.Message))
                        {
                            RpHub.Notify(
                                result.Message,
                                result.Success ? (byte)1 : (byte)2);
                        }
                    };
            }

            var rightHand = BoneLib.Player.RightHand;
            var controller = BoneLib.Player.RightController;

            if (rightHand == null || controller == null)
                return;

            Transform palm =
                rightHand.palmPositionTransform != null
                    ? rightHand.palmPositionTransform
                    : rightHand.transform;

            bool near =
                Vector3.Distance(palm.position, root.transform.position)
                < 0.13f;

            if (hintText != null)
            {
                string hold =
                    near && triggerHeldSince >= 0f
                        ? "  |  " + Mathf.Clamp(
                            (int)((Time.unscaledTime - triggerHeldSince)
                                / WithdrawHoldSeconds * 100f), 0, 100)
                            + " %"
                        : "";

                hintText.text =
                    near
                        ? WithdrawLabels[withdrawIndex]
                          + "  |  A : changer  |  Gâchette maintenue : sortir  |  B : ranger"
                          + hold
                        : "Main droite près du poignet : sortir billets et pièces";
            }

            if (!near)
            {
                triggerHeldSince = -1f;
                return;
            }

            if (controller.GetAButtonDown())
            {
                withdrawIndex =
                    (withdrawIndex + 1) % WithdrawItems.Length;
            }

            // Sortie volontaire : gâchette maintenue ~0,7 s près du poignet,
            // pour ne plus sortir d'argent par accident en attrapant un objet.
            if (!controller.GetPrimaryInteractionButton())
            {
                triggerHeldSince = -1f;
                return;
            }

            if (triggerHeldSince < 0f)
                triggerHeldSince = Time.unscaledTime;

            if (Time.unscaledTime - triggerHeldSince >= WithdrawHoldSeconds &&
                Time.unscaledTime >= nextSpawnAllowed)
            {
                triggerHeldSince = -1f;
                nextSpawnAllowed = Time.unscaledTime + 1.2f;

                if (!PhysicalCashSpawnNetwork.RequestSpawn(
                        WithdrawItems[withdrawIndex],
                        "wallet"))
                {
                    RpHub.Notify(
                        "Sortie d'espèce impossible (serveur indisponible).",
                        2);
                }
            }
        }

        /// <summary>
        /// Quand aucun os d'avant-bras n'existe (rig physique de BONELAB),
        /// l'écran suit le poignet : position estimée à 13 cm derrière la
        /// paume, tourné vers la tête du joueur.
        /// </summary>
        private static void FollowWristIfNeeded()
        {
            if (!followWrist || root == null)
                return;

            var hand = BoneLib.Player.LeftHand;
            Camera camera = Camera.main;

            if (hand == null || camera == null)
                return;

            Transform palm =
                hand.palmPositionTransform != null
                    ? hand.palmPositionTransform
                    : hand.transform;

            Transform head = camera.transform;

            Vector3 handPos = palm.position;

            Vector3 shoulder =
                head.position - head.up * 0.22f - head.right * 0.2f;

            Vector3 elbow =
                Vector3.Lerp(shoulder, handPos, 0.5f)
                + Vector3.down * 0.08f;

            Vector3 direction = handPos - elbow;

            if (direction.sqrMagnitude < 0.0001f)
                return;

            direction.Normalize();

            Vector3 position = handPos - direction * 0.13f;

            Vector3 toHead = head.position - position;
            toHead -= direction * Vector3.Dot(toHead, direction);

            if (toHead.sqrMagnitude < 0.0001f)
                toHead = -head.forward;

            toHead.Normalize();

            Vector3 forward = -toHead;
            Vector3 up = Vector3.Cross(forward, direction);

            if (up.sqrMagnitude < 0.0001f)
                return;

            Quaternion rotation = Quaternion.LookRotation(forward, up);

            float blend = 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);

            root.transform.SetPositionAndRotation(
                Vector3.Lerp(
                    root.transform.position,
                    position + toHead * 0.03f,
                    blend),
                Quaternion.Slerp(
                    root.transform.rotation, rotation, blend));
        }

        private static void CreateDisplay(Transform forearm)
        {
            MelonLoader.MelonLogger.Msg("[FusionDarkRP Wrist] Portefeuille attaché à : " + (forearm != null ? forearm.name : "?"));

            if (forearm == null)
                return;

            root = new GameObject("FusionDarkRP_LeftForearmCash");

            root.transform.SetParent(forearm, false);

            root.transform.localPosition =
                CalculateForearmDisplayPosition(forearm);

            root.transform.localRotation = DisplayLocalRotation;
            root.transform.localScale = Vector3.one;

            attachedAnchor = forearm;

            string anchorName = forearm.name.ToLowerInvariant();

            followWrist =
                !anchorName.Contains("arm") &&
                !anchorName.Contains("hand") &&
                !anchorName.Contains("wrist");

            canvasObject = new GameObject(
                "FusionDarkRP_LeftForearmCash_Canvas"
            );

            canvasObject.layer = 5;

            RectTransform canvasRect =
                canvasObject.AddComponent<RectTransform>();

            canvasRect.SetParent(root.transform, false);
            canvasRect.sizeDelta =
                new Vector2(CanvasWidth, CanvasHeight);

            canvasRect.localPosition = Vector3.zero;
            canvasRect.localRotation = Quaternion.identity;

            canvasRect.localScale = new Vector3(
                ScreenScale,
                ScreenScale,
                ScreenScale
            );

            Canvas canvas =
                canvasObject.AddComponent<Canvas>();

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;

            Image background =
                canvasObject.AddComponent<Image>();

            Sprite walletBackground = LoadWalletBackground();

            if (walletBackground != null)
            {
                background.sprite = walletBackground;
                background.type = Image.Type.Simple;
                background.preserveAspect = false;
                background.color = new Color(
                    1f,
                    1f,
                    1f,
                    BackgroundOpacity
                );

                MelonLoader.MelonLogger.Msg(
                    "[FusionDarkRP Wrist] Image du portefeuille chargée."
                );
            }
            else
            {
                background.color =
                    new Color(0.025f, 0.035f, 0.05f, 0.92f);

                MelonLoader.MelonLogger.Warning(
                    "[FusionDarkRP Wrist] Image introuvable : fond sombre utilisé."
                );
            }

            background.raycastTarget = false;

            // Voile sombre pour garder le texte lisible sur l'image.
            GameObject panelObject =
                new GameObject("FusionDarkRP_Wrist_Panel");

            panelObject.layer = 5;

            RectTransform panelRect =
                panelObject.AddComponent<RectTransform>();

            panelRect.SetParent(canvasObject.transform, false);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(0f, -8f);
            panelRect.sizeDelta = new Vector2(640f, 190f);

            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.55f);
            panelImage.raycastTarget = false;

            titleText = CreateLabel(
                "PORTEFEUILLE",
                26,
                new Vector2(0f, 55f),
                new Vector2(760f, 75f),
                new Color(0.65f, 0.85f, 1f, 1f)
            );

            balanceText = CreateLabel(
                "Chargement...",
                48,
                new Vector2(0f, -35f),
                new Vector2(760f, 125f),
                Color.white
            );

            hintText = CreateLabel(
                "",
                18,
                new Vector2(0f, -100f),
                new Vector2(620f, 40f),
                new Color(0.85f, 0.95f, 1f, 1f)
            );

            DrawBalance();

            MelonLoader.MelonLogger.Msg(
                "[FusionDarkRP Wrist] Écran attaché au milieu de l'avant-bras gauche."
            );
        }

        private static Sprite LoadWalletBackground()
        {
            if (backgroundSprite != null)
                return backgroundSprite;

            if (backgroundLoadAttempted && !backgroundEverLoaded)
                return null;

            backgroundLoadAttempted = true;

            try
            {
                Assembly assembly =
                    Assembly.GetExecutingAssembly();

                using (Stream stream =
                    assembly.GetManifestResourceStream(
                        BackgroundResourceName))
                {
                    if (stream == null)
                    {
                        MelonLoader.MelonLogger.Warning(
                            "[FusionDarkRP Wrist] Ressource absente : "
                            + BackgroundResourceName
                        );

                        return null;
                    }

                    using (MemoryStream memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);

                        byte[] imageBytes = memory.ToArray();

                        if (imageBytes == null || imageBytes.Length == 0)
                            return null;

                        Texture2D texture = new Texture2D(
                            2,
                            2,
                            TextureFormat.RGBA32,
                            false
                        );

                        bool loaded = ImageConversion.LoadImage(
                            texture,
                            imageBytes,
                            false
                        );

                        if (!loaded)
                        {
                            UnityEngine.Object.Destroy(texture);

                            MelonLoader.MelonLogger.Warning(
                                "[FusionDarkRP Wrist] Impossible de décoder le PNG."
                            );

                            return null;
                        }

                        texture.name =
                            "FusionDarkRP_WristBackground_Texture";

                        texture.wrapMode = TextureWrapMode.Clamp;
                        texture.filterMode = FilterMode.Bilinear;

                        backgroundTexture = texture;

                        backgroundEverLoaded = true;
                        backgroundSprite = Sprite.Create(
                            backgroundTexture,
                            new Rect(
                                0f,
                                0f,
                                backgroundTexture.width,
                                backgroundTexture.height
                            ),
                            new Vector2(0.5f, 0.5f)
                        );

                        backgroundSprite.name =
                            "FusionDarkRP_WristBackground_Sprite";

                        return backgroundSprite;
                    }
                }
            }
            catch (Exception exception)
            {
                MelonLoader.MelonLogger.Error(
                    "[FusionDarkRP Wrist] Erreur de chargement de l'image : "
                    + exception
                );

                return null;
            }
        }

        private static Text CreateLabel(
            string value,
            int fontSize,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            GameObject labelObject =
                new GameObject("FusionDarkRP_Wrist_Label");

            labelObject.layer = 5;

            RectTransform rect =
                labelObject.AddComponent<RectTransform>();

            rect.SetParent(canvasObject.transform, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text label = labelObject.AddComponent<Text>();

            label.font =
                Resources.GetBuiltinResource<Font>("Arial.ttf");

            label.text = value;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            return label;
        }

        private static void RefreshBalanceIfNeeded()
        {
            if (Time.realtimeSinceStartup < nextRefresh)
                return;

            nextRefresh =
                Time.realtimeSinceStartup + RefreshInterval;

            if (!NetworkInfo.HasServer)
            {
                displayStatus = "Hors ligne";
                DrawBalance();
                return;
            }

            if (NetworkInfo.IsHost)
            {
                RefreshHostBalance();
                return;
            }

            try
            {
                WristCashBalanceRequest request =
                    new WristCashBalanceRequest();

                MessageRelay.RelayModule<
                    WristCashBalanceRequestHandler,
                    WristCashBalanceRequest>(
                        request,
                        CommonMessageRoutes.ReliableToServer
                    );
            }
            catch (Exception exception)
            {
                displayStatus = "Réseau indisponible";

                MelonLoader.MelonLogger.Warning(
                    "[FusionDarkRP Wrist] Demande impossible : "
                    + exception.Message
                );

                DrawBalance();
            }
        }

        private static void RefreshHostBalance()
        {
            PlayerID localPlayer = PlayerIDManager.LocalID;

            if (localPlayer == null || !localPlayer.IsValid)
            {
                displayStatus = "Joueur indisponible";
                DrawBalance();
                return;
            }

            if (!PlayerIdentityService.TryGetPersistentId(
                localPlayer, out string persistentId))
            {
                displayStatus = "Identité indisponible";
                DrawBalance();
                return;
            }

            EconomyRuntime runtime =
                EconomyRuntimeBootstrap.Runtime;

            if (runtime == null || runtime.Wallets == null)
            {
                displayStatus = "Économie indisponible";
                DrawBalance();
                return;
            }

            WalletData wallet =
                runtime.Wallets.GetWallet(persistentId);

            if (wallet == null)
            {
                displayStatus = "Compte en attente";
                DrawBalance();
                return;
            }

            if (wallet.CashCents < 0)
            {
                displayStatus = "Solde invalide";
                DrawBalance();
                return;
            }

            ReceiveBalance(wallet.CashCents);
        }

        public static void ReceiveBalance(long amountCents)
        {
            if (amountCents < 0)
                return;

            lock (Sync)
            {
                cashCents = amountCents;
                hasBalance = true;
                displayStatus = null;
            }

            DrawBalance();
        }

        private static void DrawBalance()
        {
            if (balanceText == null)
                return;

            if (!hasBalance)
            {
                balanceText.text =
                    string.IsNullOrWhiteSpace(displayStatus)
                        ? "Chargement..."
                        : displayStatus;

                return;
            }

            string formatted =
                (cashCents / 100m).ToString(
                    "N2",
                    CultureInfo.GetCultureInfo("fr-FR")
                );

            balanceText.text = formatted + " €";
        }

        private static void DestroyDisplay()
        {
            if (root != null)
                UnityEngine.Object.Destroy(root);

            root = null;
            canvasObject = null;
            titleText = null;
            balanceText = null;
            attachedAnchor = null;
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                DestroyDisplay();

                if (backgroundSprite != null)
                    UnityEngine.Object.Destroy(backgroundSprite);

                if (backgroundTexture != null)
                    UnityEngine.Object.Destroy(backgroundTexture);

                backgroundSprite = null;
                backgroundTexture = null;
                backgroundLoadAttempted = false;

                initialized = false;
                hasBalance = false;
                cashCents = 0;
                nextRefresh = 0f;
                nextAnchorCheck = 0f;
                displayStatus = "Chargement...";
            }

            MelonLoader.MelonLogger.Msg(
                "[FusionDarkRP Wrist] Affichage arrêté."
            );
        }
    }
}
