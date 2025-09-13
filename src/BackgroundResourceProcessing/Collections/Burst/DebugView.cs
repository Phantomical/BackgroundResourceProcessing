using System.Collections.Generic;
using System.Diagnostics;

namespace BackgroundResourceProcessing.Collections.Burst;

internal class SequenceDebugView<T>(IEnumerable<T> seq)
{
    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public T[] Items = [.. seq];
}

internal class DictionaryDebugView<K, V>(IEnumerable<KeyValuePair<K, V>> pairs)
    : SequenceDebugView<KeyValuePair<K, V>>(pairs) { }
