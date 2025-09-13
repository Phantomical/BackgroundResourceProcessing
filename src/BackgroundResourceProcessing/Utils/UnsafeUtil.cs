using System.Reflection;

namespace BackgroundResourceProcessing.Utils;

internal static class UnsafeUtil
{
    private static class ContainsReferencesImpl<T>
    {
        internal static readonly bool HasReferences;

        static ContainsReferencesImpl()
        {
            if (!typeof(T).IsValueType)
            {
                HasReferences = true;
                return;
            }

            HasReferences = false;
            if (typeof(T).IsEnum)
                return;

            var fields = typeof(T).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            var method = typeof(UnsafeUtil).GetMethod(nameof(ContainsReferences), []);

            foreach (var field in fields)
            {
                // A self-referencing struct shouldn't be possible, but otherwise
                // self fields don't affect whether it is a reference type one
                // way or another.
                if (field.FieldType == typeof(T))
                    continue;

                // We determine whether an inner field has references by just
                // calling ContainsReferences with the field type.
                var inst = method.MakeGenericMethod([field.FieldType]);
                HasReferences |= (bool)inst.Invoke(null, null);
                if (HasReferences)
                    break;
            }
        }
    }

    /// <summary>
    /// Determines whether a type contains any GC references.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public static bool ContainsReferences<T>() => ContainsReferencesImpl<T>.HasReferences;
}
