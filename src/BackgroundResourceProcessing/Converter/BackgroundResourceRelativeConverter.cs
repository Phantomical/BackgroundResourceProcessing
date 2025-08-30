using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;
using ScaledRatio = BackgroundResourceProcessing.Behaviour.ResourceRelativeBehaviour.ScaledRatio;

namespace BackgroundResourceProcessing.Converter;

public class BackgroundResourceRelativeConverter : BackgroundConverter
{
    public struct ScaledRatioExpression()
    {
        public ResourceRatioExpression ResourceRatio;
        public ConditionalExpression Fixed = ConditionalExpression.Never;

        public static ScaledRatioExpression Load(Type target, ConfigNode node)
        {
            ScaledRatioExpression result = new()
            {
                ResourceRatio = ResourceRatioExpression.Load(target, node),
            };
            node.TryGetCondition(nameof(Fixed), target, ref result.Fixed);

            return result;
        }

        public static List<ScaledRatioExpression> LoadList(
            Type target,
            ConfigNode node,
            string nodeName
        )
        {
            var nodes = node.GetNodes(nodeName);
            List<ScaledRatioExpression> ratios = new(nodes.Length);
            foreach (var child in nodes)
                ratios.Add(Load(target, child));
            return ratios;
        }

        public bool Evaluate(PartModule module, out ScaledRatio ratio)
        {
            if (!ResourceRatio.Evaluate(module, out ResourceRatio resourceRatio))
            {
                ratio = default;
                return false;
            }

            ratio = new ScaledRatio()
            {
                ResourceRatio = resourceRatio,
                Fixed = Fixed.Evaluate(module),
            };

            return true;
        }
    }

    public List<ScaledRatioExpression> inputs = [];
    public List<ScaledRatioExpression> outputs = [];
    public List<ResourceConstraintExpression> required = [];

    public ConditionalExpression ActiveCondition = ConditionalExpression.Always;

    [KSPField]
    public bool PushToLocalBackgroundInventory = false;

    [KSPField]
    public bool PullFromLocalBackgroundInventory = false;

    [KSPField]
    public string LastUpdateField = null;

    /// <summary>
    /// The name of the resource whose amount is used to determine the other
    /// rates of this converter.
    /// </summary>
    [KSPField]
    public string RelativeResource;

    /// <summary>
    /// The resource flow mode that is used to determine which inventories are
    /// counted when computing the resource amount.
    /// </summary>
    [KSPField]
    public ResourceFlowMode FlowMode = ResourceFlowMode.NULL;

    /// <summary>
    /// The maximum relative error that this behaviour will allow before
    /// creating a changepoint.
    /// </summary>
    [KSPField]
    public double MaxError = 0.1;

    /// <summary>
    /// The minimum amount of time between consecutive changepoints for this
    /// converter, in seconds.
    /// </summary>
    ///
    /// <remarks>
    /// This is to prevent infinite changepoints if the resource this behaviour
    /// depends on is decreasing due to other converters.
    /// </remarks>
    [KSPField]
    public double MinChangepointDelta = 600.0;

    private List<ConverterMultiplier> multipliers = [];
    private List<LinkExpression> links = [];
    private MemberAccessor<double>? lastUpdateField = null;

    public override ModuleBehaviour GetBehaviour(PartModule module)
    {
        if (!ActiveCondition.Evaluate(module))
            return null;

        var inputs = this.inputs.TrySelect(
            (ScaledRatioExpression input, out ScaledRatio value) =>
                input.Evaluate(module, out value)
        );
        var outputs = this.outputs.TrySelect(
            (ScaledRatioExpression output, out ScaledRatio value) =>
                output.Evaluate(module, out value)
        );
        var required = this.required.TrySelect(
            (ResourceConstraintExpression req, out ResourceConstraint value) =>
                req.Evaluate(module, out value)
        );

        var mult = 1.0;
        foreach (var field in multipliers)
            mult *= field.Evaluate(module);

        if (mult != 1.0)
        {
            inputs = inputs.Select(input =>
            {
                input.Ratio *= mult;
                return input;
            });
            outputs = outputs.Select(output =>
            {
                output.Ratio *= mult;
                return output;
            });
        }

        var behaviour = new ModuleBehaviour(
            new ResourceRelativeBehaviour()
            {
                Inputs = [.. inputs],
                Outputs = [.. outputs],
                Required = [.. required],
                ResourceName = RelativeResource,
                MaxError = MaxError,
                Connected = GetConnectedResources(module),
            }
        );

        if (PushToLocalBackgroundInventory)
            behaviour.AddPushModule(module);
        if (PullFromLocalBackgroundInventory)
            behaviour.AddPullModule(module);

        foreach (var link in links)
            link.Evaluate(module, behaviour);

        return behaviour;
    }

    public override void OnRestore(PartModule module, ResourceConverter converter)
    {
        lastUpdateField?.SetValue(module, Planetarium.GetUniversalTime());
    }

    private DynamicBitSet GetConnectedResources(PartModule module)
    {
        var part = module.part;
        var vessel = module.vessel;
        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        var resourceId = RelativeResource.GetHashCode();

        var flowMode = FlowMode;
        if (flowMode == ResourceFlowMode.NULL)
            flowMode = PartResourceLibrary.GetDefaultFlowMode(resourceId);

        DynamicBitSet connected = new(processor.Inventories.Count);
        PartSet.ResourcePrioritySet partSet = null;

        switch (flowMode)
        {
            case ResourceFlowMode.ALL_VESSEL:
            case ResourceFlowMode.STAGE_PRIORITY_FLOW:
            case ResourceFlowMode.ALL_VESSEL_BALANCE:
            case ResourceFlowMode.STAGE_PRIORITY_FLOW_BALANCE:
                if (vessel == null)
                    return [];

                partSet = vessel.resourcePartSet.GetResourceList(resourceId, true, false);
                break;

            case ResourceFlowMode.STACK_PRIORITY_SEARCH:
            case ResourceFlowMode.STAGE_STACK_FLOW:
            case ResourceFlowMode.STAGE_STACK_FLOW_BALANCE:
                partSet = part.crossfeedPartSet.GetResourceList(resourceId, true, false);
                break;

            default:
                var index = GetFlowingResourceInventoryIndex(processor, part, resourceId);
                if (index is not null)
                    connected[(int)index] = true;

                break;
        }

        if (partSet != null)
        {
            foreach (var item in GetNestedEnumerator(partSet.lists))
            {
                var index = processor.GetInventoryIndex(new(item));
                if (index == null)
                    continue;

                connected[(int)index] = true;
            }
        }

        foreach (var link in links)
        {
            if (!link.Relation.HasFlag(LinkExpression.LinkRelation.REQUIRED))
                continue;
            var target = link.EvaluateTarget(module);
            if (target == null)
                continue;

            var index = processor.GetInventoryIndex(new(target, RelativeResource));
            if (index == null)
                continue;

            connected[(int)index] = true;
        }

        return connected;
    }

    private int? GetFlowingResourceInventoryIndex(
        BackgroundResourceProcessor processor,
        Part part,
        int resourceId
    )
    {
        var resource = part.Resources.GetFlowing(resourceId, true);
        if (resource == null)
            return null;

        return processor.GetInventoryIndex(new(resource));
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        var target = GetTargetType(node);

        node.TryGetCondition(nameof(ActiveCondition), target, ref ActiveCondition);

        multipliers = ConverterMultiplier.LoadAll(target, node);
        links = LinkExpression.LoadList(target, node);

        inputs.AddRange(ScaledRatioExpression.LoadList(target, node, "INPUT_RESOURCE"));
        outputs.AddRange(ScaledRatioExpression.LoadList(target, node, "OUTPUT_RESOURCE"));
        required.AddRange(ResourceConstraintExpression.LoadRequirements(target, node));

        if (LastUpdateField != null)
            lastUpdateField = new(target, LastUpdateField, MemberAccessor<double>.Access.Write);
    }

    private static NestedListEnumerator<T> GetNestedEnumerator<T>(List<List<T>> list) => new(list);

    private struct NestedListEnumerator<T>(List<List<T>> list) : IEnumerator<T>
    {
        private static readonly List<T> Empty = [];

        List<List<T>>.Enumerator outer = list.GetEnumerator();
        List<T>.Enumerator inner = Empty.GetEnumerator();

        public T Current => inner.Current;
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (true)
            {
                if (inner.MoveNext())
                    return true;

                inner.Dispose();

                if (!outer.MoveNext())
                {
                    inner = Empty.GetEnumerator();
                    return false;
                }

                inner = outer.Current.GetEnumerator();
            }
        }

        void IEnumerator.Reset() => throw new NotImplementedException();

        public void Dispose()
        {
            inner.Dispose();
            outer.Dispose();
        }

        public readonly NestedListEnumerator<T> GetEnumerator() => this;
    }
}
