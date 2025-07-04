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
        bool first = true;
        T prev = default;

        return list.RemoveAll(
            (elem) =>
            {
                if (first)
                {
                    first = false;
                    return false;
                }

                bool remove = elem.Equals(prev);
                if (!remove)
                    prev = elem;
                return remove;
            }
        );
    }
}
