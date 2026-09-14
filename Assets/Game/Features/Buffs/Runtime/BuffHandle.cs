// keep one handle per grant so unrelated sources cannot remove each other's effects
public sealed class BuffHandle
{
    internal BuffInstance Instance { get; }

    public BuffDefinition Definition => Instance.Definition;
    public object Source { get; }
    public bool IsActive => Instance.IsActive;

    // only the runtime issues handles; callers retain them for refresh and removal
    internal BuffHandle(BuffInstance instance, object source)
    {
        Instance = instance;
        Source = source;
    }
}
