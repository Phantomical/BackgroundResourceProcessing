using System;
using System.Collections.Generic;
using System.Reflection;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Inventory;

public class BackgroundGenericInventory : BackgroundInventory
{
    readonly Dictionary<string, PartResource> resources = [];

    public override List<FakePartResource> GetResources(PartModule module)
    {
        List<FakePartResource> outputs = null;

        foreach (var resource in resources.Values)
        {
            if (!resource.condition.Evaluate(module))
                continue;

            outputs ??= [];
            outputs.Add(
                new()
                {
                    ResourceName = resource.ResourceName,
                    Amount = resource.Amount.Evaluate(module) ?? 0.0,
                    MaxAmount = resource.MaxAmount.Evaluate(module) ?? 0.0,
                }
            );
        }

        return outputs;
    }

    public override void UpdateResource(PartModule module, ResourceInventory inventory)
    {
        if (!resources.TryGetValue(inventory.ResourceName, out var resource))
            return;

        var original = resource.GetField(module) ?? inventory.OriginalAmount;
        var delta = original - inventory.OriginalAmount;
        var amount = MathUtil.Clamp(inventory.Amount + delta, 0.0, inventory.MaxAmount);

        resource.SetField(module, amount);
    }

    public override void UpdateSnapshot(
        ProtoPartModuleSnapshot module,
        ResourceInventory inventory,
        SnapshotUpdate update
    )
    {
        var node = module.moduleValues;
        if (!resources.TryGetValue(inventory.ResourceName, out var resource))
        {
            base.UpdateSnapshot(module, inventory, update);
            return;
        }

        if (!resource.UpdateConfigNode)
        {
            base.UpdateSnapshot(module, inventory, update);
            return;
        }

        node.TryGetValue(resource.Field.Name, ref inventory.Amount);
        base.UpdateSnapshot(module, inventory, update);
        node.SetValue(resource.Field.Name, inventory.Amount, createIfNotFound: true);
        inventory.OriginalAmount = inventory.Amount;
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        var target = GetTargetType(node);
        foreach (var child in node.GetNodes("RESOURCE"))
        {
            var resource = PartResource.Load(target, child);
            if (resources.ContainsKey(resource.ResourceName))
                throw new Exception(
                    $"duplicate resource name `{resource.ResourceName}` in RESOURCE blocks"
                );

            resources.Add(resource.ResourceName, resource);
        }
    }

    struct PartResource()
    {
        public ConditionalExpression condition = ConditionalExpression.Always;
        public string ResourceName;
        public FieldExpression<double> Amount = new(_ => 0.0, "0");
        public FieldExpression<double> MaxAmount = new(_ => 0.0, "0");

        /// <summary>
        /// The field that will be updated with the amount of resource
        /// stored within this inventory.
        /// </summary>
        public FieldInfo Field = null;

        /// <summary>
        /// Enable updating of the config node based on the field name.
        /// </summary>
        ///
        /// <remarks>
        /// This should work if the target field uses a KSPField attribute
        /// but not otherwise.
        /// </remarks>
        public bool UpdateConfigNode = false;

        public static PartResource Load(Type target, ConfigNode node)
        {
            PartResource res = new();

            node.TryGetCondition(nameof(condition), target, ref res.condition);
            node.TryGetValue(nameof(ResourceName), ref res.ResourceName);

            if (res.ResourceName == null)
                throw new Exception("ResourceName must be specified in RESOURCE node");

            string field = null;
            if (node.TryGetValue(nameof(Field), ref field))
            {
                const BindingFlags Flags =
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var info = target.GetField(field, Flags);

                if (info == null)
                    throw new Exception(
                        $"Target module {target.Name} contains no field named {field}"
                    );

                if (info.FieldType != typeof(double) && info.FieldType != typeof(float))
                    throw new Exception(
                        $"Field {target.Name}.{info.Name} must be of type double or float"
                    );

                res.Field = info;
                res.Amount = FieldExpression<double>.Field(field, target);
                res.UpdateConfigNode = IsKspField(info);
            }

            node.TryGetExpression(nameof(Amount), target, ref res.Amount);
            node.TryGetExpression(nameof(MaxAmount), target, ref res.MaxAmount);
            node.TryGetValue(nameof(UpdateConfigNode), ref res.UpdateConfigNode);

            return res;
        }

        public readonly void SetField(PartModule module, double value)
        {
            if (Field == null)
                return;

            if (Field.FieldType == typeof(double))
                Field.SetValue(module, value);
            else if (Field.FieldType == typeof(float))
                Field.SetValue(module, (float)value);
            else
                throw new NotImplementedException(
                    $"Cannot set a field of type `{Field.FieldType.Name}`"
                );
        }

        public readonly double? GetField(PartModule module)
        {
            if (Field == null)
                return null;

            return Field.GetValue(module) switch
            {
                double d => d,
                float f => (double)f,
                _ => throw new NotImplementedException(
                    $"not able to read field {Field.DeclaringType.Name}.{Field.Name} of type {Field.FieldType.Name}"
                ),
            };
        }

        public static bool IsKspField(FieldInfo field) =>
            field.GetCustomAttribute<KSPField>() != null;
    }
}
