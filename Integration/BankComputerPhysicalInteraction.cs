using System;
using System.Collections;
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
public static class BankComputerPhysicalInteraction
{
private const float ScanInterval = 1f;
private const int CustomersPerPage = 5;
private static readonly Dictionary<string, BankComputerScreen> Screens =
new Dictionary<string, BankComputerScreen>(StringComparer.Ordinal);
private static readonly HashSet<byte> BankerGrades =
new HashSet<byte> { 4, 5, 6, 7, 32 };
private static bool initialized;
private static float nextScan;
private static byte selectedCustomerId;
private static bool hasSelectedCustomer;
private static int customerPage;
private static long activeConsentToken;
private static long activeOperationToken;
private static bool consentPending;
private static bool operationPending;
private static bool operationDecisionSent;
private static long customerCash;
private static long customerBank;
private static string globalStatus =
"Bienvenue dans Fusion Bank.";
public static bool IsInitialized => initialized;
public static void Initialize()
{
if (initialized)
return;
try
{
if (!BankNetwork.IsInitialized)
{
MelonLogger.Warning(
"[FusionDarkRP Bank PC] BankNetwork indisponible."
);
return;
}
Screens.Clear();
BankNetwork.ResponseReceived -= OnBankResponse;
BankNetwork.ResponseReceived += OnBankResponse;
initialized = true;
nextScan = 0f;
MelonLogger.Msg(
"[FusionDarkRP Bank PC] Systeme physique initialise."
);
}
catch (Exception exception)
{
initialized = false;
MelonLogger.Error(
"[FusionDarkRP Bank PC] Initialisation : "
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
ScanDesks();
}
}
catch (Exception exception)
{
MelonLogger.Error(
"[FusionDarkRP Bank PC] Mise a jour : "
+ exception
);
}
}
public static void Shutdown()
{
BankNetwork.ResponseReceived -= OnBankResponse;
foreach (BankComputerScreen screen in Screens.Values)
{
if (screen != null)
screen.Dispose();
}
Screens.Clear();
selectedCustomerId = 0;
hasSelectedCustomer = false;
customerPage = 0;
activeConsentToken = 0;
activeOperationToken = 0;
consentPending = false;
operationPending = false;
operationDecisionSent = false;
customerCash = 0;
customerBank = 0;
globalStatus = "Bienvenue dans Fusion Bank.";
initialized = false;
nextScan = 0f;
MelonLogger.Msg(
"[FusionDarkRP Bank PC] Interfaces nettoyees."
);
}
private static void ScanDesks()
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
"BankOfficeDesk",
StringComparison.OrdinalIgnoreCase))
continue;
GameObject desk;
if (!WorldModelRuntimeManager.TryGet(
objectId,
out desk))
continue;
if (desk == null)
continue;
found.Add(objectId);
if (Screens.ContainsKey(objectId))
continue;
try
{
BankComputerScreen screen =
new BankComputerScreen(desk);
Screens.Add(objectId, screen);
MelonLogger.Msg(
"[FusionDarkRP Bank PC] Terminal installe : "
+ objectId
);
}
catch (Exception exception)
{
MelonLogger.Error(
"[FusionDarkRP Bank PC] Creation terminal : "
+ exception
);
}
}
List<string> removed = new List<string>();
foreach (KeyValuePair<string, BankComputerScreen> entry
in Screens)
{
if (!found.Contains(entry.Key))
removed.Add(entry.Key);
}
foreach (string objectId in removed)
{
BankComputerScreen screen;
if (Screens.TryGetValue(objectId, out screen))
{
screen.Dispose();
Screens.Remove(objectId);
}
}
}
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
BankerGrades.Contains(gradeId);
}
catch
{
return false;
}
}
private static void RefreshAll()
{
foreach (BankComputerScreen screen in Screens.Values)
{
if (screen != null)
screen.Draw();
}
}
private static void SetStatus(string value)
{
globalStatus = value ?? "";
RefreshAll();
}
private static void RequestCustomerConsent(byte customerId)
{
if (!IsLocalBanker())
{
SetStatus(
"Acces reserve aux banquiers autorises."
);
return;
}
if (!BankNetwork.IsInitialized)
{
SetStatus("Reseau bancaire indisponible.");
return;
}
if (consentPending || activeConsentToken > 0)
{
SetStatus(
"Une session bancaire est deja active."
);
return;
}
selectedCustomerId = customerId;
hasSelectedCustomer = true;
activeConsentToken = 0;
activeOperationToken = 0;
consentPending = true;
operationPending = false;
operationDecisionSent = false;
customerCash = 0;
customerBank = 0;
SetStatus(
"Demande de consentement envoyee au client."
);
BankNetwork.RequestConsent(customerId);
}
private static void RequestCustomerBalances()
{
if (!IsLocalBanker() ||
!hasSelectedCustomer ||
activeConsentToken <= 0)
return;
if (!BankNetwork.IsInitialized)
{
SetStatus("Reseau bancaire indisponible.");
return;
}
SetStatus("Consultation du compte en cours...");
BankNetwork.RequestBalances(
selectedCustomerId,
activeConsentToken
);
}
private static void RequestCustomerOperation(
BankOperationType operation,
long amountCents)
{
if (!IsLocalBanker() ||
!hasSelectedCustomer ||
activeConsentToken <= 0)
return;
if (operationPending)
{
SetStatus("Une operation est deja en attente.");
return;
}
if (operation != BankOperationType.Deposit &&
operation != BankOperationType.Withdraw)
return;
if (amountCents < 100 ||
amountCents > 100000000)
return;
if (!BankNetwork.IsInitialized)
{
SetStatus("Reseau bancaire indisponible.");
return;
}
operationPending = true;
activeOperationToken = 0;
operationDecisionSent = false;
SetStatus(
"Operation envoyee. Confirmation obligatoire du client."
);
BankNetwork.RequestOperation(
selectedCustomerId,
activeConsentToken,
operation,
amountCents
);
}
private static void CloseSession()
{
selectedCustomerId = 0;
hasSelectedCustomer = false;
customerPage = 0;
activeConsentToken = 0;
activeOperationToken = 0;
consentPending = false;
operationPending = false;
operationDecisionSent = false;
customerCash = 0;
customerBank = 0;
SetStatus("Session bancaire fermee.");
}
private static void OnBankResponse(
BankNetworkResponseData response)
{
if (!initialized || response == null)
return;
try
{
BankNetworkResult result =
(BankNetworkResult)response.Result;
bool localBanker = IsLocalBanker();
switch (result)
{
case BankNetworkResult.ConsentRequested:
if (!localBanker)
{
activeConsentToken = response.Token;
SetStatus(
"Un banquier demande ton autorisation "
+ "pour consulter ton compte."
);
}
break;
case BankNetworkResult.ConsentAccepted:
if (localBanker)
{
activeConsentToken = response.Token;
consentPending = false;
SetStatus(
"Autorisation recue. Chargement du compte..."
);
RequestCustomerBalances();
}
else
{
SetStatus(
"Autorisation bancaire accordee."
);
}
break;
case BankNetworkResult.ConsentRefused:
activeConsentToken = 0;
consentPending = false;
SetStatus(
"Le client a refuse la consultation."
);
break;
case BankNetworkResult.BalancesAvailable:
if (!localBanker ||
response.Token != activeConsentToken)
break;
customerCash = response.CashCents;
customerBank = response.BankCents;
SetStatus(
"Compte client charge. Choisissez une operation."
);
break;
case BankNetworkResult.OperationRequested:
activeOperationToken = response.Token;
operationDecisionSent = false;
operationPending = true;
if (!localBanker)
{
string operationName =
response.Operation ==
(byte)BankOperationType.Deposit
? "depot"
: "retrait";
SetStatus(
"Confirmation requise : "
+ operationName
+ " de "
+ FormatMoney(response.AmountCents)
+ "."
);
}
else
{
SetStatus(
"Operation en attente de confirmation du client."
);
}
break;
case BankNetworkResult.OperationCompleted:
if (activeOperationToken > 0 &&
response.Token != activeOperationToken)
break;
operationPending = false;
activeOperationToken = 0;
operationDecisionSent = false;
if (response.Success == 1)
{
customerCash = response.CashCents;
customerBank = response.BankCents;
SetStatus(
"Operation confirmee : "
+ FormatMoney(response.AmountCents)
);
}
else
{
SetStatus("Operation refusee.");
}
break;
case BankNetworkResult.OperationRefused:
if (activeOperationToken > 0 &&
response.Token != activeOperationToken)
break;
operationPending = false;
activeOperationToken = 0;
operationDecisionSent = false;
SetStatus(
"Le client a refuse l'operation."
);
break;
case BankNetworkResult.Failed:
if (response.Token != 0 &&
response.Token != activeOperationToken &&
response.Token != activeConsentToken)
break;
consentPending = false;
operationPending = false;
activeOperationToken = 0;
operationDecisionSent = false;
SetStatus(
"La demande bancaire a echoue ou a expire."
);
break;
}
}
catch (Exception exception)
{
MelonLogger.Error(
"[FusionDarkRP Bank PC] Reponse reseau : "
+ exception
);
}
}
private static void ResolveCustomerConsent(bool accepted)
{
if (IsLocalBanker() ||
activeConsentToken <= 0 ||
!BankNetwork.IsInitialized)
return;
long token = activeConsentToken;
BankNetwork.ResolveConsent(token, accepted);
activeConsentToken = 0;
SetStatus(
accepted
? "Autorisation envoyee au banquier."
: "Demande bancaire refusee."
);
}
private static void ResolveCustomerOperation(bool accepted)
{
if (IsLocalBanker() ||
activeOperationToken <= 0 ||
!BankNetwork.IsInitialized ||
operationDecisionSent)
return;
long token = activeOperationToken;
operationDecisionSent = true;
operationPending = true;
BankNetwork.ResolveOperation(token, accepted);
SetStatus(
accepted
? "Confirmation envoyee. Traitement en cours..."
: "Refus envoye. Attente de confirmation..."
);
}
private static IEnumerable<PlayerID> GetConnectedPlayers()
{
List<PlayerID> result = new List<PlayerID>();
try
{
PlayerID local = PlayerIDManager.LocalID;
foreach (PlayerID player in PlayerIDManager.PlayerIDs)
{
if (player == null || !player.IsValid)
continue;
if (local != null &&
local.IsValid &&
player.SmallID == local.SmallID)
continue;
result.Add(player);
}
}
catch (Exception exception)
{
MelonLogger.Error(
"[FusionDarkRP Bank PC] Liste joueurs : "
+ exception
);
}
return result;
}

private static string GetPlayerName(byte id)
{
string name = DarkRPNetwork.GetPlayerName(id);
return string.IsNullOrWhiteSpace(name)
? "Joueur " + id
: name;
}
private static string FormatMoney(long cents)
{
return (cents / 100m).ToString(
"N2",
System.Globalization.CultureInfo.GetCultureInfo("fr-FR")
) + " EUR";
}
private sealed class BankComputerScreen
{
private readonly GameObject body;
private readonly GameObject root;
private readonly GameObject canvasObject;
private GameObject content;
private LaserCursor cursor;
private bool disposed;
private const float ScreenWidth = 900f;
private const float ScreenHeight = 1100f;
private const float PhysicalWidth = 0.36f;
private const float PhysicalHeight = 0.44f;
public BankComputerScreen(GameObject desk)
{
if (desk == null)
throw new ArgumentNullException(nameof(desk));
body = desk;
root = new GameObject(
"FusionDarkRP_BankComputer"
);
root.transform.SetParent(
body.transform,
false
);
root.transform.localPosition =
new Vector3(0f, 1.10f, 0.080f);
root.transform.localRotation =
Quaternion.identity;
root.transform.localScale = Vector3.one;
try
{
canvasObject = new GameObject(
"FusionDarkRP_BankComputer_Canvas"
);
canvasObject.layer = 5;
RectTransform rect =
canvasObject.AddComponent<RectTransform>();
rect.SetParent(root.transform, false);
rect.sizeDelta = new Vector2(
ScreenWidth,
ScreenHeight
);
rect.localPosition = Vector3.zero;
rect.localRotation =
Quaternion.identity;
rect.localScale = new Vector3(
PhysicalWidth / ScreenWidth,
PhysicalHeight / ScreenHeight,
PhysicalWidth / ScreenWidth
);
Canvas canvas =
canvasObject.AddComponent<Canvas>();
canvas.renderMode = RenderMode.WorldSpace;
canvas.sortingOrder = 25;
canvasObject.AddComponent<GraphicRaycaster>();
Draw();
LaserCursorUtilities.CreateLaserCursor(
(Action<LaserCursor>)delegate(LaserCursor ready)
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
public void Draw()
{
if (disposed || canvasObject == null)
return;
if (content != null)
{
content.SetActive(false);
UnityEngine.Object.Destroy(content);
}
content = new GameObject("BANK_PC_PAGE");
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
Element("Background", 0f, 0f, 900f, 1100f)
.AddComponent<Image>();
background.color =
new Color(0.025f, 0.045f, 0.075f, 1f);
background.raycastTarget = false;
Label(
"FUSION BANK",
0f,
475f,
820f,
80f,
58f,
new Color(0.25f, 0.85f, 1f)
);
Label(
"POSTE BANCAIRE",
0f,
405f,
820f,
55f,
30f,
Color.white
);
Label(
globalStatus,
0f,
330f,
820f,
90f,
24f,
new Color(1f, 0.85f, 0.35f)
);
if (!BankNetwork.IsInitialized)
{
Label(
"SERVICE BANCAIRE INDISPONIBLE",
0f,
100f,
800f,
90f,
30f,
Color.red
);
UpdateCursorRegion();
return;
}
if (IsLocalBanker())
DrawBankerPage();
else
DrawCustomerPage();
UpdateCursorRegion();
}
private void DrawBankerPage()
{
Panel(
0f,
150f,
800f,
260f,
new Color(0.07f, 0.11f, 0.17f)
);
Label(
"ESPACE BANQUIER",
0f,
245f,
740f,
55f,
32f,
new Color(0.25f, 0.85f, 1f)
);
if (!hasSelectedCustomer)
{
Label(
"Selectionnez un client connecte :",
0f,
180f,
740f,
45f,
23f,
Color.white
);
List<PlayerID> connectedPlayers =
new List<PlayerID>(GetConnectedPlayers());
int pageCount = Math.Max(
1,
(connectedPlayers.Count + CustomersPerPage - 1)
/ CustomersPerPage
);
if (customerPage < 0)
customerPage = 0;
if (customerPage >= pageCount)
customerPage = pageCount - 1;
int startIndex = customerPage * CustomersPerPage;
int endIndex = Math.Min(
startIndex + CustomersPerPage,
connectedPlayers.Count
);
int visibleIndex = 0;
for (int playerIndex = startIndex;
playerIndex < endIndex;
playerIndex++)
{
PlayerID player = connectedPlayers[playerIndex];
if (player == null || !player.IsValid)
continue;
byte id = player.SmallID;
int buttonIndex = visibleIndex;
Button(
GetPlayerName(id),
0f,
95f - (buttonIndex * 75f),
620f,
62f,
() => RequestCustomerConsent(id),
new Color(0.10f, 0.30f, 0.48f)
);
visibleIndex++;
}
if (connectedPlayers.Count == 0)
{
Label(
"Aucun autre joueur connecte.",
0f,
70f,
740f,
50f,
22f,
Color.white
);
}
else if (pageCount > 1)
{
Label(
"PAGE " + (customerPage + 1)
+ " / " + pageCount,
0f,
-300f,
220f,
48f,
22f,
Color.white
);
if (customerPage > 0)
{
Button(
"PRECEDENT",
-205f,
-300f,
210f,
58f,
() =>
{
customerPage--;
RefreshAll();
},
new Color(0.12f, 0.3f, 0.48f)
);
}
if (customerPage < pageCount - 1)
{
Button(
"SUIVANT",
205f,
-300f,
210f,
58f,
() =>
{
customerPage++;
RefreshAll();
},
new Color(0.12f, 0.3f, 0.48f)
);
}
}
return;
}
Label(
"CLIENT : " + GetPlayerName(selectedCustomerId),
0f,
245f,
740f,
50f,
27f,
Color.white
);
if (activeConsentToken <= 0)
{
Button(
"ANNULER LA DEMANDE",
0f,
110f,
620f,
70f,
CloseSession,
new Color(0.48f, 0.15f, 0.15f)
);
return;
}
Panel(
0f,
-20f,
760f,
160f,
new Color(0.08f, 0.16f, 0.22f)
);
Label(
"ESPECES : " + FormatMoney(customerCash),
0f,
15f,
700f,
45f,
28f,
Color.white
);
Label(
"BANQUE : " + FormatMoney(customerBank),
0f,
-45f,
700f,
45f,
28f,
new Color(0.25f, 0.9f, 0.55f)
);
Button(
"ACTUALISER LE COMPTE",
0f,
-155f,
650f,
65f,
RequestCustomerBalances,
new Color(0.12f, 0.3f, 0.48f)
);
Button(
"DEPOT 10 EUR",
-190f,
-255f,
300f,
65f,
() => RequestCustomerOperation(
BankOperationType.Deposit, 1000),
new Color(0.08f, 0.42f, 0.27f)
);
Button(
"DEPOT 50 EUR",
190f,
-255f,
300f,
65f,
() => RequestCustomerOperation(
BankOperationType.Deposit, 5000),
new Color(0.08f, 0.42f, 0.27f)
);
Button(
"DEPOT 100 EUR",
-190f,
-340f,
300f,
65f,
() => RequestCustomerOperation(
BankOperationType.Deposit, 10000),
new Color(0.08f, 0.42f, 0.27f)
);
Button(
"RETRAIT 10 EUR",
190f,
-340f,
300f,
65f,
() => RequestCustomerOperation(
BankOperationType.Withdraw, 1000),
new Color(0.13f, 0.25f, 0.48f)
);
Button(
"RETRAIT 50 EUR",
-190f,
-425f,
300f,
65f,
() => RequestCustomerOperation(
BankOperationType.Withdraw, 5000),
new Color(0.13f, 0.25f, 0.48f)
);
Button(
"RETRAIT 100 EUR",
190f,
-425f,
300f,
65f,
() => RequestCustomerOperation(
BankOperationType.Withdraw, 10000),
new Color(0.13f, 0.25f, 0.48f)
);
Button(
"FERMER LA SESSION",
0f,
-510f,
650f,
60f,
CloseSession,
new Color(0.48f, 0.15f, 0.15f)
);
}
private void DrawCustomerPage()
{
Panel(
0f,
70f,
780f,
300f,
new Color(0.07f, 0.11f, 0.17f)
);
Label(
"ESPACE CLIENT",
0f,
165f,
740f,
60f,
34f,
new Color(0.25f, 0.85f, 1f)
);
if (activeConsentToken > 0)
{
Label(
"Un banquier souhaite consulter tes soldes.",
0f,
80f,
720f,
70f,
24f,
Color.white
);
Button(
"ACCEPTER",
-165f,
-20f,
270f,
75f,
() => ResolveCustomerConsent(true),
new Color(0.08f, 0.42f, 0.27f)
);
Button(
"REFUSER",
165f,
-20f,
270f,
75f,
() => ResolveCustomerConsent(false),
new Color(0.48f, 0.15f, 0.15f)
);
return;
}
if (activeOperationToken > 0)
{
if (operationDecisionSent)
{
Label(
"Ta reponse a ete envoyee. Attente du resultat bancaire.",
0f,
80f,
720f,
70f,
24f,
Color.white
);
return;
}
Label(
"Le banquier demande ton accord pour cette operation.",
0f,
80f,
720f,
70f,
24f,
Color.white
);
Button(
"CONFIRMER",
-165f,
-20f,
270f,
75f,
() => ResolveCustomerOperation(true),
new Color(0.08f, 0.42f, 0.27f)
);
Button(
"REFUSER",
165f,
-20f,
270f,
75f,
() => ResolveCustomerOperation(false),
new Color(0.48f, 0.15f, 0.15f)
);
return;
}
Label(
"Les demandes de ton banquier apparaitront ici.",
0f,
35f,
740f,
100f,
24f,
Color.white
);
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
"BankPC_Label",
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
"BankPC_Panel",
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
"BankPC_Button",
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
new GameObject("BankPC_Button_Label");
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
public void Dispose()
{
if (disposed)
return;
disposed = true;
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
