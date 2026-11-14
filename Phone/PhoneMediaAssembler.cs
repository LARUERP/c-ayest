using System;
using System.Collections.Generic;
using System.Linq;

namespace FusionDarkRP.Telephone;

public sealed class PhoneMediaAssembler
{
    private sealed class Pending
    {
        public PhoneMediaChunk First;
        public byte[][] Parts;
        public int Received;
        public double Started;
    }

    private readonly Dictionary<string, Pending> pending =
        new Dictionary<string, Pending>();

    public void Clear()
    {
        pending.Clear();
    }

    public void Expire(double now)
    {
        string[] expired = (
            from p in pending
            where now - p.Value.Started >
                (PhoneMediaRules.Stream(p.Value.First.Kind) ? 2 : 15)
            select p.Key
        ).ToArray();

        foreach (string key in expired)
        {
            pending.Remove(key);
        }
    }

    public byte[] Add(ulong actor, PhoneMediaChunk c, double now)
    {
        Expire(now);

        if (!PhoneMediaRules.ValidChunk(c))
            return null;

        string prefix = actor + "/";
        string key = prefix + c.Session + "/" + c.Kind + "/" + c.Id;

        if (!pending.TryGetValue(key, out Pending value))
        {
            if (PhoneMediaRules.Stream(c.Kind))
            {
                KeyValuePair<string, Pending>[] streams = (
                    from x in pending
                    where x.Key.StartsWith(prefix, StringComparison.Ordinal) &&
                          PhoneMediaRules.Stream(x.Value.First.Kind) &&
                          x.Value.First.Call == c.Call
                    orderby x.Value.First.Frame
                    select x
                ).ToArray();

                if (streams.Length >= 2)
                {
                    if (c.Frame <= streams[0].Value.First.Frame)
                        return null;

                    pending.Remove(streams[0].Key);
                }
            }

            if (pending.Count >= 32 ||
                pending.Keys.Count(k =>
                    k.StartsWith(prefix, StringComparison.Ordinal)) >= 3)
            {
                return null;
            }

            value = new Pending
            {
                First = c,
                Parts = new byte[c.Count][],
                Started = now
            };

            pending[key] = value;
        }

        PhoneMediaChunk first = value.First;

        if (c.Count != first.Count ||
            c.Total != first.Total ||
            c.Call != first.Call ||
            c.Frame != first.Frame ||
            c.Owner != first.Owner ||
            c.Device != first.Device ||
            c.Access != first.Access)
        {
            pending.Remove(key);
            return null;
        }

        if (value.Parts[c.Index] != null)
        {
            if (!value.Parts[c.Index].SequenceEqual(c.Bytes))
                pending.Remove(key);

            return null;
        }

        value.Parts[c.Index] = (byte[])c.Bytes.Clone();
        value.Received++;

        if (value.Received != c.Count)
            return null;

        byte[] result = new byte[c.Total];

        for (int i = 0; i < c.Count; i++)
        {
            value.Parts[i].CopyTo(result, i * 600);
        }

        pending.Remove(key);
        return result;
    }
}
