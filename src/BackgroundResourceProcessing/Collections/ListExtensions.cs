using System.Collections.Generic;

namespace BackgroundResourceProcessing.Collections;

internal static class ListExtensions
{
    /// <summary>
    /// Remove all duplicate elements from a list. This is implemented
    /// in-place an requires that the list be sorted first.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="list"></param>
    /// <returns>The number of elements removed from the list.</returns>
    public static int Deduplicate<T>(this List<T> list)
    {
        if (list.Count <= 1)
            return 0;

        var count = list.Count;
        var prev = list[0];
        int i = 1;
        int j = 1;
        for (; i < count; ++i)
        {
            var elem = list[i];
            if (elem.Equals(prev))
                continue;

            prev = elem;
            list[j++] = elem;
        }

        if (i != j)
            list.RemoveRange(j, i - j);

        return i - j;
    }

    /// <summary>
    /// Attempt to remove the last element in the list.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="list"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool TryPopBack<T>(this List<T> list, out T value)
    {
        if (list.Count == 0)
        {
            value = default;
            return false;
        }

        value = list[list.Count - 1];
        list.RemoveAt(list.Count - 1);
        return true;
    }
}
