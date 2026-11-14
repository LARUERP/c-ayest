
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FusionDarkRP.Integration;

namespace FusionDarkRP.Telephone;

public sealed class PhoneService
{
    public const int MaxContacts = 100;
    public const int MaxMessages = 100;
    public const int MaxText = 320;

    private const int MaxAccounts = 2048;

    private PhoneDatabase db;
    private Dictionary<ulong, PhoneAccount> accounts;
    private Dictionary<string, PhoneAccount> numbers;
    private readonly string file;

    public int Count => accounts.Count;

    public PhoneService(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
            throw new ArgumentException("Chemin de base téléphone invalide.", nameof(file));

        this.file = file;

        if (File.Exists(file) && new FileInfo(file).Length > 16777216)
            throw new InvalidDataException("Base téléphone trop volumineuse.");

        db = File.Exists(file)
            ? JsonSerializer.Deserialize<PhoneDatabase>(File.ReadAllText(file))
            : new PhoneDatabase();

        Validate();
        Index();

        if (!db.RandomNumbers || db.Accounts.Any(a => a.PinHash != "" && a.PinDigits != 4))
            Migrate();
    }

    private static string RandomNumber(HashSet<string> occupied)
    {
        string number;

        do
        {
            number = "06" + RandomNumberGenerator
                .GetInt32(10000000, 100000000)
                .ToString("D8", CultureInfo.InvariantCulture);
        }
        while (occupied.Contains(number));

        occupied.Add(number);
        return number;
    }

    private void Migrate()
    {
        Commit(() =>
        {
            if (!db.RandomNumbers)
            {
                HashSet<string> occupied =
                    new HashSet<string>(db.Accounts.Select(a => a.Number));

                Dictionary<string, string> replacements =
                    db.Accounts.ToDictionary(
                        a => a.Number,
                        a => RandomNumber(occupied));

                foreach (PhoneAccount account in db.Accounts)
                {
                    account.Number = replacements[account.Number];

                    foreach (PhoneCallEntry call in account.Calls)
                    {
                        if (replacements.TryGetValue(call.Number, out var callValue))
                            call.Number = callValue;
                    }

                    foreach (PhoneContact contact in account.Contacts)
                    {
                        if (replacements.TryGetValue(contact.Number, out var contactValue))
                            contact.Number = contactValue;
                    }

                    foreach (PhoneSms message in account.Messages)
                    {
                        if (replacements.TryGetValue(message.From, out var fromValue))
                            message.From = fromValue;

                        if (replacements.TryGetValue(message.To, out var toValue))
                            message.To = toValue;
                    }

                    foreach (PhoneBankEntry transaction in account.Transactions)
                    {
                        foreach (KeyValuePair<string, string> item in replacements)
                        {
                            if (transaction.Label == "De " + Display(item.Key))
                                transaction.Label = "De " + Display(item.Value);
                            else if (transaction.Label == "Vers " + Display(item.Key))
                                transaction.Label = "Vers " + Display(item.Value);
                        }
                    }
                }

                db.RandomNumbers = true;
            }

            foreach (PhoneAccount account in db.Accounts)
            {
                if (account.PinDigits != 4)
                {
                    account.PinHash = "";
                    account.PinSalt = "";
                    account.PinDigits = 4;
                }
            }
        });
    }

    public static string Normalize(string number)
    {
        if (number == null || number.Length > 40)
            throw new InvalidOperationException("Numéro invalide.");

        string normalized = string.Concat(
            number.Where(c => c != ' ' && c != '-' && c != '.'));

        if (normalized.Length != 10 ||
            normalized.Any(c => c < '0' || c > '9'))
        {
            throw new InvalidOperationException(
                "Saisir un numéro RP de 10 chiffres.");
        }

        return normalized;
    }

    public static string Display(string number)
    {
        return number != null && number.Length == 10
            ? string.Join(" ", Enumerable.Range(0, 5)
                .Select(i => number.Substring(i * 2, 2)))
            : number ?? "";
    }

    private void Index()
    {
        accounts = db.Accounts.ToDictionary(a => a.Player);
        numbers = db.Accounts.ToDictionary(
            a => a.Number,
            StringComparer.Ordinal);
    }

    private void Validate()
    {
        if (db == null ||
            db.Version != 1 ||
            db.NextNumber < 1 ||
            db.NextNumber > 100000000 ||
            db.Accounts == null ||
            db.Accounts.Count > MaxAccounts ||
            db.Accounts.Any(a => a == null))
        {
            throw new InvalidDataException(
                "Base téléphone invalide. Conserver le fichier et vérifier son backup.");
        }

        if (db.Accounts.Select(a => a.Player).Distinct().Count() != db.Accounts.Count ||
            db.Accounts.Select(a => a.Number).Distinct().Count() != db.Accounts.Count)
        {
            throw new InvalidDataException("Identités ou numéros dupliqués.");
        }

        foreach (PhoneAccount account in db.Accounts)
        {
            if (Normalize(account.Number) != account.Number ||
                !account.Number.StartsWith("06") ||
                account.Contacts == null ||
                account.Messages == null ||
                account.RecentCommands == null ||
                account.Contacts.Count > MaxContacts ||
                account.Messages.Count > MaxMessages ||
                account.RecentCommands.Count > 128)
            {
                throw new InvalidDataException("Compte téléphone invalide.");
            }

            if (account.Wallpaper == null ||
                (account.Wallpaper != "" &&
                 !Guid.TryParseExact(account.Wallpaper, "N", out _)))
            {
                throw new InvalidDataException("Fond d’écran invalide.");
            }

            if (account.Calls == null ||
                account.Calls.Count > 50 ||
                account.Calls.Any(c =>
                    c == null ||
                    !Guid.TryParseExact(c.Id, "N", out _) ||
                    c.Number == null ||
                    (c.Number != "" && Normalize(c.Number) != c.Number) ||
                    (c.Number == "" && (!c.Incoming || !c.Masked)) ||
                    c.Seconds < 0 ||
                    c.Seconds > 86400 ||
                    !new[] { "completed", "missed", "cancelled", "declined" }
                        .Contains(c.Outcome)))
            {
                throw new InvalidDataException("Historique appels invalide.");
            }

            if (account.Transactions == null ||
                account.Transactions.Count > 20 ||
                account.Transactions.Any(t =>
                    t == null ||
                    !Guid.TryParseExact(t.Id, "N", out _) ||
                    t.Label == null ||
                    t.Label.Length > 80))
            {
                throw new InvalidDataException("Historique banque invalide.");
            }

            if (account.GalleryDeleted == null ||
                account.GalleryDeleted.Count > 200 ||
                account.GalleryDeleted.Any(id =>
                    !Guid.TryParseExact(id, "N", out _)))
            {
                throw new InvalidDataException("Historique Galerie invalide.");
            }

            if (account.Gallery == null ||
                account.Gallery.Count > 100 ||
                account.Gallery.Any(id =>
                    !Guid.TryParseExact(id, "N", out _)) ||
                account.Gallery.Distinct().Count() != account.Gallery.Count)
            {
                throw new InvalidDataException("Galerie invalide.");
            }

            if (account.PinSalt == null ||
                account.PinHash == null ||
                (account.PinSalt == "") != (account.PinHash == ""))
            {
                throw new InvalidDataException("Code téléphone invalide.");
            }

            foreach (PhoneContact contact in account.Contacts)
            {
                if (contact == null ||
                    contact.Photo == null ||
                    (contact.Photo != "" &&
                     !Guid.TryParseExact(contact.Photo, "N", out _)) ||
                    Normalize(contact.Number) != contact.Number ||
                    string.IsNullOrWhiteSpace(contact.Name) ||
                    contact.Name.Length > 32 ||
                    contact.Name.Any(char.IsControl))
                {
                    throw new InvalidDataException("Contact invalide.");
                }
            }

            if (account.Contacts.Select(c => c.Number).Distinct().Count()
                != account.Contacts.Count)
            {
                throw new InvalidDataException("Contacts dupliqués.");
            }

            foreach (PhoneSms message in account.Messages)
            {
                if (message == null ||
                    message.Voice == null ||
                    (message.Voice != "" &&
                     (!Guid.TryParseExact(message.Voice, "N", out _) ||
                      message.VoiceSeconds < 1 ||
                      message.VoiceSeconds > 15)) ||
                    !Guid.TryParseExact(message.Id, "N", out _) ||
                    Normalize(message.From) != message.From ||
                    Normalize(message.To) != message.To ||
                    (message.From != account.Number &&
                     message.To != account.Number) ||
                    (string.IsNullOrWhiteSpace(message.Text) &&
                     string.IsNullOrEmpty(message.Photo) &&
                     string.IsNullOrEmpty(message.Voice)) ||
                    message.Text.Length > MaxText ||
                    (!string.IsNullOrEmpty(message.Photo) &&
                     !Guid.TryParseExact(message.Photo, "N", out _)))
                {
                    throw new InvalidDataException("Message invalide.");
                }
            }

            if (account.Messages.Select(m => m.Id).Distinct().Count()
                != account.Messages.Count)
            {
                throw new InvalidDataException("Messages dupliqués.");
            }

            if (account.RecentCommands.Any(s =>
                !Guid.TryParseExact(s, "N", out _)))
            {
                throw new InvalidDataException("Commande sauvegardée invalide.");
            }
        }
    }

    private void Save()
    {
        Validate();

        string directory = Path.GetDirectoryName(file);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        byte[] data = JsonSerializer.SerializeToUtf8Bytes(db);

        if (data.Length > 16777216)
            throw new InvalidOperationException("Base téléphone pleine.");

        string temporary = file + ".tmp";

        try
        {
            if (File.Exists(file))
                File.Copy(file, file + ".bak", overwrite: true);

            using (FileStream stream = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private void Commit(Action action)
    {
        byte[] backup = JsonSerializer.SerializeToUtf8Bytes(db);

        try
        {
            action();
            Save();
            Index();
        }
        catch
        {
            db = JsonSerializer.Deserialize<PhoneDatabase>(backup);
            Index();
            throw;
        }
    }

    public PhoneAccount Ensure(ulong id)
    {
        if (accounts.TryGetValue(id, out var account))
            return account;

        if (accounts.Count >= MaxAccounts)
            throw new InvalidOperationException(
                "Capacité des comptes téléphone atteinte.");

        Commit(() =>
        {
            string number = RandomNumber(new HashSet<string>(numbers.Keys));
            db.NextNumber++;

            db.Accounts.Add(new PhoneAccount
            {
                Player = id,
                Number = number,
                PinDigits = 4
            });
        });

        return accounts[id];
    }

    public PhoneChange Apply(ulong id, PhoneCommand command, DateTime utc)
    {
        Ensure(id);

        if (command == null ||
            !Guid.TryParseExact(command.Nonce, "N", out _))
        {
            throw new InvalidOperationException(
                "Identifiant de commande invalide.");
        }

        PhoneAccount account = accounts[id];

        if (account.RecentCommands.Contains(command.Nonce))
        {
            return new PhoneChange
            {
                Duplicate = true,
                Notice = "Action déjà enregistrée."
            };
        }

        PhoneChange change = new PhoneChange();
        string number = Normalize(command.Number);

        Commit(() =>
        {
            switch (command.Operation)
            {
                case "contact":
                {
                    string name = (command.Name ?? "").Trim();

                    if (name.Length < 1 ||
                        name.Length > 32 ||
                        name.Any(char.IsControl))
                    {
                        throw new InvalidOperationException(
                            "Nom de contact : 1 à 32 caractères.");
                    }

                    if (number == account.Number)
                        throw new InvalidOperationException("C'est ton propre numéro.");

                    PhoneContact contact =
                        account.Contacts.FirstOrDefault(c => c.Number == number);

                    if (contact == null)
                    {
                        if (account.Contacts.Count >= MaxContacts)
                            throw new InvalidOperationException("100 contacts maximum.");

                        account.Contacts.Add(new PhoneContact
                        {
                            Number = number,
                            Name = name
                        });
                    }
                    else
                    {
                        contact.Name = name;
                    }

                    change.Notice = "Contact enregistré.";
                    break;
                }

                case "contactPhoto":
                {
                    PhoneContact contact =
                        account.Contacts.FirstOrDefault(c => c.Number == number);

                    if (contact == null)
                        throw new InvalidOperationException(
                            "Enregistrer le contact avant de choisir sa photo.");

                    string photo = command.Photo ?? "";

                    if (photo != "" &&
                        (!Guid.TryParseExact(photo, "N", out _) ||
                         !account.Gallery.Contains(photo)))
                    {
                        throw new InvalidOperationException(
                            "Choisir une photo de la Galerie de ce téléphone.");
                    }

                    contact.Photo = photo;
                    change.Notice = photo == ""
                        ? "Photo du contact retirée."
                        : "Photo du contact enregistrée.";
                    break;
                }

                case "deleteContact":
                    account.Contacts.RemoveAll(c => c.Number == number);
                    change.Notice = "Contact supprimé.";
                    break;

                case "sms":
                {
                    string text = (command.Text ?? "").Trim();

                    if ((text.Length < 1 &&
                         string.IsNullOrEmpty(command.Photo) &&
                         string.IsNullOrEmpty(command.Voice)) ||
                        text.Length > MaxText ||
                        text.Any(c => char.IsControl(c) && c != '\n'))
                    {
                        throw new InvalidOperationException(
                            "SMS : 1 à 320 caractères.");
                    }

                    if (number == account.Number)
                        throw new InvalidOperationException(
                            "Impossible d'envoyer un SMS à soi-même.");

                    if (!numbers.TryGetValue(number, out var recipient))
                        throw new InvalidOperationException(
                            "Ce numéro n'est pas attribué sur ce serveur.");

                    if (!string.IsNullOrEmpty(command.Photo) &&
                        !Guid.TryParseExact(command.Photo, "N", out _))
                    {
                        throw new InvalidOperationException("Pièce jointe invalide.");
                    }

                    if (!string.IsNullOrEmpty(command.Voice) &&
                        !string.IsNullOrEmpty(command.Photo))
                    {
                        throw new InvalidOperationException(
                            "Choisir une photo ou un vocal.");
                    }

                    if (!string.IsNullOrEmpty(command.Voice) &&
                        (!Guid.TryParseExact(command.Voice, "N", out _) ||
                         command.VoiceSeconds < 1 ||
                         command.VoiceSeconds > 15))
                    {
                        throw new InvalidOperationException("Vocal invalide.");
                    }

                    string messageId = Guid.NewGuid().ToString("N");

                    AddMessage(account, new PhoneSms
                    {
                        Id = messageId,
                        From = account.Number,
                        To = number,
                        Text = text,
                        Photo = command.Photo ?? "",
                        Voice = command.Voice ?? "",
                        VoiceSeconds = command.VoiceSeconds,
                        Utc = utc.ToUniversalTime(),
                        Read = true
                    });

                    AddMessage(recipient, new PhoneSms
                    {
                        Id = messageId,
                        From = account.Number,
                        To = number,
                        Text = text,
                        Photo = command.Photo ?? "",
                        Voice = command.Voice ?? "",
                        VoiceSeconds = command.VoiceSeconds,
                        Utc = utc.ToUniversalTime()
                    });

                    change.Recipient = recipient.Player;
                    change.Notice = "SMS enregistré et envoyé.";
                    break;
                }

                case "read":
                    foreach (PhoneSms message in account.Messages.Where(
                        m => m.To == account.Number && m.From == number))
                    {
                        message.Read = true;
                    }

                    change.Notice = "";
                    break;

                default:
                    throw new InvalidOperationException(
                        "Action téléphone inconnue.");
            }

            account.RecentCommands.Add(command.Nonce);

            if (account.RecentCommands.Count > 128)
                account.RecentCommands.RemoveAt(0);
        });

        return change;
    }

    private static void AddMessage(PhoneAccount account, PhoneSms message)
    {
        account.Messages.Add(message);

        while (account.Messages.Count > MaxMessages)
            account.Messages.RemoveAt(0);
    }

    public bool ReserveExternal(ulong actor, string nonce)
    {
        PhoneAccount account = Ensure(actor);

        if (!Guid.TryParseExact(nonce, "N", out _))
            throw new InvalidOperationException("Commande invalide.");

        if (account.RecentCommands.Contains(nonce))
            return false;

        Commit(() =>
        {
            account.RecentCommands.Add(nonce);

            while (account.RecentCommands.Count > 128)
                account.RecentCommands.RemoveAt(0);
        });

        return true;
    }

    /// <summary>
    /// Conservé pour compatibilité avec PhoneMod.
    /// Les virements sont désormais enregistrés uniquement
    /// dans l'historique central Fusion DarkRP.
    /// </summary>
    [Obsolete("L'historique des virements est géré par Fusion DarkRP.")]
    public void BankMovement(
        ulong from,
        ulong to,
        long amount,
        string nonce,
        DateTime utc)
    {
        // Aucun mouvement bancaire local ne doit être créé ici.
    }

    public bool BankPinSet(ulong owner)
    {
        return Ensure(owner).BankPinHash != "";
    }

    public bool VerifyBankPin(ulong owner, string pin)
    {
        PhoneAccount account = Ensure(owner);
        return PhonePin.Verify(
            pin,
            account.BankPinSalt,
            account.BankPinHash);
    }

    public void SetBankPin(ulong owner, string pin)
    {
        PhoneAccount account = Ensure(owner);
        string salt = PhonePin.Salt();
        string hash = PhonePin.Hash(pin, salt);

        Commit(() =>
        {
            account.BankPinSalt = salt;
            account.BankPinHash = hash;
        });
    }

    public bool PinSet(ulong owner)
    {
        return Ensure(owner).PinHash != "";
    }

    public bool VerifyPin(ulong owner, string pin)
    {
        PhoneAccount account = Ensure(owner);
        return PhonePin.Verify(pin, account.PinSalt, account.PinHash);
    }

    public void SetPin(ulong owner, string pin)
    {
        PhoneAccount account = Ensure(owner);
        string salt = PhonePin.Salt();
        string hash = PhonePin.Hash(pin, salt);

        Commit(() =>
        {
            account.PinSalt = salt;
            account.PinHash = hash;
            account.PinDigits = 4;
        });
    }

    public List<string> DeletedPhotos(ulong owner)
    {
        return Ensure(owner).GalleryDeleted.ToList();
    }

    public List<string> Gallery(ulong owner)
    {
        return Ensure(owner).Gallery.ToList();
    }

    public void AddPhoto(ulong owner, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new InvalidOperationException("Photo invalide.");

        PhoneAccount account = Ensure(owner);

        if (!account.GalleryDeleted.Contains(id) &&
            !account.Gallery.Contains(id))
        {
            if (account.Gallery.Count >= 100)
                throw new InvalidOperationException(
                    "Galerie pleine (100 photos).");

            Commit(() => account.Gallery.Insert(0, id));
        }
    }

    public bool PhotoDeleted(ulong owner, string id)
    {
        return Ensure(owner).GalleryDeleted.Contains(id);
    }

    public void DeletePhoto(ulong owner, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new InvalidOperationException("Photo invalide.");

        PhoneAccount account = Ensure(owner);

        if (account.GalleryDeleted.Contains(id))
            return;

        Commit(() =>
        {
            account.Gallery.Remove(id);
            account.GalleryDeleted.Add(id);

            while (account.GalleryDeleted.Count > 200)
                account.GalleryDeleted.RemoveAt(0);
        });
    }

    public string Wallpaper(ulong owner)
    {
        return Ensure(owner).Wallpaper;
    }

    public bool IsWallpaper(ulong owner, string id)
    {
        return !string.IsNullOrEmpty(id) && Wallpaper(owner) == id;
    }

    public void SetWallpaper(ulong owner, string id)
    {
        id = id ?? "";

        PhoneAccount account = Ensure(owner);

        if (id != "" &&
            (!Guid.TryParseExact(id, "N", out _) ||
             !account.Gallery.Contains(id)))
        {
            throw new InvalidOperationException(
                "Choisir une photo de la Galerie de ce téléphone.");
        }

        Commit(() => account.Wallpaper = id);
    }

    public bool CanReadVoice(ulong owner, string id)
    {
        return Ensure(owner).Messages.Any(m => m.Voice == id);
    }

    public bool PhotoReferenced(string photo)
    {
        return db.Accounts.Any(a =>
            a.Wallpaper == photo ||
            a.Gallery.Contains(photo) ||
            a.Contacts.Any(c => c.Photo == photo) ||
            a.Messages.Any(m => m.Photo == photo));
    }

    public bool CanReadPhoto(ulong player, string photo)
    {
        PhoneAccount account = Ensure(player);

        return account.Wallpaper == photo ||
               account.Gallery.Contains(photo) ||
               account.Contacts.Any(c => c.Photo == photo) ||
               account.Messages.Any(m => m.Photo == photo);
    }

    public ulong? FindPlayer(string input)
    {
        string key = Normalize(input);

        return numbers.TryGetValue(key, out var account)
            ? new ulong?(account.Player)
            : null;
    }

    public void RecordCall(
        ulong caller,
        ulong callee,
        PhoneCallResult result)
    {
        PhoneAccount a = Ensure(caller);
        PhoneAccount b = Ensure(callee);

        if (a.Calls.Any(c => c.Id == result.Id) ||
            b.Calls.Any(c => c.Id == result.Id))
        {
            return;
        }

        Commit(() =>
        {
            a.Calls.Insert(0, new PhoneCallEntry
            {
                Id = result.Id,
                Number = b.Number,
                Outcome = result.Outcome,
                Incoming = false,
                Masked = result.Masked,
                Seconds = result.Seconds,
                Utc = result.Utc
            });

            b.Calls.Insert(0, new PhoneCallEntry
            {
                Id = result.Id,
                Number = result.Masked ? "" : a.Number,
                Outcome = result.Outcome,
                Incoming = true,
                Masked = result.Masked,
                Seconds = result.Seconds,
                Utc = result.Utc
            });

            while (a.Calls.Count > 50)
                a.Calls.RemoveAt(50);

            while (b.Calls.Count > 50)
                b.Calls.RemoveAt(50);
        });
    }

    public PhoneView PrivateView(ulong id)
    {
        PhoneAccount account = Ensure(id);

        List<PhoneBankEntry> centralTransactions =
            new List<PhoneBankEntry>();

        if (FusionPhoneEconomy.IsHosting)
        {
            string historyError;
            List<PhoneBankEntry> history;

            if (FusionPhoneEconomy.TryGetHistory(
                id,
                out history,
                out historyError) &&
                history != null)
            {
                centralTransactions = history;
            }
        }

        return new PhoneView
        {
            Player = id,
            Number = account.Number,

            Calls = account.Calls.Select(c => new PhoneCallEntry
            {
                Id = c.Id,
                Number = c.Number,
                Outcome = c.Outcome,
                Incoming = c.Incoming,
                Masked = c.Masked,
                Seconds = c.Seconds,
                Utc = c.Utc
            }).ToList(),

            Transactions = centralTransactions,

            Contacts = account.Contacts.Select(c => new PhoneContact
            {
                Number = c.Number,
                Name = c.Name,
                Photo = c.Photo
            }).ToList(),

            Messages = account.Messages.Select(m => new PhoneSms
            {
                Id = m.Id,
                From = m.From,
                To = m.To,
                Text = m.Text,
                Photo = m.Photo,
                Voice = m.Voice,
                VoiceSeconds = m.VoiceSeconds,
                Utc = m.Utc,
                Read = m.Read
            }).ToList()
        };
    }
}
