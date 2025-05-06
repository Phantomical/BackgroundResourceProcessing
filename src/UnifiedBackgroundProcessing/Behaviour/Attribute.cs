using System;

namespace UnifiedBackgroundProcessing.Behaviour
{
    /// <summary>
    /// Marks the current class as a behaviour for the purposes of serialization.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    ///   This attribute marks a class that can be deserialized as a
    ///   <see cref="BaseBehaviour"/>. It is used by
    ///   <see cref="UnifiedBackgroundProcessing.RegisterAllBehaviours"/> to
    ///   discover behaviour classes in an assembly.
    /// </para>
    ///
    /// <para>
    ///   The key information that this attribute collects is a unique primary
    ///   name for the behaviour, along with a number of alternate names. The
    ///   primary name can be provided directly as a string or can be determined
    ///   from a provided <see cref="Type"/> object. Alternates are intended to
    ///   allow for migrations. They will result in a warning if they are not
    ///   unique but do allow for collisions.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class Behaviour : Attribute
    {
        public readonly string Name;
        public readonly string[] Alternates;

        public Behaviour(Type type, string[] alternates = null)
        {
            Name = type.Name;
            Alternates = alternates ?? [];
        }

        public Behaviour(string name, string[] alternates = null)
        {
            Name = name;
            Alternates = alternates ?? [];
        }
    }
}
