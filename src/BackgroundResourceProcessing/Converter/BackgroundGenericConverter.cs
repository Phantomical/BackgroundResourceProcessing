using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Converter
{
    /// <summary>
    /// A background converter that uses the resource rates specified in the
    /// <see cref="ModuleResourceHandler"/> attached to the part module.
    ///
    /// This will work for most stock modules that have <c>RESOURCE</c> nodes,
    /// but modded modules are a crapshoot.
    /// </summary>
    public class BackgroundGenericConverter : BackgroundConverter
    {
        /// <summary>
        /// A condition to evaluate to determine whether this converter should
        /// be active. This can be any target filter expression.
        /// </summary>
        [KSPField]
        public string ActiveCondition = null;

        /// <summary>
        /// A condition that decides whether to apply <see cref="Multiplier"/>.
        /// </summary>
        [KSPField]
        public string MultiplierCondition = null;

        /// <summary>
        /// A multiplier to apply to resource rates.
        /// </summary>
        [KSPField]
        public double Multiplier = 1.0;

        /// <summary>
        /// A field that we should read to get the multiplier rate.
        /// </summary>
        [KSPField]
        public string MultiplierField = null;

        /// <summary>
        /// The name of a field to update with <c>Planetarium.GetUniversalTime()</c>
        /// when this ship is loaded.
        /// </summary>
        [KSPField]
        public string LastUpdateField = null;

        /// <summary>
        /// A field to read on the target type containing a list of input resources.
        ///
        /// The target field should be either a list of <c>ResourceRatio</c>s or
        /// <c>ResourceDefinition</c>s.
        /// </summary>
        [KSPField]
        public string InputsField = null;

        /// <summary>
        /// A field to read on the target type containing a list of output resources
        /// to read.
        ///
        /// The target field should be either a list of <c>ResourceRatio</c>s or
        /// <c>ResourceDefinition</c>s.
        /// </summary>
        [KSPField]
        public string RequirementsField = null;

        /// <summary>
        /// A field to read on the target type containing a list of resource
        /// constraints.
        ///
        /// The target field should be either a list of <c>ResourceRatio</c>s or
        /// <c>ResourceDefinition</c>s.
        /// </summary>
        [KSPField]
        public string OutputsField = null;

        bool disabled = false;

        private ModuleFilter activeConditionMember;
        private ModuleFilter multiplierConditionMember;
        private MemberInfo multiplierMember;
        private MemberInfo lastUpdateMember;
        private MemberInfo inputsMember;
        private MemberInfo outputsMember;
        private MemberInfo requiredMember;

        public override AdapterBehaviour GetBehaviour(PartModule module)
        {
            if (disabled)
                return null;

            if (!(EvaluateCondition(module, activeConditionMember) ?? true))
                return null;

            IEnumerable<ResourceRatio> inputs;
            IEnumerable<ResourceRatio> outputs;
            IEnumerable<ResourceConstraint> required = [];

            if (inputsMember != null)
            {
                inputs = GetMemberValue(inputsMember, module) switch
                {
                    IEnumerable<ResourceRatio> value => value,
                    IEnumerable<ModuleResource> value => value.Select(res => new ResourceRatio(
                        res.resourceDef.name,
                        res.rate,
                        false,
                        res.flowMode
                    )),
                    null => [],
                    _ => throw new NotImplementedException(),
                };
            }
            else
            {
                inputs = module.resHandler.inputResources.Select(res => new ResourceRatio(
                    res.resourceDef.name,
                    res.rate,
                    false,
                    res.flowMode
                ));
            }

            if (outputsMember != null)
            {
                outputs = GetMemberValue(outputsMember, module) switch
                {
                    IEnumerable<ResourceRatio> value => value,
                    IEnumerable<ModuleResource> value => value.Select(res => new ResourceRatio(
                        res.resourceDef.name,
                        res.rate,
                        false,
                        res.flowMode
                    )),
                    null => [],
                    _ => throw new NotImplementedException(),
                };
            }
            else
            {
                outputs = module.resHandler.outputResources.Select(res => new ResourceRatio(
                    res.resourceDef.name,
                    res.rate,
                    false,
                    res.flowMode
                ));
            }

            if (requiredMember != null)
            {
                required = GetMemberValue(requiredMember, module) switch
                {
                    IEnumerable<ResourceConstraint> value => value,
                    IEnumerable<ResourceRatio> value => value.Select(
                        ratio => new ResourceConstraint(ratio)
                    ),
                    IEnumerable<ModuleResource> value => value.Select(
                        res => new ResourceConstraint()
                        {
                            ResourceName = res.resourceDef.name,
                            Amount = res.amount,
                            FlowMode = res.flowMode,
                        }
                    ),
                    null => [],
                    _ => throw new NotImplementedException(),
                };
            }

            if (EvaluateCondition(module, multiplierConditionMember) ?? false)
            {
                double mult =
                    multiplierMember != null
                        ? (double)GetMemberValue(multiplierMember, module)
                        : Multiplier;

                inputs = inputs.Select(input => input.WithMultiplier(mult));
                outputs = outputs.Select(output => output.WithMultiplier(mult));
            }

            return new(new ConstantConverter(inputs.ToList(), outputs.ToList(), required.ToList()));
        }

        public override void OnRestore(PartModule module, ResourceConverter converter)
        {
            if (disabled)
                return;

            if (lastUpdateMember != null)
            {
                SetMemberValue(lastUpdateMember, module, Planetarium.GetUniversalTime());
            }
        }

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            string name = null;
            if (!node.TryGetValue("name", ref name))
                return;

            var module = AssemblyLoader.GetClassByName(typeof(PartModule), name);
            if (module == null)
            {
                LogUtil.Error($"{GetType().Name}: No target module with name {name}");
                return;
            }

            var type = module.GetType();

            try
            {
                if (ActiveCondition != null)
                    activeConditionMember = GetConditionFilter(ActiveCondition, node);
                if (MultiplierCondition != null)
                    multiplierConditionMember = GetConditionFilter(MultiplierCondition, node);
                if (MultiplierField != null)
                    multiplierMember = GetTypedMember<double>(type, MultiplierField, Access.Read);
                if (LastUpdateField != null)
                    lastUpdateMember = GetTypedMember<double>(type, LastUpdateField, Access.Write);
                if (InputsField != null)
                    inputsMember = GetRateListMember(type, InputsField);
                if (OutputsField != null)
                    outputsMember = GetRateListMember(type, OutputsField);
                if (RequirementsField != null)
                    requiredMember = GetConstraintListMember(type, RequirementsField);
            }
            catch (Exception e)
            {
                LogUtil.Error($"{e.Message}. This {GetType().Name} module will be disabled.");
                disabled = true;
            }
        }

        private static bool? EvaluateCondition(PartModule module, ModuleFilter filter)
        {
            if (filter == null)
                return null;

            return filter.Invoke(module);
        }

        private enum Access
        {
            Read = 0x1,
            Write = 0x2,
            ReadWrite = 0x3,
        }

        private class MemberException(string message) : Exception(message) { }

        private static ModuleFilter GetConditionFilter(string filter, ConfigNode node)
        {
            if (filter == null)
                return null;

            return ModuleFilter.Compile(filter, node);
        }

        private static MemberInfo GetTypedMember<T>(Type type, string fieldName, Access access)
        {
            var info = GetNamedMember(type, fieldName, access);
            if (info == null)
                return info;

            var memberType = GetMemberType(info);
            if (memberType != typeof(T))
            {
                if (typeof(T) == typeof(double) && memberType == typeof(float))
                    return info;

                throw new MemberException(
                    $"Member {type.Name}.{info.Name} is not a {typeof(T).Name}, got {memberType.Name} instead"
                );
            }

            return info;
        }

        private static MemberInfo GetRateListMember(Type type, string field)
        {
            var info = GetNamedMember(type, field, Access.Read);
            if (info == null)
                return null;

            var memberType = GetMemberType(info);
            if (typeof(IEnumerable<ResourceRatio>).IsAssignableFrom(memberType))
                return info;
            if (typeof(IEnumerable<ModuleResource>).IsAssignableFrom(memberType))
                return info;

            throw new MemberException(
                $"Member {type.Name}.{info.Name} is a compatible type with a input or output list"
            );
        }

        private static MemberInfo GetConstraintListMember(Type type, string field)
        {
            var info = GetNamedMember(type, field, Access.Read);
            if (info == null)
                return null;

            var memberType = GetMemberType(info);
            if (typeof(IEnumerable<ResourceRatio>).IsAssignableFrom(memberType))
                return info;
            if (typeof(IEnumerable<ModuleResource>).IsAssignableFrom(memberType))
                return info;
            if (typeof(IEnumerable<ResourceConstraint>).IsAssignableFrom(memberType))
                return info;

            throw new MemberException(
                $"Member {type.Name}.{info.Name} is a compatible type with a input or output list"
            );
        }

        private static MemberInfo GetNamedMember(Type type, string fieldName, Access access)
        {
            BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MemberInfo member =
                (MemberInfo)type.GetField(fieldName, flags) ?? type.GetProperty(fieldName, flags);

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
    }
}
