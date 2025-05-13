using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

#nullable enable

namespace UnifiedBackgroundProcessing.Collections
{
    /// <summary>
    /// A min priority queue.
    /// </summary>
    /// <typeparam name="TElement">The type of elements in the queue.</typeparam>
    /// <typeparam name="TPriority">The type of the priority associated with enqueued elements.</typeparam>
    ///
    /// <remarks>
    /// <para>
    ///  This implements a min-queue using a binary heap backed by an array. It
    ///  is somewhat meant to imitate the PriorityQueue class in
    ///  <c>System.Collections.Generic</c> in later .NET versions.
    /// </para>
    /// </remarks>
    public class PriorityQueue<TElement, TPriority>
    {
        private List<(TElement Element, TPriority Priority)> _nodes;
        private readonly IComparer<TPriority> _comparer;

        public PriorityQueue()
        {
            _nodes = [];
            _comparer = InitialComparer(null);
        }

        public PriorityQueue(int capacity)
            : this(capacity, null) { }

        public PriorityQueue(IComparer<TPriority>? comparer)
            : this(0, comparer) { }

        public PriorityQueue(int capacity, IComparer<TPriority>? comparer)
        {
            _nodes = new(capacity);
            _comparer = InitialComparer(comparer);
        }

        public PriorityQueue(IEnumerable<(TElement Element, TPriority Priority)> items)
            : this(items, null) { }

        public PriorityQueue(
            IEnumerable<(TElement Element, TPriority Priority)> items,
            IComparer<TPriority>? comparer
        )
        {
            _nodes = new(items);
            _comparer = InitialComparer(comparer);

            // TODO: This is inefficient,
            _nodes.Sort((a, b) => _comparer.Compare(a.Priority, b.Priority));
        }

        /// <summary>
        /// Gets the total number of elements stored in this priority queue.
        /// </summary>
        public int Count => _nodes.Count;

        /// <summary>
        /// Indicates whether this queue is empty or not.
        /// </summary>
        public bool IsEmpty => Count == 0;

        /// <summary>
        /// Gets the total number of elements this queue's backing storage can hold without reallocating.
        /// </summary>
        public int Capacity => _nodes.Capacity;

        /// <summary>
        /// Gets the priority comparer used by this queue.
        /// </summary>
        public IComparer<TPriority> Comparer => _comparer;

        /// <summary>
        /// Adds a new element to the priority queue.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <param name="priority">The element's priority.</param>
        public void Enqueue(TElement element, TPriority priority)
        {
            _nodes.Add((element, priority));
            MoveUp(_nodes.Count - 1);
        }

        /// <summary>
        /// Returns the minimal element from the queue.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">The queue is empty.</exception>
        public TElement Peek()
        {
            if (_nodes.Count == 0)
                throw new InvalidOperationException("Attempted to call Peek() on an empty queue");

            return _nodes[0].Element;
        }

        /// <summary>
        /// Attempts to return the minimal element from the queue, if there is one.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <param name="priority">The element's priority.</param>
        /// <returns>
        ///   <see langword="true"/> if there was an element in the queue,
        ///   <see langword="false"/> otherwise.
        /// </returns>
        public bool TryPeek(out TElement element, out TPriority priority)
        {
            if (Count == 0)
            {
#nullable disable
                element = default;
                priority = default;
#nullable enable
                return false;
            }

            (element, priority) = _nodes[0];
            return true;
        }

        /// <summary>
        /// Removes the minimal element from the queue and returns
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">The queue is empty.</exception>
        public TElement Dequeue()
        {
            if (_nodes.Count == 0)
                throw new InvalidOperationException(
                    "Attempted to call Dequeue() on an empty queue"
                );

            var element = _nodes[0].Element;
            RemoveRootElement();
            return element;
        }

        /// <summary>
        /// Attempt to remove the minimal element from the queue. Copying its
        /// value and associated priority to <paramref name="element" /> and
        /// <paramref name="priority"/>, respectively.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <param name="priority">The element's priority.</param>
        /// <returns>
        ///   <see langword="true"/> if an element was dequeued;
        ///   <see langword="false"/> otherwise.
        /// </returns>
        public bool TryDequeue(out TElement element, out TPriority priority)
        {
            if (Count == 0)
            {
#nullable disable
                element = default;
                priority = default;
#nullable enable
                return false;
            }

            (element, priority) = _nodes[0];
            RemoveRootElement();
            return true;
        }

        public bool Remove(
            TElement element,
            out TElement removedElement,
            out TPriority removedPriority,
            IEqualityComparer<TElement>? equalityComparer = null
        )
        {
            var index = FindIndex(element, equalityComparer ?? EqualityComparer<TElement>.Default);
            if (index < 0)
            {
#nullable disable
                removedElement = default;
                removedPriority = default;
#nullable enable
                return false;
            }

            (removedElement, removedPriority) = _nodes[index];
            RemoveAtIndex(index);
            return true;
        }

        private void MoveUp(int index)
        {
            var node = _nodes[index];

            while (index != 0)
            {
                int parentIndex = GetParentindex(index);
                var parent = _nodes[parentIndex];

                if (_comparer.Compare(node.Priority, parent.Priority) < 0)
                {
                    _nodes[index] = parent;
                    index = parentIndex;
                }
                else
                {
                    break;
                }
            }

            _nodes[index] = node;
        }

        private void MoveDown(int index)
        {
            var node = _nodes[index];

            while (true)
            {
                var leftIndex = GetLeftChildIndex(index);
                var rightIndex = GetRightChildIndex(index);

                if (leftIndex >= _nodes.Count)
                    break;

                var left = _nodes[leftIndex];
                var minChildIndex = leftIndex;
                var minChild = left;

                if (rightIndex < _nodes.Count)
                {
                    var right = _nodes[rightIndex];

                    if (_comparer.Compare(right.Priority, left.Priority) <= 0)
                    {
                        minChildIndex = rightIndex;
                        minChild = right;
                    }
                }

                if (_comparer.Compare(node.Priority, minChild.Priority) <= 0)
                {
                    // Heap property is satisfied. We can insert the node here.
                    break;
                }

                _nodes[index] = minChild;
                index = minChildIndex;
            }

            _nodes[index] = node;
        }

        private void RemoveRootElement()
        {
            RemoveAtIndex(0);
        }

        private void RemoveAtIndex(int index)
        {
            Debug.Assert(index < _nodes.Count);

            var last = _nodes.Last();
            _nodes.RemoveAt(_nodes.Count - 1);
            if (_nodes.Count <= index)
                return;
            _nodes[index] = last;
            MoveDown(index);
        }

        private int FindIndex(TElement element, IEqualityComparer<TElement> comparer)
        {
            return _nodes.FindIndex(tuple => comparer.Equals(element, tuple.Element));
        }

        static int GetParentindex(int index)
        {
            return (index - 1) / 2;
        }

        static int GetLeftChildIndex(int index)
        {
            return index * 2 + 1;
        }

        static int GetRightChildIndex(int index)
        {
            return index * 2 + 2;
        }

        static IComparer<TPriority> InitialComparer(IComparer<TPriority>? comparer)
        {
            return comparer ?? Comparer<TPriority>.Default;
        }
    }
}
