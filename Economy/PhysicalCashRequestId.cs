using System;

namespace FusionDarkRP.Economy
{
    public static class PhysicalCashRequestId
    {
        public static string Create()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static bool IsValid(string requestId)
        {
            return !string.IsNullOrWhiteSpace(requestId)
                && Guid.TryParseExact(requestId, "N", out _);
        }
    }
}
