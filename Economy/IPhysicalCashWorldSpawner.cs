namespace FusionDarkRP.Economy
{
    public interface IPhysicalCashWorldSpawner
    {
        bool TrySpawn(
            PhysicalCashSpawnTicket ticket,
            out string spawnedObjectId,
            out string error);

        bool TryDestroy(
            string spawnedObjectId,
            out string error);

        bool Exists(string spawnedObjectId);
    }
}
