using BackgroundResourceProcessing.Collections;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Collections;

public sealed class StableListTests : BRPTestBase
{
    /// <summary>
    /// Inserting past the end fills the gap with holes instead of throwing.
    /// Loading a saved list depends on this: a list that has had an element
    /// removed saves sparse indices, and every element has to go back at the
    /// index it was saved at.
    /// </summary>
    [TestInfo("StableListTests_InsertPastEnd")]
    public void InsertPastEnd()
    {
        StableList<string> list = [];

        list.Insert(0, "a");
        list.Insert(3, "d");

        Assert.AreEqual(4, list.Count);
        Assert.AreEqual("a", list[0]);
        Assert.IsTrue(list[1] is null);
        Assert.IsTrue(list[2] is null);
        Assert.AreEqual("d", list[3]);

        // Iteration skips the holes.
        Assert.AreEqual("[a, d]", TestUtil.SequenceToString(list));

        // ...and they get reused before the list grows any further.
        Assert.AreEqual(1, list.Add("b"));
        Assert.AreEqual(2, list.Add("c"));
        Assert.AreEqual(4, list.Add("e"));
        Assert.AreEqual("[a, b, c, d, e]", TestUtil.SequenceToString(list));
    }

    [TestInfo("StableListTests_InsertOverwritesExisting")]
    public void InsertOverwritesExisting()
    {
        StableList<string> list = [];

        list.Insert(0, "a");
        list.Insert(1, "b");
        list.Insert(1, "c");

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual("a", list[0]);
        Assert.AreEqual("c", list[1]);
    }
}
