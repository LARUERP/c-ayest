using System;

namespace FusionDarkRP.Telephone;

public sealed class PhoneBankEntry
{
    public string Id { get; set; } = "";

    public long Amount { get; set; }

    public string Label { get; set; } = "";

    public DateTime Utc { get; set; }
}
