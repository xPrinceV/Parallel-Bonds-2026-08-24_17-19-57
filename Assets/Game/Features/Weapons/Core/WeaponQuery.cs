using System.Collections.Generic;
using UnityEngine;

public static class WeaponQuery
{
    // Both buffers belong to the query owner and must be distinct.
    public static void OverlapCircle(Vector2 point, float radius, List<Collider2D> results, List<Collider2D> scratch)
    {
        // Match OverlapCircleAll's legacy filter, including runtime trigger changes.
        var filter = new ContactFilter2D { useTriggers = Physics2D.queriesHitTriggers };
        filter.SetLayerMask(Physics2D.DefaultRaycastLayers);
        filter.SetDepth(-Mathf.Infinity, Mathf.Infinity);
        Physics2D.OverlapCircle(point, radius, filter, results);

        SortByZ(results, scratch);
    }

    private static void SortByZ(List<Collider2D> results, List<Collider2D> scratch)
    {
        // Flat/already ordered queries need no scratch storage or merge passes.
        for (int i = 1; i < results.Count; i++)
        {
            if (results[i - 1].transform.position.z > results[i].transform.position.z)
            {
                scratch.Clear();
                scratch.AddRange(results);
                MergeSort(results, scratch, 0, results.Count);
                scratch.Clear();
                return;
            }
        }
    }

    private static void MergeSort(List<Collider2D> results, List<Collider2D> scratch, int start, int end)
    {
        if (end - start < 2)
            return;

        int middle = start + (end - start) / 2;
        MergeSort(results, scratch, start, middle);
        MergeSort(results, scratch, middle, end);

        int left = start;
        int right = middle;
        int output = start;
        while (left < middle && right < end)
        {
            // Equal-depth hits keep their original query order.
            if (results[left].transform.position.z > results[right].transform.position.z)
                scratch[output++] = results[right++];
            else
                scratch[output++] = results[left++];
        }
        while (left < middle)
            scratch[output++] = results[left++];
        while (right < end)
            scratch[output++] = results[right++];
        for (int i = start; i < end; i++)
            results[i] = scratch[i];
    }
}
