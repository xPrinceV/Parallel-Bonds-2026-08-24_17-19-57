using UnityEngine;

// Map roots contain geometry only, independently of the sleeping world's gameplay content.
[DisallowMultipleComponent]
public class WorldMap : MonoBehaviour
{
    [SerializeField] private Transform boundaryRoot;

    public Transform BoundaryRoot => boundaryRoot;
    public bool IsActive => gameObject.activeInHierarchy;
    public const float SearchRadius = 3f;
    private const float SearchStep = 0.5f;
    private const float Clearance = 0.02f;
    private const int MaxHits = 128;

    public bool IsConfigured => (transform.parent == null || transform.parent.gameObject.activeInHierarchy)
        && TryGetPlayableRect(out _);

    internal bool IsGeometryOnly()
    {
        if (GetComponentInChildren<Animator>(true) != null || GetComponentInChildren<Animation>(true) != null
            || GetComponentInChildren<ParticleSystem>(true) != null || GetComponentInChildren<AudioSource>(true) != null)
            return false;
        foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != this)
                return false;
        }
        foreach (Rigidbody2D body in GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (body.bodyType != RigidbodyType2D.Static)
                return false;
        }
        return true;
    }

    public bool SetMapActive(bool active)
    {
        gameObject.SetActive(active);
        return IsActive == active;
    }

    // Only this map is woken. Neither hero nor any content/Buff lifecycle participates in probing.
    public bool TryFindSafePosition(PlayerController current, PlayerController target, out Vector3 position)
    {
        position = current == null ? Vector3.zero : current.transform.position;
        if ((transform.parent != null && !transform.parent.gameObject.activeInHierarchy)
            || !TryGetPlayableRect(out Rect playable) || !IsGeometryOnly()
            || !TryGetFootprint(current, out Vector2 currentOffset, out float currentRadius)
            || !TryGetFootprint(target, out Vector2 targetOffset, out float targetRadius)
            || !Finite(position.x) || !Finite(position.y) || !Finite(position.z))
            return false;

        Vector2 origin = position;
        if (!ContainsFootprint(playable, origin + currentOffset, currentRadius))
            return false;

        bool wasActive = gameObject.activeSelf;
        try
        {
            if (!SetMapActive(true) || !IsActive)
                return false;
            PrepareGeometry();

            var overlaps = new Collider2D[MaxHits];
            var filter = new ContactFilter2D { useTriggers = true };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // At most 169 lattice candidates, within three world units, with a 25 ms query budget.
            for (int ring = 0; ring <= 6; ring++)
            {
                for (int y = -ring; y <= ring; y++)
                {
                    for (int x = -ring; x <= ring; x++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != ring)
                            continue;
                        if (clock.ElapsedMilliseconds >= 25)
                            return false;
                        Vector2 delta = new Vector2(x, y) * SearchStep;
                        if (delta.sqrMagnitude > SearchRadius * SearchRadius)
                            continue;
                        Vector2 center = origin + delta + targetOffset;
                        // The boundary is convex: contained source/end footprints cannot cross outside it.
                        if (!IsClear(playable, center, targetRadius, filter, overlaps))
                            continue;
                        if (clock.ElapsedMilliseconds >= 25)
                            return false;
                        position = new Vector3(origin.x + delta.x, origin.y + delta.y, position.z);
                        return true;
                    }
                }
            }
            return false;
        }
        finally
        {
            gameObject.SetActive(wasActive);
            Physics2D.SyncTransforms();
        }
    }

    // Match Instantiate(prefab, position, Quaternion.identity, spawnParent) without waking the prefab.
    public bool TryFindSafeSpawnPosition(Transform prefab, Transform spawnParent, PlayerController player,
        float distance, out Vector3 position)
    {
        position = player == null ? Vector3.zero : player.transform.position;
        if (prefab == null || spawnParent == null || !IsConfigured || !IsGeometryOnly()
            || !Finite(distance) || distance <= 0f || !Finite(position.x) || !Finite(position.y) || !Finite(position.z)
            || !TryGetPlayableRect(out Rect playable)
            || !TryGetFootprint(player, out Vector2 playerOffset, out float playerRadius))
            return false;
        Matrix4x4 pose = spawnParent.localToWorldMatrix * Matrix4x4.TRS(Vector3.zero,
            Quaternion.Inverse(spawnParent.rotation), prefab.localScale);
        // A hero touching a wall need not satisfy the extra placement margin: only the boss is moved.
        if (!TryGetFootprint(prefab, pose, out Vector2 offset, out float radius))
            return false;

        bool wasActive = gameObject.activeSelf;
        try
        {
            if (!SetMapActive(true) || !IsActive)
                return false;
            PrepareGeometry();
            Vector3 origin = position;
            var overlaps = new Collider2D[MaxHits];
            var filter = new ContactFilter2D { useTriggers = true };
            float separation = playerRadius + radius + Clearance;
            // Exhaust at most 416 candidates: preferred right, then 32 directions on 13 nearby radii.
            // Unlike a switch, exhausted spawn search ends the run, so do not stop on a wall-clock deadline.
            for (int ring = 0; ring <= 12; ring++)
            {
                float adjustment = ring == 0 ? 0f : ((ring + 1) / 2) * SearchStep * (ring % 2 == 1 ? -1f : 1f);
                float candidateDistance = distance + adjustment;
                if (candidateDistance <= 0f || !Finite(candidateDistance))
                    continue;
                for (int direction = 0; direction < 32; direction++)
                {
                    float angle = direction * (Mathf.PI * 2f / 32f);
                    Vector3 candidate = origin + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * candidateDistance;
                    Vector2 center = (Vector2)candidate + offset;
                    if ((center - ((Vector2)origin + playerOffset)).sqrMagnitude < separation * separation
                        || !IsClear(playable, center, radius, filter, overlaps))
                        continue;
                    position = candidate;
                    return true;
                }
            }
            return false;
        }
        finally
        {
            gameObject.SetActive(wasActive);
            Physics2D.SyncTransforms();
        }
    }

    private void PrepareGeometry()
    {
        foreach (UnityEngine.Tilemaps.TilemapCollider2D tilemap in GetComponentsInChildren<UnityEngine.Tilemaps.TilemapCollider2D>())
        {
            if (tilemap.hasTilemapChanges)
                tilemap.ProcessTilemapChanges();
        }
        foreach (CompositeCollider2D composite in GetComponentsInChildren<CompositeCollider2D>())
        {
            if (composite.generationType == CompositeCollider2D.GenerationType.Manual)
                composite.GenerateGeometry();
        }
        Physics2D.SyncTransforms();
    }

    private bool IsClear(Rect playable, Vector2 center, float radius, ContactFilter2D filter, Collider2D[] overlaps)
    {
        if (!ContainsFootprint(playable, center, radius))
            return false;
        int count = Physics2D.OverlapCircle(center, radius + Clearance, filter, overlaps);
        // A full buffer is ambiguous, never evidence of a safe destination.
        if (count == overlaps.Length)
            return false;
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = overlaps[i];
            if (hit != null && !hit.isTrigger && hit.transform.IsChildOf(transform))
                return false;
        }
        return true;
    }

    private static bool ContainsFootprint(Rect playable, Vector2 center, float radius)
    {
        float clearance = radius + Clearance;
        return center.x - clearance >= playable.xMin && center.x + clearance <= playable.xMax
            && center.y - clearance >= playable.yMin && center.y + clearance <= playable.yMax;
    }

    // Read authored offsets/transforms, not physics bounds that may be stale before SyncTransforms.
    public bool TryGetPlayableRect(out Rect playable)
    {
        playable = default;
        if (boundaryRoot == null || !boundaryRoot.gameObject.activeInHierarchy
            || boundaryRoot.IsChildOf(transform))
            return false;
        Collider2D[] walls = boundaryRoot.GetComponentsInChildren<Collider2D>(true);
        if (walls.Length != 4)
            return false;
        var horizontal = new Rect[2];
        var vertical = new Rect[2];
        int horizontalCount = 0, verticalCount = 0;
        foreach (Collider2D collider in walls)
        {
            if (!(collider is BoxCollider2D wall) || !wall.enabled || !wall.gameObject.activeInHierarchy
                || wall.isTrigger || wall.edgeRadius != 0f || wall.usedByEffector
                || wall.compositeOperation != Collider2D.CompositeOperation.None
                || (wall.attachedRigidbody != null && (!wall.attachedRigidbody.simulated
                    || wall.attachedRigidbody.bodyType != RigidbodyType2D.Static)))
                return false;
            Vector3 x = wall.transform.TransformVector(Vector3.right);
            Vector3 y = wall.transform.TransformVector(Vector3.up);
            // Reject rotation, tilt and shear instead of treating a rotated wall's AABB as solid.
            if (!AxisAligned(x, true) || !AxisAligned(y, false)
                || !Finite(wall.size.x) || !Finite(wall.size.y) || wall.size.x <= 0f || wall.size.y <= 0f)
                return false;
            Vector3 center = wall.transform.TransformPoint(wall.offset);
            Vector2 size = new Vector2(Mathf.Abs(x.x) * wall.size.x, Mathf.Abs(y.y) * wall.size.y);
            if (!Finite(center.x) || !Finite(center.y) || !Finite(size.x) || !Finite(size.y))
                return false;
            Rect bounds = new Rect((Vector2)center - size * 0.5f, size);
            if (size.x > size.y && horizontalCount < 2)
                horizontal[horizontalCount++] = bounds;
            else if (size.y > size.x && verticalCount < 2)
                vertical[verticalCount++] = bounds;
            else
                return false;
        }
        if (horizontalCount != 2 || verticalCount != 2)
            return false;
        Rect left = vertical[0].center.x < vertical[1].center.x ? vertical[0] : vertical[1];
        Rect right = vertical[0].center.x < vertical[1].center.x ? vertical[1] : vertical[0];
        Rect bottom = horizontal[0].center.y < horizontal[1].center.y ? horizontal[0] : horizontal[1];
        Rect top = horizontal[0].center.y < horizontal[1].center.y ? horizontal[1] : horizontal[0];
        // Clip the small corner gaps in the authored walls to their common covered span.
        float minX = Mathf.Max(left.xMax, bottom.xMin, top.xMin);
        float maxX = Mathf.Min(right.xMin, bottom.xMax, top.xMax);
        float minY = Mathf.Max(bottom.yMax, left.yMin, right.yMin);
        float maxY = Mathf.Min(top.yMin, left.yMax, right.yMax);
        if (minX >= maxX || minY >= maxY)
            return false;
        playable = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private static bool AxisAligned(Vector3 axis, bool horizontal)
    {
        float length = horizontal ? Mathf.Abs(axis.x) : Mathf.Abs(axis.y);
        return Finite(length) && length > 0f && Mathf.Abs(axis.z) <= length * 0.00001f
            && (horizontal ? Mathf.Abs(axis.y) : Mathf.Abs(axis.x)) <= length * 0.00001f;
    }

    // Circle heroes are exact. Boxes/capsules and compound bodies use a conservative enclosing circle.
    // Read serialized shape data because sleeping Collider2D.bounds is empty. Never clone gameplay scripts.
    public static bool TryGetFootprint(PlayerController player, out Vector2 offset, out float radius)
    {
        return TryGetFootprint(player == null ? null : player.transform, out offset, out radius);
    }

    public static bool TryGetFootprint(Transform root, out Vector2 offset, out float radius)
    {
        return TryGetFootprint(root, root == null ? Matrix4x4.identity : root.localToWorldMatrix, out offset, out radius);
    }

    private static bool TryGetFootprint(Transform root, Matrix4x4 pose, out Vector2 offset, out float radius)
    {
        offset = Vector2.zero;
        radius = 0f;
        if (root == null)
            return false;
        bool found = false;
        foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
        {
            if (!collider.enabled || collider.isTrigger || collider.GetComponentInParent<Weapon>() != null)
                continue;
            bool active = true;
            for (Transform node = collider.transform; node != root; node = node.parent)
                active &= node.gameObject.activeSelf;
            if (!active)
                continue;
            Matrix4x4 shape = pose * root.worldToLocalMatrix * collider.transform.localToWorldMatrix;
            Vector2 center = shape.MultiplyPoint3x4(collider.offset) - pose.MultiplyPoint3x4(Vector3.zero);
            Vector3 scale = shape.lossyScale;
            float shapeRadius;
            if (collider is CircleCollider2D circle)
                shapeRadius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            else if (collider is BoxCollider2D box)
                shapeRadius = Vector2.Scale(box.size * 0.5f, new Vector2(scale.x, scale.y)).magnitude
                    + box.edgeRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            else if (collider is CapsuleCollider2D capsule)
                shapeRadius = Vector2.Scale(capsule.size * 0.5f, new Vector2(scale.x, scale.y)).magnitude;
            else
                return false;
            if (!Finite(shapeRadius) || shapeRadius <= 0f || !Finite(center.x) || !Finite(center.y))
                return false;
            if (!found)
            {
                offset = center;
                radius = shapeRadius;
                found = true;
            }
            else
                radius = Mathf.Max(radius, Vector2.Distance(offset, center) + shapeRadius);
        }
        return found;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
