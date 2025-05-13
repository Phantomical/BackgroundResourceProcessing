using System.Collections.Generic;

namespace BackgroundResourceProcessing.Collections
{
    public class EdgeMap<C, I>
    {
        Dictionary<C, HashSet<I>> forward = [];
        Dictionary<I, HashSet<C>> reverse = [];

        public EdgeMap() { }

        public void Add(C converterId, I inventoryId)
        {
            var inventories = GetOrCreateConverterEntry(converterId);
            var converters = GetOrCreateInventoryEntry(inventoryId);

            converters.Add(converterId);
            inventories.Add(inventoryId);
        }

        public bool Remove(C converterId, I inventoryId)
        {
            if (!forward.TryGetValue(converterId, out var inventories))
                return false;
            if (!reverse.TryGetValue(inventoryId, out var converters))
                return false;

            var removed = false;
            removed |= inventories.Remove(inventoryId);
            removed |= converters.Remove(converterId);
            if (!removed)
                return false;

            if (inventories.Count == 0)
                forward.Remove(converterId);
            if (converters.Count == 0)
                reverse.Remove(inventoryId);

            return true;
        }

        public void RemoveConverter(C converterId)
        {
            if (!forward.TryGetValue(converterId, out var inventories))
                return;
            forward.Remove(converterId);

            foreach (var inventoryId in inventories)
                reverse[inventoryId].Remove(converterId);
        }

        public void RemoveInventory(I inventoryId)
        {
            if (!reverse.TryGetValue(inventoryId, out var converters))
                return;
            reverse.Remove(inventoryId);

            foreach (var converterId in converters)
                forward[converterId].Remove(inventoryId);
        }

        public HashSet<I> GetConverterEntry(C converterId)
        {
            if (!forward.TryGetValue(converterId, out var set))
                return [];
            return set;
        }

        public HashSet<C> GetInventoryEntry(I inventoryId)
        {
            if (!reverse.TryGetValue(inventoryId, out var set))
                return [];
            return set;
        }

        private HashSet<I> GetOrCreateConverterEntry(C converterId)
        {
            if (!forward.TryGetValue(converterId, out var set))
            {
                set = [];
                forward.Add(converterId, set);
            }

            return set;
        }

        private HashSet<C> GetOrCreateInventoryEntry(I inventoryId)
        {
            if (!reverse.TryGetValue(inventoryId, out var set))
            {
                set = [];
                reverse.Add(inventoryId, set);
            }

            return set;
        }
    }
}
