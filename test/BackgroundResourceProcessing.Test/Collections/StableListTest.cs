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

    [TestInfo("StableListTests_RemoveLastAfterRefill")]
    public void RemoveLastAfterRefill()
    {
        StableList<string> list = [];

        list.Add("a");
        list.Add("b");
        list.Add("c");
        list.RemoveAt(1);
        Assert.AreEqual(1, list.Add("d"));

        // [a, d, c]: removing the last element must only remove c.
        list.RemoveAt(2);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual("a", list[0]);
        Assert.AreEqual("d", list[1]);
    }

    [TestInfo("StableListTests_RemoveLastTrimsTrailingHoles")]
    public void RemoveLastTrimsTrailingHoles()
    {
        StableList<string> list = [];

        list.Add("a");
        list.Add("b");
        list.Add("c");
        list.Add("d");
        list.RemoveAt(1);
        list.RemoveAt(2);

        // [a, null, null, d]: removing d also trims the holes, but keeps a.
        list.RemoveAt(3);

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("a", list[0]);

        // The next add goes right after a.
        Assert.AreEqual(1, list.Add("e"));
        Assert.AreEqual(2, list.Count);
    }

    [TestInfo("StableListTests_RemoveLastWithHoleBefore")]
    public void RemoveLastWithHoleBefore()
    {
        StableList<string> list = [];

        list.Add("a");
        list.Add("b");
        list.Add("c");
        list.Add("d");
        list.RemoveAt(1);

        // [a, null, c, d]: removing d must keep c.
        list.RemoveAt(3);

        Assert.AreEqual(3, list.Count);
        Assert.AreEqual("a", list[0]);
        Assert.IsTrue(list[1] is null);
        Assert.AreEqual("c", list[2]);
    }
}
