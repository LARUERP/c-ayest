namespace FusionDarkRP.Telephone;

public static class PhoneProximity
{
    public static bool Within(
        float ax, float ay, float az,
        float bx, float by, float bz,
        float radius)
    {
        if (!float.IsFinite(ax) ||
            !float.IsFinite(ay) ||
            !float.IsFinite(az) ||
            !float.IsFinite(bx) ||
            !float.IsFinite(by) ||
            !float.IsFinite(bz) ||
            !float.IsFinite(radius) ||
            radius <= 0f)
        {
            return false;
        }

        double dx = (double)ax - bx;
        double dy = (double)ay - by;
        double dz = (double)az - bz;

        return dx * dx + dy * dy + dz * dz
            <= (double)radius * radius;
    }
}
