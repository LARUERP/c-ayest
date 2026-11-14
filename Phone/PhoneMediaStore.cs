
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FusionDarkRP.Telephone;

public sealed class PhoneMediaStore
{
    private readonly string folder;
    private readonly int limit;

    private readonly Dictionary<ulong, string[]> capturedCache =
        new Dictionary<ulong, string[]>();

    public PhoneMediaStore(string folder, int limit = 1000)
    {
        this.folder = folder;
        this.limit = limit;
        Directory.CreateDirectory(folder);
    }

    public string PathFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new InvalidOperationException("Identifiant photo invalide.");
        }

        return Path.Combine(folder, id + ".jpg");
    }

    public bool Owns(ulong owner, string id)
    {
        string path = PathFor(id);

        return File.Exists(path)
            && File.Exists(path + ".owner")
            && File.ReadAllText(path + ".owner")
                == owner.ToString(CultureInfo.InvariantCulture);
    }

    public void Save(string id, ulong owner, byte[] data)
    {
        if (!PhoneMediaRules.Jpeg(data))
        {
            throw new InvalidOperationException(
                "JPEG invalide ou dimensions excessives.");
        }

        string path = PathFor(id);

        if (File.Exists(path))
        {
            if (!Owns(owner, id) || !Read(id).SequenceEqual(data))
            {
                throw new InvalidOperationException(
                    "Identifiant photo déjà utilisé.");
            }

            return;
        }

        if (Directory.EnumerateFiles(folder, "*.jpg").Take(limit).Count() >= limit)
        {
            throw new InvalidOperationException("Stockage photo plein.");
        }

        string temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, data);

        try
        {
            File.WriteAllText(
                path + ".owner",
                owner.ToString(CultureInfo.InvariantCulture));

            File.Move(temporaryPath, path);
            capturedCache.Clear();
        }
        catch
        {
            if (!File.Exists(path) && File.Exists(path + ".owner"))
            {
                File.Delete(path + ".owner");
            }

            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public byte[] Read(string id)
    {
        string path = PathFor(id);

        if (!File.Exists(path) || new FileInfo(path).Length > 65536)
        {
            throw new InvalidOperationException("Photo indisponible.");
        }

        return File.ReadAllBytes(path);
    }

    public string[] List()
    {
        Guid result;

        return Directory.EnumerateFiles(folder, "*.jpg")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(id => Guid.TryParseExact(id, "N", out result))
            .OrderByDescending(id => File.GetLastWriteTimeUtc(PathFor(id)))
            .Take(limit)
            .ToArray();
    }

    public void Prune(Func<string, bool> referenced, DateTime olderThanUtc)
    {
        string[] ids = List();

        foreach (string id in ids)
        {
            if (File.GetLastWriteTimeUtc(PathFor(id)) < olderThanUtc
                && !referenced(id))
            {
                Delete(id);
            }
        }
    }

    public void MarkCaptured(string id, ulong owner)
    {
        if (!Owns(owner, id))
        {
            throw new InvalidOperationException("Photo source absente.");
        }

        File.WriteAllText(
            PathFor(id) + ".captured",
            owner.ToString(CultureInfo.InvariantCulture));

        capturedCache.Clear();
    }

    public string[] Captured(ulong owner)
    {
        if (!capturedCache.TryGetValue(owner, out string[] value))
        {
            value = List()
                .Where(id =>
                    Owns(owner, id)
                    && File.Exists(PathFor(id) + ".captured")
                    && File.ReadAllText(PathFor(id) + ".captured")
                        == owner.ToString(CultureInfo.InvariantCulture))
                .ToArray();

            capturedCache[owner] = value;
        }

        return (string[])value.Clone();
    }

    public void UnmarkCaptured(string id)
    {
        File.Delete(PathFor(id) + ".captured");
        capturedCache.Clear();
    }

    public int OwnedCount(ulong owner)
    {
        return List().Count(id => Owns(owner, id));
    }

    public DateTime Date(string id)
    {
        return File.GetLastWriteTime(PathFor(id));
    }

    public void Delete(string id)
    {
        string path = PathFor(id);

        File.Delete(path);
        File.Delete(path + ".owner");
        File.Delete(path + ".captured");

        capturedCache.Clear();
    }
}
