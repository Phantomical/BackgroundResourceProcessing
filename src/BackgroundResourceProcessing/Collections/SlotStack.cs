using System.Collections.Generic;

namespace BackgroundResourceProcessing.Collections
{
    /// <summary>
    /// A wrapper around <see cref="Stack"/> which doesn't allocate unless it
    /// needs to store more than 1 element.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal ref struct SlotStack<T>()
    {
        private T slot = default;
        private bool hasValue = false;

        private Stack<T> stack = null;

        public int Count
        {
            get
            {
                var slotcnt = hasValue ? 1 : 0;
                var stackcnt = stack == null ? 0 : stack.Count;

                return slotcnt + stackcnt;
            }
        }

        public void Push(T value)
        {
            if (!hasValue)
            {
                slot = value;
                hasValue = true;
                return;
            }

            stack ??= new();
            stack.Push(slot);
            slot = value;
        }

        public bool TryPop(out T value)
        {
            if (hasValue)
            {
                value = slot;
                hasValue = false;
                return true;
            }

            if (stack == null || stack.Count == 0)
            {
                value = default;
                return false;
            }

            value = stack.Pop();
            return false;
        }
    }
}
