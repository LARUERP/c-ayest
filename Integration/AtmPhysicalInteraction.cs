using System;
using System.Collections.Generic;
using FusionDarkRP.Economy;
using Il2CppSLZ.Bonelab;
using Il2CppTMPro;
using LabFusion.Player;
using LabFusion.UI;
using LabFusion.Utilities;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace FusionDarkRP.Integration
{
    /// <summary>
    /// Interface physique des distributeurs Fusion DarkRP.
    /// L'identifiant exact de la carte est transmis au serveur.
    /// Le serveur contrôle le propriétaire avant toute opération.
    /// </summary>
    public static class AtmPhysicalInteraction
    {
        private const float ScanInterval = 1f;

        private static readonly Dictionary<string, AtmScreen> Screens =
            new Dictionary<string, AtmScreen>(StringComparer.Ordinal);

        private static bool initialized;
        private static float nextScan;

        public static bool IsInitialized => initialized;

        public static void Initialize()
        {
            if (initialized)
                return;

            try
            {
                Screens.Clear();

                AtmNetwork.ResponseReceived -= OnAtmResponse;
                AtmNetwork.ResponseReceived += OnAtmResponse;

                initialized = true;
                nextScan = 0f;

                MelonLogger.Msg(
                    "[FusionDarkRP ATM] Interaction physique initialisee."
                );
            }
            catch (Exception exception)
            {
                initialized = false;

                MelonLogger.Error(
                    "[FusionDarkRP ATM] Initialisation physique : "
                    + exception
                );
            }
        }

        public static void Update()
        {
            if (!initialized)
                return;

            try
            {
                if (Time.realtimeSinceStartup >= nextScan)
                {
                    nextScan =
                        Time.realtimeSinceStartup + ScanInterval;

                    ScanAtms();
                }

                foreach (AtmScreen screen in Screens.Values)
                {
                    if (screen != null)
                        screen.UpdateCardReader();
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(
                    "[FusionDarkRP ATM] Mise a jour physique : "
                    + exception
                );
            }
        }

        private static void ScanAtms()
        {
            if (!WorldModelRuntimeManager.IsInitialized)
                return;

            string[] objectIds =
                WorldModelRuntimeManager.GetSpawnedObjectIds();

            HashSet<string> found =
                new HashSet<string>(StringComparer.Ordinal);

            foreach (string objectId in objectIds)
            {
                if (string.IsNullOrWhiteSpace(objectId))
                    continue;

                string modelId;

                if (!WorldModelRuntimeManager.TryGetModelId(
                    objectId,
                    out modelId))
                    continue;

                if (!string.Equals(
                    modelId,
                    "ATM",
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                GameObject atmObject;

                if (!WorldModelRuntimeManager.TryGet(
                    objectId,
                    out atmObject))
                    continue;

                if (atmObject == null)
                    continue;

                found.Add(objectId);

                if (Screens.ContainsKey(objectId))
                    continue;

                try
                {
                    AtmScreen screen = new AtmScreen(atmObject);

                    Screens.Add(objectId, screen);

                    MelonLogger.Msg(
                        "[FusionDarkRP ATM] Ecran et lecteur raccordes : "
                        + objectId
                    );
                }
                catch (Exception exception)
                {
                    MelonLogger.Error(
                        "[FusionDarkRP ATM] Creation impossible : "
                        + exception
                    );
                }
            }

            List<string> removed = new List<string>();

            foreach (KeyValuePair<string, AtmScreen> entry in Screens)
            {
                if (!found.Contains(entry.Key))
                    removed.Add(entry.Key);
            }

            foreach (string objectId in removed)
            {
                AtmScreen screen;

                if (Screens.TryGetValue(objectId, out screen))
                {
                    screen.Dispose();
                    Screens.Remove(objectId);
                }
            }
        }

        private static void OnAtmResponse(
            AtmNetworkResponseData response)
        {
            if (!initialized || response == null)
                return;

            foreach (AtmScreen screen in Screens.Values)
            {
                if (screen == null)
                    continue;

                try
                {
                    screen.ApplyResponse(response);
                }
                catch (Exception exception)
                {
                    MelonLogger.Error(
                        "[FusionDarkRP ATM] Affichage reponse : "
                        + exception
                    );
                }
            }
        }

        public static void Shutdown()
        {
            AtmNetwork.ResponseReceived -= OnAtmResponse;

            foreach (AtmScreen screen in Screens.Values)
            {
                if (screen != null)
                    screen.Dispose();
            }

            Screens.Clear();

            initialized = false;
            nextScan = 0f;

            MelonLogger.Msg(
                "[FusionDarkRP ATM] Interfaces et lecteurs nettoyes."
            );
        }

        private sealed class AtmScreen
        {
            private const float RequestTimeoutSeconds = 15f;

            private readonly GameObject body;
            private readonly GameObject root;
            private readonly GameObject canvasObject;

            private GameObject content;
            private LaserCursor cursor;

            private bool disposed;
            private bool waiting;
            private bool cardInserted;

            private float nextCardCheck;
            private float requestDeadline;

            private long pendingToken;

            private string activeCardId;

            private string status =
                "Inserez votre carte bancaire.";

            private string cashText = "Especes : --";
            private string bankText = "Compte : --";

            private long lastCash;
            private long lastBank;

            private const float ScreenWidth = 600f;
            private const float ScreenHeight = 1000f;

            // Échelle uniforme (0,00045 m/px) : plus de texte écrasé.
            private const float PhysicalWidth = 0.27f;
            private const float PhysicalHeight = 0.45f;

            private static readonly Vector3 CardSlotLocalPosition =
                new Vector3(-0.065f, 0.3675f, -0.205f);

            private static readonly Vector3 CardSlotHalfExtents =
                new Vector3(0.075f, 0.045f, 0.055f);

            // Position finale de la carte insérée : 6 mm devant la plaque du
            // lecteur (plaque à z = -0,191, centre de détection à z = -0,205).
            private static readonly Vector3 CardInsertedOffset =
                new Vector3(0f, 0f, -0.0088f);

            public AtmScreen(GameObject atm)
            {
                if (atm == null)
                    throw new ArgumentNullException(nameof(atm));

                body = atm;

                root = new GameObject(
                    "FusionDarkRP_ATM_Interface"
                );

                root.transform.SetParent(
                    body.transform,
                    false
                );

                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;

                try
                {
                    canvasObject = new GameObject(
                        "FusionDarkRP_ATM_Canvas"
                    );

                    canvasObject.layer = 5;

                    RectTransform rect =
                        canvasObject.AddComponent<RectTransform>();

                    rect.SetParent(root.transform, false);

                    rect.sizeDelta = new Vector2(
                        ScreenWidth,
                        ScreenHeight
                    );

                    rect.localPosition = new Vector3(
                        0f,
                        1.10f,
                        -0.188f
                    );

                    // Orientation corrigée : le canvas regarde vers
                    // le joueur (l'ancienne rotation de 180° inversait
                    // le texte comme dans un miroir).
                    rect.localRotation = Quaternion.identity;

                    rect.localScale = new Vector3(
                        PhysicalWidth / ScreenWidth,
                        PhysicalHeight / ScreenHeight,
                        PhysicalWidth / ScreenWidth
                    );

                    Canvas canvas =
                        canvasObject.AddComponent<Canvas>();

                    canvas.renderMode = RenderMode.WorldSpace;
                    canvas.sortingOrder = 20;

                    canvasObject.AddComponent<GraphicRaycaster>();

                    Draw();

                    LaserCursorUtilities.CreateLaserCursor(
                        (Action<LaserCursor>)delegate(
                            LaserCursor ready)
                        {
                            if (disposed ||
                                root == null ||
                                ready == null)
                            {
                                if (ready != null)
                                {
                                    UnityEngine.Object.Destroy(
                                        ((Component)ready).gameObject
                                    );
                                }

                                return;
                            }

                            cursor = ready;

                            Transform cursorTransform =
                                ((Component)cursor).transform;

                            cursorTransform.SetParent(
                                root.transform,
                                false
                            );

                            cursorTransform.localPosition =
                                Vector3.zero;

                            cursorTransform.localRotation =
                                Quaternion.identity;

                            UpdateCursorRegion();

                            ((Component)cursor).gameObject
                                .SetActive(true);
                        }
                    );
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            private readonly System.Collections.Generic.HashSet<int>
                ignoredCardColliders =
                    new System.Collections.Generic.HashSet<int>();

            private Collider[] bodyColliders;

            private Collider detectedCardCollider;
            private Il2CppSLZ.Marrow.InteractableHost lockedHost;
            private Rigidbody lockedBody;
            private bool lockedWasKinematic;
            private string ejectedCardId;
            private bool bundleMode;

            /// <summary>
            /// La carte est avalée par la fente : elle est lâchée par la main
            /// (les deux mains restent libres pour l'argent), centrée dans le
            /// lecteur et immobilisée jusqu'au bouton « EJECTER MA CARTE ».
            /// </summary>
            private void LockCard()
            {
                try
                {
                    Collider cardCollider = detectedCardCollider;

                    if (cardCollider == null || body == null)
                        return;

                    Il2CppSLZ.Marrow.InteractableHost host =
                        cardCollider.GetComponentInParent<
                            Il2CppSLZ.Marrow.InteractableHost>();

                    if (host == null || host.Rb == null)
                        return;

                    try
                    {
                        host.ForceDetach(true);
                    }
                    catch
                    {
                        // Déjà lâchée : sans importance.
                    }

                    lockedHost = host;
                    lockedBody = host.Rb;
                    lockedWasKinematic = lockedBody.isKinematic;

                    lockedBody.isKinematic = true;

                    // 1) Carte remise droite : face parallèle à la plaque du
                    //    lecteur, grand côté à l'horizontale, haut vers le haut.
                    // Carte à plat, grand côté dans l'axe de la fente : elle
                    // entre par la tranche dans le lecteur.
                    Quaternion wanted =
                        Quaternion.LookRotation(
                            body.transform.up, body.transform.right);

                    Quaternion correction =
                        wanted *
                        Quaternion.Inverse(
                            cardCollider.transform.rotation);

                    host.transform.rotation =
                        correction * host.transform.rotation;

                    Physics.SyncTransforms();

                    // 2) Carte plaquée contre la plaque, centrée sur la fente.
                    Vector3 center =
                        body.transform.TransformPoint(
                            CardSlotLocalPosition
                            + CardInsertedOffset);

                    host.transform.position +=
                        center - cardCollider.bounds.center;

                    Physics.SyncTransforms();
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[FusionDarkRP ATM] Insertion de la carte : "
                        + exception.Message);
                }
            }

            private void ReleaseCard(bool push)
            {
                try
                {
                    if (lockedBody != null)
                    {
                        lockedBody.isKinematic = lockedWasKinematic;

                        if (push && body != null)
                        {
                            Vector3 center =
                                body.transform.TransformPoint(
                                    CardSlotLocalPosition);

                            Camera camera = Camera.main;

                            Vector3 outward =
                                camera != null
                                    ? camera.transform.position - center
                                    : body.transform.forward;

                            outward.y = 0f;

                            if (outward.sqrMagnitude < 0.0001f)
                                outward = body.transform.forward;

                            lockedBody.velocity = outward.normalized * 0.9f;
                        }
                    }
                }
                catch
                {
                    // L'objet a pu être détruit entre-temps.
                }

                lockedHost = null;
                lockedBody = null;
            }

            private void EjectCard()
            {
                ejectedCardId = activeCardId;
                ReleaseCard(true);
                CloseCardSession();
            }

            /// <summary>
            /// La carte (portée par un support Eder 22) ne doit pas buter
            /// contre le distributeur : sinon la physique repousse le joueur.
            /// Les collisions carte/distributeur sont ignorées dès que la
            /// carte approche ; la détection du lecteur reste active.
            /// </summary>
            private void IgnoreCardCollisions()
            {
                if (bodyColliders == null)
                    bodyColliders =
                        body.GetComponentsInChildren<Collider>(true);

                Vector3 center =
                    body.transform.TransformPoint(CardSlotLocalPosition);

                Collider[] near = Physics.OverlapSphere(center, 0.8f);

                if (near == null)
                    return;

                foreach (Collider candidate in near)
                {
                    if (candidate == null)
                        continue;

                    int id = candidate.GetInstanceID();

                    if (ignoredCardColliders.Contains(id))
                        continue;

                    string ignoredCardId;

                    if (!BankCardHolsterMetadataNetwork.TryGetCardId(
                            candidate.gameObject, out ignoredCardId))
                    {
                        continue;
                    }

                    ignoredCardColliders.Add(id);

                    foreach (Collider atmCollider in bodyColliders)
                    {
                        if (atmCollider != null)
                            Physics.IgnoreCollision(
                                candidate, atmCollider, true);
                    }
                }
            }

            public void UpdateCardReader()
            {
                if (disposed || body == null)
                    return;

                CheckRequestTimeout();

                if (Time.realtimeSinceStartup < nextCardCheck)
                    return;

                nextCardCheck =
                    Time.realtimeSinceStartup + 0.08f;

                try
                {
                    IgnoreCardCollisions();
                }
                catch
                {
                    // La détection de la carte continue sans cette aide.
                }

                string detectedCardId;

                bool detected =
                    TryGetBankCardInReader(out detectedCardId);

                if (detected &&
                    cardInserted &&
                    string.Equals(
                        detectedCardId,
                        activeCardId,
                        StringComparison.Ordinal))
                {
                    return;
                }

                if (!detected)
                    ejectedCardId = null;

                if (detected &&
                    ejectedCardId != null &&
                    string.Equals(
                        detectedCardId,
                        ejectedCardId,
                        StringComparison.Ordinal))
                {
                    return;
                }

                if (!detected && !cardInserted)
                    return;

                CloseCardSession();

                if (!detected)
                    return;

                cardInserted = true;
                activeCardId = detectedCardId;

                LockCard();

                status = "Carte detectee. Lecture du compte...";
                cashText = "Especes : chargement...";
                bankText = "Compte : chargement...";

                Draw();

                RequestBalances();
            }

            private void CheckRequestTimeout()
            {
                if (!waiting ||
                    requestDeadline <= 0f ||
                    Time.realtimeSinceStartup < requestDeadline)
                    return;

                waiting = false;
                pendingToken = 0;
                requestDeadline = 0f;

                status =
                    "Le serveur ne repond pas. Reessayez.";

                MelonLogger.Warning(
                    "[FusionDarkRP ATM] Delai de reponse depasse."
                );

                Draw();
            }

            private void CloseCardSession()
            {
                ReleaseCard(false);

                bool hadCard = cardInserted;

                cardInserted = false;
                waiting = false;
                pendingToken = 0;
                requestDeadline = 0f;
                activeCardId = null;

                lastCash = 0;
                lastBank = 0;

                cashText = "Especes : --";
                bankText = "Compte : --";

                status = "Inserez votre carte bancaire.";

                if (hadCard)
                {
                    MelonLogger.Msg(
                        "[FusionDarkRP ATM] Session fermee : "
                        + "carte retiree ou remplacee."
                    );
                }

                Draw();
            }

            private bool TryGetBankCardInReader(
                out string cardId)
            {
                cardId = null;

                if (body == null)
                    return false;

                Vector3 center =
                    body.transform.TransformPoint(
                        CardSlotLocalPosition
                    );

                Vector3 scale =
                    body.transform.lossyScale;

                Vector3 halfExtents =
                    Vector3.Scale(
                        CardSlotHalfExtents,
                        new Vector3(
                            Mathf.Abs(scale.x),
                            Mathf.Abs(scale.y),
                            Mathf.Abs(scale.z)
                        )
                    );

                string positional;
                Collider positionalCollider;

                if (BankCardHolsterMetadataNetwork.TryFindCardInBox(
                        center,
                        body.transform.rotation,
                        halfExtents,
                        activeCardId,
                        out positional,
                        out positionalCollider))
                {
                    cardId = positional;
                    detectedCardCollider = positionalCollider;
                    return true;
                }

                Collider[] colliders =
                    Physics.OverlapBox(
                        center,
                        halfExtents,
                        body.transform.rotation,
                        ~0,
                        QueryTriggerInteraction.Collide
                    );

                if (colliders == null)
                    return false;

                foreach (Collider collider in colliders)
                {
                    if (collider == null)
                        continue;

                    string detectedCardId;

                    if (!BankCardHolsterMetadataNetwork.TryGetCardId(
                        collider.gameObject,
                        out detectedCardId))
                    {
                        continue;
                    }

                    Guid parsedGuid;

                    if (!Guid.TryParseExact(
                        detectedCardId,
                        "N",
                        out parsedGuid) ||
                        parsedGuid == Guid.Empty)
                    {
                        continue;
                    }

                    cardId = parsedGuid.ToString("N");
                    detectedCardCollider = collider;
                    return true;
                }

                return false;
            }

            private void UpdateCursorRegion()
            {
                if (cursor == null ||
                    canvasObject == null)
                    return;

                cursor.regions =
                    new LaserCursor.CursorRegion[]
                    {
                        new LaserCursor.CursorRegion
                        {
                            bounds = new Bounds(
                                Vector3.zero,
                                Vector3.Scale(
                                    new Vector3(
                                        ScreenWidth,
                                        ScreenHeight,
                                        80f
                                    ),
                                    canvasObject.transform.lossyScale
                                )
                            ),
                            center = canvasObject.transform
                        }
                    };
            }

            private void Draw()
            {
                if (disposed || canvasObject == null)
                    return;

                if (content != null)
                {
                    content.SetActive(false);
                    UnityEngine.Object.Destroy(content);
                }

                content = new GameObject("ATM_PAGE");
                content.layer = 5;

                RectTransform pageRect =
                    content.AddComponent<RectTransform>();

                pageRect.SetParent(
                    canvasObject.transform,
                    false
                );

                pageRect.sizeDelta = new Vector2(
                    ScreenWidth,
                    ScreenHeight
                );

                Image background =
                    Element("Background", 0f, 0f, 600f, 1000f)
                        .AddComponent<Image>();

                background.color = new Color(
                    0.025f,
                    0.045f,
                    0.075f,
                    1f
                );

                background.raycastTarget = false;

                Label(
                    "FUSION BANK",
                    0f,
                    410f,
                    550f,
                    70f,
                    48f,
                    new Color(0.25f, 0.85f, 1f)
                );

                Label(
                    "DISTRIBUTEUR AUTOMATIQUE",
                    0f,
                    345f,
                    550f,
                    45f,
                    23f,
                    Color.white
                );

                if (!cardInserted)
                {
                    Panel(
                        0f,
                        70f,
                        510f,
                        230f,
                        new Color(0.07f, 0.11f, 0.17f)
                    );

                    Label(
                        "INSEREZ VOTRE CARTE",
                        0f,
                        105f,
                        470f,
                        65f,
                        31f,
                        new Color(0.25f, 0.85f, 1f)
                    );

                    Label(
                        "Placez votre carte bancaire physique dans la fente.",
                        0f,
                        35f,
                        460f,
                        75f,
                        21f,
                        Color.white
                    );

                    Label(
                        status,
                        0f,
                        -100f,
                        520f,
                        65f,
                        22f,
                        new Color(1f, 0.85f, 0.35f)
                    );

                    UpdateCursorRegion();
                    return;
                }

                Panel(
                    0f,
                    205f,
                    520f,
                    175f,
                    new Color(0.07f, 0.11f, 0.17f)
                );

                Label(
                    cashText,
                    0f,
                    245f,
                    470f,
                    45f,
                    27f,
                    Color.white
                );

                Label(
                    bankText,
                    0f,
                    175f,
                    470f,
                    45f,
                    29f,
                    new Color(0.25f, 0.9f, 0.55f)
                );

                Label(
                    status,
                    0f,
                    95f,
                    520f,
                    65f,
                    22f,
                    new Color(1f, 0.85f, 0.35f)
                );

                Color withdrawColor =
                    new Color(0.13f, 0.25f, 0.48f);

                Label(
                    "DEPOT PHYSIQUE : placez vos billets dans la fente.",
                    0f,
                    15f,
                    520f,
                    45f,
                    19f,
                    new Color(0.25f, 0.9f, 0.55f)
                );

                Button(
                    (bundleMode ? "LIASSE 5 EUR" : "RETIRER 5 EUR"),
                    -110f, -70f, 215f, 70f,
                    () => RequestOperation(
                        BankOperationType.Withdraw,
                        bundleMode ? 500 * 50L : 500L),
                    withdrawColor
                );

                Button(
                    (bundleMode ? "LIASSE 10 EUR" : "RETIRER 10 EUR"),
                    110f, -70f, 215f, 70f,
                    () => RequestOperation(
                        BankOperationType.Withdraw,
                        bundleMode ? 1000 * 50L : 1000L),
                    withdrawColor
                );

                Button(
                    (bundleMode ? "LIASSE 20 EUR" : "RETIRER 20 EUR"),
                    -110f, -155f, 215f, 70f,
                    () => RequestOperation(
                        BankOperationType.Withdraw,
                        bundleMode ? 2000 * 50L : 2000L),
                    withdrawColor
                );

                Button(
                    (bundleMode ? "LIASSE 50 EUR" : "RETIRER 50 EUR"),
                    110f, -155f, 215f, 70f,
                    () => RequestOperation(
                        BankOperationType.Withdraw,
                        bundleMode ? 5000 * 50L : 5000L),
                    withdrawColor
                );

                Button(
                    (bundleMode ? "LIASSE 100 EUR" : "RETIRER 100 EUR"),
                    0f, -240f, 440f, 70f,
                    () => RequestOperation(
                        BankOperationType.Withdraw,
                        bundleMode ? 10000 * 50L : 10000L),
                    withdrawColor
                );

                Button(
                    "EJECTER MA CARTE",
                    0f, -325f, 520f, 70f,
                    EjectCard,
                    new Color(0.5f, 0.18f, 0.18f)
                );

                Button(
                    "ACTUALISER",
                    -130f, -410f, 255f, 70f,
                    RequestBalances,
                    new Color(0.12f, 0.3f, 0.48f)
                );

                Button(
                    bundleMode
                        ? "MODE : LIASSES x50"
                        : "MODE : BILLETS",
                    130f, -410f, 255f, 70f,
                    () =>
                    {
                        bundleMode = !bundleMode;
                        Draw();
                    },
                    new Color(0.35f, 0.28f, 0.1f)
                );

                Label(
                    "EJECTER pour reprendre votre carte.",
                    0f, -478f, 520f, 36f, 17f,
                    new Color(0.75f, 0.8f, 0.9f)
                );

                UpdateCursorRegion();
            }

            private GameObject Element(
                string name,
                float x,
                float y,
                float width,
                float height)
            {
                GameObject element = new GameObject(name);
                element.layer = 5;

                RectTransform rect =
                    element.AddComponent<RectTransform>();

                rect.SetParent(content.transform, false);

                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);

                rect.anchoredPosition = new Vector2(x, y);
                rect.sizeDelta = new Vector2(width, height);

                return element;
            }

            private void Label(
                string value,
                float x,
                float y,
                float width,
                float height,
                float fontSize,
                Color color)
            {
                TextMeshProUGUI label =
                    Element(
                        "ATM_Label",
                        x,
                        y,
                        width,
                        height
                    ).AddComponent<TextMeshProUGUI>();

                label.font = TMP_Settings.defaultFontAsset;
                label.text = value;
                label.fontSize = fontSize;
                label.color = color;
                label.richText = false;
                label.enableWordWrapping = true;
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
            }

            private void Panel(
                float x,
                float y,
                float width,
                float height,
                Color color)
            {
                Image image =
                    Element(
                        "ATM_Panel",
                        x,
                        y,
                        width,
                        height
                    ).AddComponent<Image>();

                image.color = color;
                image.raycastTarget = false;
            }

            private void Button(
                string text,
                float x,
                float y,
                float width,
                float height,
                Action action,
                Color color)
            {
                GameObject buttonObject =
                    Element(
                        "ATM_Button",
                        x,
                        y,
                        width,
                        height
                    );

                Image image =
                    buttonObject.AddComponent<Image>();

                image.color = color;
                image.raycastTarget = false;

                UnityEngine.UI.Button uiButton =
                    buttonObject.AddComponent<UnityEngine.UI.Button>();

                uiButton.targetGraphic = image;

                UIMachineUtilities.AddClickEvent(
                    uiButton,
                    action
                );

                GameObject labelObject =
                    new GameObject("ATM_Button_Label");

                labelObject.layer = 5;

                RectTransform labelRect =
                    labelObject.AddComponent<RectTransform>();

                labelRect.SetParent(
                    buttonObject.transform,
                    false
                );

                labelRect.sizeDelta =
                    new Vector2(width - 12f, height - 8f);

                TextMeshProUGUI label =
                    labelObject.AddComponent<TextMeshProUGUI>();

                label.font = TMP_Settings.defaultFontAsset;
                label.text = text;
                label.fontSize = 23f;
                label.color = Color.white;
                label.richText = false;
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;

                GameObject hitObject =
                    new GameObject("HIT");

                hitObject.layer = 5;

                hitObject.transform.SetParent(
                    buttonObject.transform,
                    false
                );

                BoxCollider collider =
                    hitObject.AddComponent<BoxCollider>();

                collider.size = new Vector3(
                    width,
                    height,
                    12f
                );
            }

            private void RequestBalances()
            {
                if (disposed ||
                    !cardInserted ||
                    string.IsNullOrWhiteSpace(activeCardId))
                    return;

                if (!AtmNetwork.IsReady)
                {
                    status = "Service bancaire indisponible.";
                    Draw();
                    return;
                }

                if (waiting)
                {
                    status = "Une demande est deja en cours.";
                    Draw();
                    return;
                }

                waiting = true;
                requestDeadline = 0f;
                status = "Consultation du compte...";
                Draw();

                // -1 : demande en cours d'émission. L'hôte traite sa propre
                // demande immédiatement ; sa réponse arrive avant le retour
                // de l'appel et ne doit pas être ignorée.
                pendingToken = -1;

                long issued =
                    AtmNetwork.RequestBalances(activeCardId);

                if (issued <= 0)
                {
                    pendingToken = 0;
                    waiting = false;
                    requestDeadline = 0f;
                    status = "Impossible d'envoyer la demande.";
                    Draw();
                    return;
                }

                if (pendingToken == -1)
                {
                    pendingToken = issued;

                    requestDeadline =
                        Time.realtimeSinceStartup
                        + RequestTimeoutSeconds;
                }
            }

            private void RequestOperation(
                BankOperationType operation,
                long amountCents)
            {
                if (disposed ||
                    !cardInserted ||
                    string.IsNullOrWhiteSpace(activeCardId))
                    return;

                if (!AtmNetwork.IsReady)
                {
                    status = "Service bancaire indisponible.";
                    Draw();
                    return;
                }

                if (waiting)
                {
                    status = "Une operation est deja en cours.";
                    Draw();
                    return;
                }

                if (operation != BankOperationType.Deposit &&
                    operation != BankOperationType.Withdraw)
                    return;

                if (amountCents < 100 ||
                    amountCents > 100000000)
                    return;

                waiting = true;
                requestDeadline = 0f;

                status =
                    (operation == BankOperationType.Deposit
                        ? "Depot"
                        : "Retrait")
                    + " en attente de validation serveur...";

                Draw();

                pendingToken = -1;

                long issued =
                    AtmNetwork.RequestOperation(
                        operation,
                        amountCents,
                        activeCardId
                    );

                if (issued <= 0)
                {
                    pendingToken = 0;
                    waiting = false;
                    requestDeadline = 0f;
                    status = "Impossible d'envoyer la demande.";
                    Draw();
                    return;
                }

                if (pendingToken == -1)
                {
                    pendingToken = issued;

                    requestDeadline =
                        Time.realtimeSinceStartup
                        + RequestTimeoutSeconds;
                }
            }

            public void ApplyResponse(
                AtmNetworkResponseData response)
            {
                if (disposed ||
                    response == null ||
                    !cardInserted ||
                    !waiting ||
                    pendingToken == 0 ||
                    (pendingToken > 0 &&
                        response.Token != pendingToken))
                    return;

                waiting = false;
                pendingToken = 0;
                requestDeadline = 0f;

                switch (response.Result)
                {
                    case AtmNetworkResults.BalancesAvailable:
                        if (response.Success == 0)
                        {
                            status =
                                "Carte refusee ou compte indisponible.";
                            break;
                        }

                        lastCash = response.CashCents;
                        lastBank = response.BankCents;

                        cashText =
                            "Especes : " + FormatMoney(lastCash);

                        bankText =
                            "Banque : " + FormatMoney(lastBank);

                        status = "Compte bancaire charge.";
                        break;

                    case AtmNetworkResults.OperationCompleted:
                        if (response.Success == 0)
                        {
                            status = "Operation refusee.";
                            break;
                        }

                        lastCash = response.CashCents;
                        lastBank = response.BankCents;

                        cashText =
                            "Especes : " + FormatMoney(lastCash);

                        bankText =
                            "Banque : " + FormatMoney(lastBank);

                        status =
                            (response.Operation ==
                             (byte)BankOperationType.Deposit
                                ? "Depot confirme : "
                                : "Retrait confirme : ")
                            + FormatMoney(response.AmountCents);

                        break;

                    case AtmNetworkResults.Failed:
                        status =
                            "Carte refusee ou operation indisponible.";
                        break;

                    default:
                        return;
                }

                Draw();
            }

            private static string FormatMoney(long cents)
            {
                return (cents / 100m).ToString(
                    "N2",
                    System.Globalization.CultureInfo.GetCultureInfo(
                        "fr-FR"
                    )
                ) + " EUR";
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;

                cardInserted = false;
                waiting = false;
                pendingToken = 0;
                requestDeadline = 0f;
                activeCardId = null;

                if (cursor != null)
                {
                    UnityEngine.Object.Destroy(
                        ((Component)cursor).gameObject
                    );

                    cursor = null;
                }

                if (root != null)
                    UnityEngine.Object.Destroy(root);
            }
        }
    }
}
