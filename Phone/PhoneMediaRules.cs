using System;

namespace FusionDarkRP.Telephone;

public static class PhoneMediaRules
{
    public const int ChunkBytes = 600;
    public const int PhotoBytes = 65536;
    public const int VideoBytes = 16384;

    public static bool Voice(string kind)
    {
        return kind == "voiceUpload" || kind == "voiceDownload";
    }

    public static bool Stream(string kind)
    {
        return kind == "video" || kind == "live";
    }

    public static bool ValidChunk(PhoneMediaChunk c)
    {
        if (c == null ||
            c.Protocol != 16 ||
            !Guid.TryParseExact(c.Session, "N", out var result) ||
            !Guid.TryParseExact(c.Id, "N", out result) ||
            c.Bytes == null)
        {
            return false;
        }

        if (c.Kind != "upload" &&
            c.Kind != "download" &&
            c.Kind != "video" &&
            c.Kind != "live" &&
            !Voice(c.Kind))
        {
            return false;
        }

        int max = Voice(c.Kind)
            ? 1440016
            : (Stream(c.Kind) ? 16384 : 65536);

        if (Stream(c.Kind) &&
            (!Guid.TryParseExact(c.Call, "N", out result) || c.Frame == 0))
        {
            return false;
        }

        if (c.Total < 16 ||
            c.Total > max ||
            c.Count != (c.Total + 600 - 1) / 600 ||
            c.Index < 0 ||
            c.Index >= c.Count)
        {
            return false;
        }

        return c.Bytes.Length ==
            Math.Min(600, c.Total - c.Index * 600);
    }

    public static PhoneMediaChunk[] Split(
        string session,
        string id,
        string kind,
        byte[] bytes,
        string call = "",
        uint frame = 0u)
    {
        if (bytes == null ||
            bytes.Length < 16 ||
            bytes.Length > (Voice(kind)
                ? 1440016
                : (Stream(kind) ? 16384 : 65536)))
        {
            throw new InvalidOperationException(
                "Image absente ou trop volumineuse.");
        }

        int count = (bytes.Length + 600 - 1) / 600;
        PhoneMediaChunk[] array = new PhoneMediaChunk[count];

        for (int i = 0; i < count; i++)
        {
            PhoneMediaChunk chunk = new PhoneMediaChunk
            {
                Session = session,
                Id = id,
                Kind = kind,
                Call = call,
                Frame = frame,
                Index = i,
                Count = count,
                Total = bytes.Length,
                Bytes = bytes.AsSpan(
                    i * 600,
                    Math.Min(600, bytes.Length - i * 600)).ToArray()
            };

            if (!ValidChunk(chunk))
            {
                throw new InvalidOperationException(
                    "Image trop volumineuse ou enveloppe invalide.");
            }

            array[i] = chunk;
        }

        return array;
    }

    public static bool Jpeg(
        byte[] bytes,
        int maxWidth = 640,
        int maxHeight = 480)
    {
        if (bytes == null ||
            bytes.Length < 16 ||
            bytes.Length > 65536 ||
            bytes[0] != byte.MaxValue ||
            bytes[1] != 216 ||
            bytes[^2] != byte.MaxValue ||
            bytes[^1] != 217)
        {
            return false;
        }

        int i = 2;
        bool result = false;

        while (i + 1 < bytes.Length)
        {
            if (bytes[i++] != byte.MaxValue)
                return false;

            for (; i < bytes.Length && bytes[i] == byte.MaxValue; i++)
            {
            }

            if (i >= bytes.Length)
                return false;

            int marker = bytes[i++];

            switch (marker)
            {
                case 218:
                    return result;

                default:
                    if (marker != 0)
                    {
                        if (marker >= 208 && marker <= 215)
                            break;

                        if (i + 2 > bytes.Length)
                            return false;

                        int length = (bytes[i] << 8) | bytes[i + 1];

                        if (length < 2 || i + length > bytes.Length)
                            return false;

                        if (marker >= 192 &&
                            marker <= 207 &&
                            marker != 192 &&
                            marker != 194 &&
                            marker != 196 &&
                            marker != 200 &&
                            marker != 204)
                        {
                            return false;
                        }

                        if (marker == 192 || marker == 194)
                        {
                            if (length < 8 || bytes[i + 2] != 8)
                                return false;

                            int height = (bytes[i + 3] << 8) | bytes[i + 4];
                            int width = (bytes[i + 5] << 8) | bytes[i + 6];

                            if (width < 1 ||
                                height < 1 ||
                                width > maxWidth ||
                                height > maxHeight)
                            {
                                return false;
                            }

                            result = true;
                        }

                        i += length;
                        break;
                    }

                    goto case 216;

                case 216:
                case 217:
                    return false;
            }
        }

        return false;
    }
}
