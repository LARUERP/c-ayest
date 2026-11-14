using System.Collections.Generic;

namespace FusionDarkRP.Telephone;

public sealed class PhoneDatabase
{
    public int Version { get; set; } = 1;

    public long NextNumber { get; set; } = 1L;

    public bool RandomNumbers { get; set; }

    public List<PhoneAccount> Accounts { get; set; } = new List<PhoneAccount>();
}
