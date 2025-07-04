using System.Collections.Generic;

namespace BackgroundResourceProcessing.Collections;

internal static class StackExtensions
{
    public static bool TryPopValue<T>(this Stack<T> stack, out T value)
    {
        if (stack.Count == 0)
        {
            value = default;
            return false;
        }

        value = stack.Pop();
        return true;
    }
}
