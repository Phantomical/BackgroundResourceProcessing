using System;
using System.Reflection;

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
    public class ModuleBackgroundConditionalConverter : ModuleBackgroundConstantConverter
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

        [KSPField(isPersistant = true)]
        private uint? cachedPersistentModuleId = null;

        private PartModule module = null;
        private MemberInfo conditionMember = null;

        private PartModule GetLinkedModuleCached()
        {
            if (module != null)
                return module;

            if (cachedPersistentModuleId == null)
                return null;
            var persistentId = (uint)cachedPersistentModuleId;
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
            base.OnStart(state);
            Setup();
        }

        protected override ConverterBehaviour GetConverterBehaviour()
        {
            if (!EvaluateCondition())
                return null;

            return base.GetConverterBehaviour();
        }

        private void Setup()
        {
            module = GetLinkedModule();
            if (module == null)
                return;
            conditionMember = GetConditionMember();
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
                    LogUtil.Error(
                        $"No module of type {TargetModule} found on part {part.partName}. ",
                        $"This {GetType().Name} module will be disabled."
                    );
                }
                else
                {
                    var suffix = GetNumberSuffix(index);
                    LogUtil.Error(
                        $"There is no {index}{suffix} module of type {TargetModule} found on part {part.partName}. ",
                        $"This {GetType().Name} module will be disabled."
                    );
                }

                return null;
            }

            cachedPersistentModuleId = found.PersistentId;
            return found;
        }

        private MemberInfo GetConditionMember()
        {
            var fieldName = Condition;
            if (fieldName.StartsWith("!"))
                fieldName = fieldName.Substring(1);

            var type = module.GetType();
            MemberInfo member = type.GetField(fieldName, BindingFlags.NonPublic);
            if (member == null)
            {
                var info = type.GetProperty(fieldName, BindingFlags.NonPublic);
                if (info.CanRead)
                    member = info;
            }

            if (member == null)
            {
                LogUtil.Error(
                    $"Module of type {type.Name} has no field or readable property named '{fieldName}'. ",
                    $"This {GetType().Name} module will be disabled."
                );
                return null;
            }

            var memberType = GetMemberType(member);
            if (memberType != typeof(bool))
            {
                LogUtil.Error(
                    $"Field '{fieldName}' on type {type.Name} is not a boolean field. ",
                    $"This {GetType().Name} module will be disabled."
                );
                return null;
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
