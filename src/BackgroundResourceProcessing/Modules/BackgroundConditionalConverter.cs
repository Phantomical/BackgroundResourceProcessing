using System;
using System.ComponentModel.Design.Serialization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BackgroundResourceProcessing.Modules
{
    /// <summary>
    /// A background constant converter that checks whether it is enabled by
    /// looking at a field on another module.
    /// </summary>
    ///
    /// <remarks>
    /// This is primarily addressed for mod compatibility. As long as you can
    /// model the mod's behaviour as one or more constant converters that are
    /// conditionally active then you can make it compatible without having to
    /// actually write custom modules.
    /// </remarks>
    public class ModuleBackgroundConditionalConverter
        : ModuleBackgroundConstantConverter,
            IBackgroundVesselRestoreHandler
    {
        /// <summary>
        /// The type name of the module that this module should look for.
        /// </summary>
        [KSPField]
        public string TargetModule;

        /// <summary>
        /// The index of the module (for all modules with the same type).
        /// </summary>
        [KSPField]
        public int TargetModuleIndex = 0;

        /// <summary>
        /// A condition to evaluate to determine when this converter should be
        /// active. This must be the name of a boolean field or property on the
        /// target module, optionally preceded by <c>!</c> to negate the condition.
        /// </summary>
        [KSPField]
        public string Condition = "";

        /// <summary>
        /// A field that would normally track the last update time that should be
        /// overwritten with the current time on resume.
        /// </summary>
        ///
        /// <remarks>
        /// This is designed to allow for suppressing existing catch-up logic in
        /// a module.
        /// </remarks>
        [KSPField]
        public string LastUpdateField = null;

        [KSPField(isPersistant = true)]
        private uint cachedPersistentModuleId = 0;

        private PartModule module = null;
        private MemberInfo conditionMember = null;
        private MemberInfo lastUpdateMember = null;

        private PartModule GetLinkedModuleCached()
        {
            if (module != null)
                return module;

            if (cachedPersistentModuleId == 0)
                return null;
            var persistentId = cachedPersistentModuleId;
            var found = part.Modules[persistentId];
            if (found == null)
                return null;

            var type = found.GetType();
            if (type.Name != TargetModule)
                return null;

            return found;
        }

        public override void OnStart(StartState state)
        {
            Setup();
        }

        public void OnVesselRestore()
        {
            Setup();

            if (lastUpdateMember == null)
                return;

            SetMemberValue(lastUpdateMember, module, Planetarium.GetUniversalTime());
        }

        protected override ConverterBehaviour GetConverterBehaviour()
        {
            if (!EvaluateCondition())
                return null;

            return base.GetConverterBehaviour();
        }

        private void Setup()
        {
            if (module != null)
                return;

            try
            {
                module = GetLinkedModule();
                if (module == null)
                    return;
                conditionMember = GetConditionMember();
                lastUpdateMember = GetLastUpdateMember();
            }
            catch (MemberException e)
            {
                LogUtil.Error($"{e.Message}. This {GetType().Name} module will be disabled.");
                module = null;
            }
        }

        private PartModule GetLinkedModule()
        {
            var cached = GetLinkedModuleCached();
            if (cached != null)
                return cached;

            int index = 0;
            PartModule found = null;
            for (int i = 0; i < part.Modules.Count; ++i)
            {
                var module = part.Modules[i];
                var type = module.GetType();
                if (type.Name != TargetModule)
                    continue;

                if (index != TargetModuleIndex)
                {
                    index += 1;
                    continue;
                }

                found = module;
                break;
            }

            if (found == null)
            {
                if (index == 0)
                {
                    throw new MemberException(
                        $"No module of type {TargetModule} found on part {part.partName}"
                    );
                }
                else
                {
                    var suffix = GetNumberSuffix(index);
                    throw new MemberException(
                        $"There is no {index}{suffix} module of type {TargetModule} found on part {part.partName}"
                    );
                }
            }

            cachedPersistentModuleId = found.GetPersistentId();
            return found;
        }

        private MemberInfo GetConditionMember()
        {
            var fieldName = Condition;
            if (fieldName.StartsWith("!"))
                fieldName = fieldName.Substring(1);

            var type = module.GetType();
            var member = GetNamedMember(fieldName, Access.Read);

            var memberType = GetMemberType(member);
            if (memberType != typeof(bool))
                throw new MemberException($"Field {type.Name}.{fieldName} is not a boolean field");

            return member;
        }

        private MemberInfo GetLastUpdateMember()
        {
            if (LastUpdateField == null)
                return null;

            var type = module.GetType();
            var member = GetNamedMember(LastUpdateField, Access.Write);
            var memberType = GetMemberType(member);
            if (memberType != typeof(double))
                throw new MemberException(
                    $"Field {type.Name}.{LastUpdateField} is not a double field"
                );

            return member;
        }

        private enum Access
        {
            Read = 0x1,
            Write = 0x2,
            ReadWrite = 0x3,
        }

        private class MemberException(string message) : Exception(message) { }

        private MemberInfo GetNamedMember(string fieldName, Access access)
        {
            var type = module.GetType();
            MemberInfo member =
                (MemberInfo)type.GetField(fieldName, BindingFlags.NonPublic)
                ?? type.GetProperty(fieldName, BindingFlags.NonPublic);

            if (member == null)
            {
                throw new MemberException(
                    $"Module of type {type.Name} has no field or property named '{fieldName}'"
                );
            }

            if (member is PropertyInfo property)
            {
                if ((access & Access.Read) != 0 && !property.CanRead)
                {
                    throw new MemberException(
                        $"Property {type.Name}.{fieldName} is required to be readable, but was not readable"
                    );
                }

                if ((access & Access.Write) != 0 && !property.CanWrite)
                {
                    throw new MemberException(
                        $"Property {type.Name}.{fieldName} is required to be writable, but was not writable"
                    );
                }
            }

            return member;
        }

        private bool EvaluateCondition()
        {
            if (module == null)
                return false;
            if (conditionMember == null)
                return false;

            bool invert = Condition.StartsWith("!");
            bool value = (bool)GetMemberValue(conditionMember, module);
            if (invert)
                value = !value;

            return value;
        }

        private static Type GetMemberType(MemberInfo member)
        {
            return member switch
            {
                FieldInfo fieldInfo => fieldInfo.FieldType,
                PropertyInfo propertyInfo => propertyInfo.PropertyType,
                _ => throw new NotImplementedException(),
            };
        }

        private static object GetMemberValue(MemberInfo member, object obj)
        {
            return member switch
            {
                FieldInfo fieldInfo => fieldInfo.GetValue(obj),
                PropertyInfo propertyInfo => propertyInfo.GetValue(obj),
                _ => throw new NotImplementedException(),
            };
        }

        private static void SetMemberValue<T>(MemberInfo member, object obj, T value)
        {
            if (member is FieldInfo fieldInfo)
                fieldInfo.SetValue(obj, value);
            else if (member is PropertyInfo propertyInfo)
                propertyInfo.SetValue(obj, value);
            else
                throw new NotImplementedException();
        }

        private static string GetNumberSuffix(int n)
        {
            var mod100 = n % 100;

            // Special case: 11th, 12th, 13th
            if (mod100 >= 10 && mod100 < 20)
                return "th";

            if (n % 10 == 1)
                return "st";
            if (n % 10 == 2)
                return "nd";
            if (n % 10 == 3)
                return "rd";
            return "th";
        }
    }
}
