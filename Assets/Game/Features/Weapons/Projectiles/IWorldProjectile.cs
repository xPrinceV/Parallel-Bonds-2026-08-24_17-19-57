// End a projectile without applying hit effects; repeated calls must be safe.
// This is a lifecycle boundary, not a complete pooling or reuse contract.
public interface IWorldProjectile
{
    void Despawn();
}
