using System;

namespace FusionDarkRP.Telephone;

public sealed class WindyPostView
{
    public string Id { get; set; } = "";

    public string Handle { get; set; } = "";

    public string Text { get; set; } = "";

    public string Photo { get; set; } = "";

    public DateTime Utc { get; set; }

    public bool Mine { get; set; }
}
