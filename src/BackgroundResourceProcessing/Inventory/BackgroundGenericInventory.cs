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

            double maxAmount = Math.Max(resource.MaxAmount.Evaluate(module) ?? 0.0, 0.0);
            double amount = MathUtil.Clamp(resource.Amount.Evaluate(module) ?? 0.0, 0.0, maxAmount);

            outputs ??= [];
            outputs.Add(
                new()
                {
                    ResourceName = resource.ResourceName,
                    Amount = amount,
                    MaxAmount = maxAmount,
                }
            );
        }

        return outputs;
    }

    public override void UpdateResource(PartModule module, ResourceInventory inventory)
    {
        if (!resources.TryGetValue(inventory.ResourceName, out var resource))
            return;

        var original = resource.Field?.GetValue(module) ?? inventory.OriginalAmount;
        var delta = original - inventory.OriginalAmount;
        var amount = MathUtil.Clamp(inventory.Amount + delta, 0.0, inventory.MaxAmount);

        resource.Field?.SetValue(module, amount);
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

        if (resource.Field is null)
        {
            base.UpdateSnapshot(module, inventory, update);
            return;
        }

        var accessor = resource.Field.Value;

        node.TryGetValue(accessor.Name, ref inventory.Amount);
        base.UpdateSnapshot(module, inventory, update);
        node.SetValue(accessor.Name, inventory.Amount, createIfNotFound: true);
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

        /// <summary>
        /// The amount of <see cref="ResourceName"/> that is currently stored
        /// in this inventory.
        /// </summary>
        public FieldExpression<double> Amount = new(_ => 0.0, "0");

        /// <summary>
        /// The maximum amount of <see cref="ResourceName"/> that can be stored
        /// in this inventory. This can be infinite.
        /// </summary>
        public FieldExpression<double> MaxAmount = new(_ => 0.0, "0");

        /// <summary>
        /// The field that will be updated with the amount of resource
        /// stored within this inventory.
        /// </summary>
        public MemberAccessor<double>? Field = null;

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
                var accessor = new MemberAccessor<double>(target, field);

                res.Field = accessor;
                res.Amount = FieldExpression<double>.Field(field, target);
                res.UpdateConfigNode = IsKspField(accessor);
            }

            node.TryGetExpression(nameof(Amount), target, ref res.Amount);
            node.TryGetExpression(nameof(MaxAmount), target, ref res.MaxAmount);
            node.TryGetValue(nameof(UpdateConfigNode), ref res.UpdateConfigNode);

            return res;
        }

        public static bool IsKspField(MemberAccessor<double> field) =>
            field.Member.GetCustomAttribute<KSPField>() != null;
    }
}
