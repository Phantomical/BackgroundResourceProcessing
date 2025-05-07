using System;
using System.Collections.Generic;
using System.Linq;
using Google.OrTools.LinearSolver;
using Smooth.Collections;
using UnifiedBackgroundProcessing.Collections;

namespace UnifiedBackgroundProcessing.Modules
{
    internal static class DictUtil
    {
        internal static KeyValuePair<K, V> CreateKeyValuePair<K, V>(K key, V value)
        {
            return new KeyValuePair<K, V>(key, value);
        }
    }

    [Serializable]
    public struct InventoryId(uint partId, string resourceName)
    {
        public uint partId = partId;
        public string resourceName = resourceName;

        public InventoryId(PartResource resource)
            : this(resource.part.persistentId, resource.resourceName) { }

        public void Load(ConfigNode node)
        {
            node.TryGetValue("partId", ref partId);
            node.TryGetValue("resourceName", ref resourceName);
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("partId", partId);
            node.AddValue("resourceName", resourceName);
        }
    }

    /// <summary>
    /// A modelled resource inventory within a part.
    /// </summary>
    [Serializable]
    public class ResourceInventory
    {
        /// <summary>
        /// The persistent part id. Used to find the part again when the
        /// vessel goes off the rails.
        /// </summary>
        public uint partId;

        /// <summary>
        /// The name of the resource stored in this inventory.
        /// </summary>
        public string resourceName;

        /// <summary>
        /// How many units of resource are stored in this inventory.
        /// </summary>
        public double amount;

        /// <summary>
        /// The maximum number of units of resource that can be stored in
        /// inventory.
        /// </summary>
        public double maxAmount;

        /// <summary>
        /// The rate at which resources are currently being added or removed
        /// from this inventory.
        /// </summary>
        public double rate = 0.0;

        public bool Full => maxAmount - amount < 1e-6;
        public bool Empty => amount < 1e-6;

        public InventoryId Id => new(partId, resourceName);

        public ResourceInventory(PartResource resource)
        {
            var part = resource.part;

            partId = part.persistentId;
            resourceName = resource.resourceName;
            amount = resource.amount;
            maxAmount = resource.maxAmount;
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("partId", partId);
            node.AddValue("resourceName", resourceName);
            node.AddValue("amount", amount);
            node.AddValue("maxAmount", maxAmount);
        }

        public void Load(ConfigNode node)
        {
            node.TryGetValue("partId", ref partId);
            node.TryGetValue("resourceName", ref resourceName);
            node.TryGetValue("amount", ref amount);
            node.TryGetValue("maxAmount", ref maxAmount);
        }
    }

    /// <summary>
    /// Some common base properties shared between producers, consumers, and
    /// converters. Not meant to be used directly.
    /// </summary>
    public abstract class Base
    {
        public Dictionary<string, HashSet<uint>> push = [];
        public Dictionary<string, HashSet<uint>> pull = [];

        public double nextChangepoint = double.PositiveInfinity;

        public void Load(ConfigNode node)
        {
            node.TryGetValue("nextChangepoint", ref nextChangepoint);

            foreach (var inner in node.GetNodes("PUSH_INVENTORY"))
            {
                InventoryId id = new(0, "");
                id.Load(inner);

                AddPushInventory(id);
            }

            foreach (var inner in node.GetNodes("PULL_INVENTORY"))
            {
                InventoryId id = new(0, "");
                id.Load(inner);

                AddPullInventory(id);
            }
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("nextChangepoint", nextChangepoint);

            foreach (var entry in push)
            {
                var resourceName = entry.Key;
                foreach (var partId in entry.Value)
                {
                    var inner = node.AddNode("PUSH_INVENTORY");
                    new InventoryId(partId, resourceName).Save(inner);
                }
            }

            foreach (var entry in pull)
            {
                var resourceName = entry.Key;
                foreach (var partId in entry.Value)
                {
                    var inner = node.AddNode("PULL_INVENTORY");
                    new InventoryId(partId, resourceName).Save(inner);
                }
            }
        }

        public void AddPushInventory(InventoryId id)
        {
            if (!push.TryGetValue(id.resourceName, out var list))
            {
                list = [];
                push.Add(id.resourceName, list);
            }

            list.Add(id.partId);
        }

        public void AddPullInventory(InventoryId id)
        {
            if (!pull.TryGetValue(id.resourceName, out var list))
            {
                list = [];
                pull.Add(id.resourceName, list);
            }

            list.Add(id.partId);
        }
    }

    public class Converter(ConverterBehaviour behaviour)
    {
        public Dictionary<string, HashSet<uint>> push = [];
        public Dictionary<string, HashSet<uint>> pull = [];

        public ConverterBehaviour behaviour = behaviour;

        public Dictionary<string, ResourceRatio> inputs = [];
        public Dictionary<string, ResourceRatio> outputs = [];
        public Dictionary<string, ResourceRatio> required = [];

        public double nextChangepoint = double.PositiveInfinity;

        public void Refresh(VesselState state)
        {
            var resources = behaviour.GetResources(state);
            nextChangepoint = behaviour.GetNextChangepoint(state);

            inputs.Clear();
            outputs.Clear();
            required.Clear();

            foreach (var input in resources.Inputs)
                inputs.Add(input.ResourceName, input);
            foreach (var output in resources.Outputs)
                outputs.Add(output.ResourceName, output);
            foreach (var req in resources.Requirements)
                required.Add(req.ResourceName, req);
        }

        public void Load(ConfigNode node)
        {
            node.TryGetValue("nextChangepoint", ref nextChangepoint);

            foreach (var inner in node.GetNodes("PUSH_INVENTORY"))
            {
                InventoryId id = new(0, "");
                id.Load(inner);

                AddPushInventory(id);
            }

            foreach (var inner in node.GetNodes("PULL_INVENTORY"))
            {
                InventoryId id = new(0, "");
                id.Load(inner);

                AddPullInventory(id);
            }

            var bNode = node.GetNode("BEHAVIOUR");
            if (bNode != null)
                behaviour = ConverterBehaviour.LoadStatic(bNode);

            outputs.Clear();
            inputs.Clear();
            required.Clear();

            outputs.AddAll(
                ConfigUtil
                    .LoadOutputResources(node)
                    .Select(ratio => DictUtil.CreateKeyValuePair(ratio.ResourceName, ratio))
            );
            inputs.AddAll(
                ConfigUtil
                    .LoadInputResources(node)
                    .Select(ratio => DictUtil.CreateKeyValuePair(ratio.ResourceName, ratio))
            );
            required.AddAll(
                ConfigUtil
                    .LoadRequiredResources(node)
                    .Select(ratio => DictUtil.CreateKeyValuePair(ratio.ResourceName, ratio))
            );
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("nextChangepoint", nextChangepoint);

            foreach (var entry in push)
            {
                var resourceName = entry.Key;
                foreach (var partId in entry.Value)
                {
                    var inner = node.AddNode("PUSH_INVENTORY");
                    new InventoryId(partId, resourceName).Save(inner);
                }
            }

            foreach (var entry in pull)
            {
                var resourceName = entry.Key;
                foreach (var partId in entry.Value)
                {
                    var inner = node.AddNode("PULL_INVENTORY");
                    new InventoryId(partId, resourceName).Save(inner);
                }
            }

            behaviour.Save(node.AddNode("BEHAVIOUR"));
            ConfigUtil.SaveOutputResources(node, outputs.Select(output => output.Value));
            ConfigUtil.SaveInputResources(node, inputs.Select(input => input.Value));
            ConfigUtil.SaveRequiredResources(node, required.Select(required => required.Value));
        }

        public void AddPushInventory(InventoryId id)
        {
            if (!push.TryGetValue(id.resourceName, out var list))
            {
                list = [];
                push.Add(id.resourceName, list);
            }

            list.Add(id.partId);
        }

        public void AddPullInventory(InventoryId id)
        {
            if (!pull.TryGetValue(id.resourceName, out var list))
            {
                list = [];
                pull.Add(id.resourceName, list);
            }

            list.Add(id.partId);
        }
    }

    public class BackgroundProcessorModule : VesselModule
    {
        private List<Converter> converters = [];
        private Dictionary<InventoryId, ResourceInventory> inventories = [];

        private double lastUpdate = 0.0;
        private double nextChangepoint = 0.0;

        /// <summary>
        /// Whether background processing is actively running on this module.
        /// </summary>
        ///
        /// <remarks>
        /// Generally, this will be true when the vessel is on rails, and false
        /// otherwise.
        /// </remarks>
        public bool BackgroundProcessingActive { get; private set; } = false;

        public override Activation GetActivation()
        {
            return Activation.LoadedOrUnloaded;
        }

        public override int GetOrder()
        {
            return base.GetOrder();
        }

        protected override void OnStart()
        {
            var instance = UnifiedBackgroundProcessing.Instance;
            if (instance == null)
            {
                LogUtil.Error(
                    "BackgroundProcessorModule created but there is no active instance ",
                    "of the UnifiedBackgroundProcessing addon"
                );
                return;
            }

            instance.RegisterChangepointCallback(this, nextChangepoint);
        }

        protected void OnDestroy()
        {
            var instance = UnifiedBackgroundProcessing.Instance;
            if (instance == null)
            {
                LogUtil.Error(
                    "BackgroundProcessorModule destroyed but there is no active instance ",
                    "of the UnifiedBackgroundProcessing addon"
                );
                return;
            }

            instance.UnregisterChangepointCallbacks(this);
        }

        public override void OnUnloadVessel()
        {
            var currentTime = Planetarium.GetUniversalTime();
            var instance = UnifiedBackgroundProcessing.Instance;
            if (instance == null)
            {
                LogUtil.Error(
                    "BackgroundProcessorModule.OnUnloadVessel called but there is ",
                    "no active instance of the UnifiedBackgroundProcessing addon"
                );
                return;
            }

            RecordVesselState();
            ForceUpdateBehaviours(currentTime);
            ComputeRates();

            var changepoint = ComputeNextChangepoint(currentTime);
            nextChangepoint = changepoint;
            instance.RegisterChangepointCallback(this, changepoint);
        }

        public override void OnLoadVessel()
        {
            var currentTime = Planetarium.GetUniversalTime();

            UpdateInventories(currentTime);
            ApplyInventories();

            var instance = UnifiedBackgroundProcessing.Instance;
            if (instance == null)
            {
                LogUtil.Error(
                    "BackgroundProcessorModule.OnLoadVessel called but there is ",
                    "no active instance of the UnifiedBackgroundProcessing addon"
                );
                return;
            }

            instance.UnregisterChangepointCallbacks(this);
        }

        internal void OnChangepoint(double changepoint)
        {
            // We do nothing for active vessels.
            if (vessel.loaded)
                return;

            UpdateInventories(changepoint);
            UpdateBehaviours(changepoint);
            ComputeRates();

            var next = ComputeNextChangepoint(changepoint);
            nextChangepoint = next;

            var instance = UnifiedBackgroundProcessing.Instance;
            if (instance == null)
            {
                LogUtil.Error(
                    "BackgroundProcessorModule.OnChangepoint called but there is ",
                    "no active instance of the UnifiedBackgroundProcessing addon"
                );
                return;
            }

            instance.RegisterChangepointCallback(this, next);
        }

        /// <summary>
        /// Find all background processing modules and resources on this vessel
        /// and update the module state accordingly.
        /// </summary>
        private void RecordVesselState()
        {
            converters.Clear();
            inventories.Clear();

            lastUpdate = Planetarium.GetUniversalTime();

            foreach (var part in vessel.Parts)
            {
                var partId = part.persistentId;
                foreach (var resource in part.Resources)
                {
                    inventories.Add(
                        new InventoryId(partId, resource.resourceName),
                        new ResourceInventory(resource)
                    );
                }
            }

            var state = new VesselState
            {
                Vessel = vessel,
                CurrentTime = Planetarium.GetUniversalTime(),
            };

            foreach (var part in vessel.Parts)
            {
                var converters = part.FindModulesImplementing<BackgroundConverter>();

                // No point calculating linked inventories if there are no
                // background modules on the current part.
                if (converters.Count == 0)
                    continue;

                foreach (var module in converters)
                {
                    var behaviour = module.GetBehaviour();
                    if (behaviour == null)
                        continue;
                    behaviour.sourceModule = module.GetType().FullName;

                    var converter = new Converter(behaviour);
                    converter.Refresh(state);

                    foreach (var entry in converter.inputs)
                    {
                        var ratio = entry.Value;
                        var pull = GetConnectedResources(
                            part,
                            ratio.ResourceName,
                            ratio.FlowMode,
                            true
                        );
                        var push = GetConnectedResources(
                            part,
                            ratio.ResourceName,
                            ratio.FlowMode,
                            true
                        );

                        foreach (var resource in pull.set)
                            converter.AddPullInventory(new(resource));

                        foreach (var resource in push.set)
                            converter.AddPushInventory(new(resource));
                    }

                    this.converters.Add(converter);
                }
            }
        }

        /// <summary>
        /// Update the amount of resource stored in each resource.
        /// </summary>
        /// <param name="currentTime"></param>
        private void UpdateInventories(double currentTime)
        {
            var deltaT = currentTime - lastUpdate;

            if (deltaT < 1.0 / 30)
            {
                LogUtil.Warn(
                    $"Background update period for vessel {vessel.name} was less than 32ms"
                );
                return;
            }

            foreach (var entry in inventories)
            {
                var inventory = entry.Value;
                inventory.amount += inventory.rate * deltaT;

                if (inventory.Full)
                    inventory.amount = inventory.maxAmount;
                else if (inventory.Empty)
                    inventory.amount = 0.0;
            }
        }

        /// <summary>
        /// Update the sets of
        /// </summary>
        /// <param name="currentTime"></param>
        private void UpdateBehaviours(double currentTime)
        {
            VesselState state = new() { CurrentTime = currentTime, Vessel = Vessel };

            foreach (var converter in converters)
            {
                if (converter.nextChangepoint > currentTime)
                    continue;

                converter.Refresh(state);
            }
        }

        private void ForceUpdateBehaviours(double currentTime)
        {
            VesselState state = new() { CurrentTime = currentTime, Vessel = Vessel };

            foreach (var converter in converters)
                converter.Refresh(state);
        }

        private class InventoryVariables()
        {
            // Variables representing resource flow into this inventory from a converter.
            public List<Variable> inflow = [];

            // Variables representing resource flow out of this inventory to a converter.
            public List<Variable> outflow = [];

            // The subset of inflow variables that have DumpExcess set to false.
            public List<Variable> limited = [];
        }

        private void ComputeRates()
        {
            // What we want to do here is to calculate the rates at which the
            // resources in each inventory are changing. That is really the only
            // information we care about here.
            //
            // This can be represented as a linear programming problem.
            //
            // To start lets define some variables:
            // - let I be the number of inventories,
            // - let N be the number of converters,
            // - let R be the number of resources.
            //
            // We then define
            // - a_j to be the fraction of the time that converter j is running.
            //   It should be 1 for 100% of the time and 0 if it is not running at all.
            // - P_{j,k} to be the production rate of resource k by converter j
            // - C_{j,k} to be the consumption rate of resource k by converter j
            // - p_{i,j,k} to be the rate of resource k that converter j is emitting
            //   into inventory i
            // - c_{i,j,k} to be the same as above but for resource consumption
            // - r_{i,k} to be the net rate of change in inventory i for resource k.
            //   Note that this always be 0 for all but one resource for any given
            //   inventory.
            //
            // Now that we have those variables we can define our constraints:
            // - forall(i,k, sum(j in 0..N, p_{j,k} - c_{j,k})) == r_{i,k})
            // - r_{i,k} <= 0 if inventory i is full with resource k
            // - r_{i,k} >= 0 if inventory i is empty of resource k
            // - forall(j,k, sum(i in 0..I, p_{i,j,k}) == a_j * P_{j,k})
            // - forall(j,k, sum(i in 0..I, c_{i,j,k}) == a_j * C_{j,k})
            // - forall(j, 0 <= a_j <= 1)
            //
            // In words, this ends up being:
            // - r_{i,j,k} is the sum of the rates of resource k flowing into and
            //   out of inventory i.
            // - some boundary conditions on the rate (cannot overfill or take
            //   resources that are not present).
            // - The outflows of resource k from producer j sum up to its total
            //   production rate for resource k P_{j,k}.
            // - The inflows of resource k into producer j sum up to its total
            //   consumption rate for resource k C_{j,k}.
            // - A converter cannot be more than 100% active or run in reverse.
            //
            // The code below constructs a linear problem instance (with some
            // optimizations) and feeds that into OR-Tools' LP solver.
            //
            // There are still a bunch of improvements that can be made here:
            // - This completely ignores required resources. They don't really
            //   seem to be used much by KSP mods but it would be good to
            //   properly support them. I'm just genuinly not sure how to
            //   support them in the general case.
            // - In the case where there are no resource loops we can probably
            //   come up with a much simpler algorithm to solve this that would
            //   be much faster than calling out to a LP solver.

            var solver =
                Solver.CreateSolver("GLOP")
                ?? throw new Exception("GLOP solver not available within Google.OrTools.Solver");

            // The activation fractions of the converters.
            var active = solver.MakeNumVarArray(converters.Count, 0.0, 1.0, "active");
            var rates = solver.MakeNumVarArray(
                inventories.Count,
                double.NegativeInfinity,
                double.PositiveInfinity,
                "rates"
            );

            var invVars = new Dictionary<InventoryId, InventoryVariables>();
            var invRates = new Dictionary<InventoryId, Variable>();

            for (int i = 0; i < converters.Count; ++i)
            {
                var converter = converters[i];
                var rate = active[i];

                foreach (var entry in converter.inputs)
                {
                    var ratio = entry.Value;
                    LinearExpr sum = -ratio.Ratio * rate;

                    if (converter.pull.TryGetValue(ratio.ResourceName, out var pull))
                    {
                        foreach (var partId in pull)
                        {
                            var id = new InventoryId(partId, ratio.ResourceName);
                            var vars = invVars.GetOrInsert(id, new());
                            var pullvar = solver.MakeNumVar(0.0, double.PositiveInfinity, "");

                            vars.outflow.Add(pullvar);
                            sum += pullvar;
                        }
                    }

                    solver.Add(sum == 0.0);
                }

                foreach (var entry in converter.outputs)
                {
                    var ratio = entry.Value;
                    LinearExpr sum = -ratio.Ratio * rate;

                    if (converter.push.TryGetValue(ratio.ResourceName, out var push))
                    {
                        foreach (var partId in push)
                        {
                            var id = new InventoryId(partId, ratio.ResourceName);
                            var vars = invVars.GetOrInsert(id, new());
                            var pushvar = solver.MakeNumVar(0.0, double.PositiveInfinity, "");

                            if (!ratio.DumpExcess)
                                vars.limited.Add(pushvar);
                            vars.inflow.Add(pushvar);
                            sum += pushvar;
                        }
                    }

                    solver.Add(sum == 0.0);
                }
            }

            int index = 0;
            foreach (var entry in invVars)
            {
                var id = entry.Key;
                var vars = entry.Value;
                var rate = rates[index];
                var inventory = inventories[id];
                index += 1;

                invRates.Add(id, rate);

                LinearExpr rateEq = -rate;

                foreach (var inflow in vars.inflow)
                    rateEq += inflow;
                foreach (var outflow in vars.outflow)
                    rateEq -= outflow;

                solver.Add(rateEq == 0.0);

                if (inventory.Full)
                {
                    // We need to constrain non-DumpExcess inflows into this
                    // inventory to be <= outflows

                    LinearExpr constraint = null;

                    foreach (var outflow in vars.outflow)
                    {
                        if (constraint == null)
                            constraint = -outflow;
                        else
                            constraint -= outflow;
                    }

                    foreach (var inflow in vars.limited)
                    {
                        if (constraint == null)
                            constraint = inflow;
                        else
                            constraint += inflow;
                    }

                    if (constraint != null)
                        solver.Add(constraint <= 0.0);
                }
                else if (inventory.Empty)
                {
                    // No equivalent to DumpExcess here. We just need to ensure
                    // that inflows >= outflows

                    LinearExpr constraint = null;

                    foreach (var outflow in vars.outflow)
                    {
                        if (constraint == null)
                            constraint = -outflow;
                        else
                            constraint -= outflow;
                    }

                    foreach (var inflow in vars.outflow)
                    {
                        if (constraint == null)
                            constraint = inflow;
                        else
                            constraint += inflow;
                    }

                    if (constraint != null)
                        solver.Add(constraint >= 0.0);
                }
                else
                {
                    // There is no constraint on the rate here. It could be
                    // positive or negative and both are OK.
                }
            }

            LinearExpr f = null;
            foreach (var a in active)
            {
                if (f == null)
                    f = a;
                else
                    f += a;
            }

            solver.Maximize(f);

            foreach (var entry in invRates)
            {
                var id = entry.Key;
                var variable = entry.Value;
                var inv = inventories[id];
                var rate = variable.SolutionValue();

                // Due to DumpExcess converters the rate can be positive even
                // if the inventory is full. It should never be negative if
                // the inventory is empty but we clamp it here anyway just to
                // be safe.
                if (inv.Empty)
                    rate = Math.Max(rate, 0.0);
                else if (inv.Full)
                    rate = Math.Min(rate, 0.0);

                inv.rate = rate;
            }
        }

        private double ComputeNextChangepoint(double currentTime)
        {
            double changepoint = double.PositiveInfinity;

            foreach (var entry in inventories)
            {
                var inv = entry.Value;

                if (inv.rate == 0.0)
                    continue;

                double duration;
                if (inv.rate < 0.0)
                    duration = inv.amount / -inv.rate;
                else
                    duration = (inv.maxAmount - inv.amount) / inv.amount;

                changepoint = Math.Min(changepoint, currentTime + duration);
            }

            foreach (var converter in converters)
                changepoint = Math.Min(changepoint, converter.nextChangepoint);

            return changepoint;
        }

        /// <summary>
        /// Update all inventories on the vessel to reflect those stored within
        /// this module.
        /// </summary>
        private void ApplyInventories()
        {
            Dictionary<uint, Part> parts = [];
            parts.AddAll(
                vessel.Parts.Select(part => DictUtil.CreateKeyValuePair(part.persistentId, part))
            );

            foreach (var entry in inventories)
            {
                var inventory = entry.Value;
                if (!parts.TryGetValue(inventory.partId, out var part))
                    continue;

                var resource = part.Resources.Get(inventory.resourceName);
                if (resource == null)
                    continue;

                resource.amount = Math.Min(inventory.amount, resource.maxAmount);
            }
        }

        private PartSet.ResourcePrioritySet GetConnectedResources(
            Part part,
            string resourceName,
            ResourceFlowMode flowMode,
            bool pulling
        )
        {
            int resourceId = resourceName.GetHashCode();

            // This switch roughly mirrors the switch in in Part.GetConnectedResourceTotals
            switch (flowMode)
            {
                case ResourceFlowMode.ALL_VESSEL:
                case ResourceFlowMode.STAGE_PRIORITY_FLOW:
                case ResourceFlowMode.ALL_VESSEL_BALANCE:
                case ResourceFlowMode.STAGE_PRIORITY_FLOW_BALANCE:
                    if (part.vessel == null)
                        return new();

                    return vessel.resourcePartSet.GetResourceList(resourceId, pulling, false);
                case ResourceFlowMode.STACK_PRIORITY_SEARCH:
                case ResourceFlowMode.STAGE_STACK_FLOW:
                case ResourceFlowMode.STAGE_STACK_FLOW_BALANCE:
                    return part.crossfeedPartSet.GetResourceList(resourceId, pulling, false);

                default:
                    return BuildResourcePrioritySet(GetFlowingResources(part.Resources, pulling));
            }
        }

        private List<PartResource> GetFlowingResources(PartResourceList resources, bool pulling)
        {
            List<PartResource> output = [];

            foreach (var resource in resources.dict.Values)
            {
                if (!resource.flowState)
                    continue;

                if (pulling)
                {
                    if (
                        (resource.flowMode & PartResource.FlowMode.Out)
                        != PartResource.FlowMode.None
                    )
                        output.Add(resource);
                }
                else
                {
                    if (
                        (resource.flowMode & PartResource.FlowMode.In) != PartResource.FlowMode.None
                    )
                        output.Add(resource);
                }
            }

            return output;
        }

        private PartSet.ResourcePrioritySet BuildResourcePrioritySet(List<PartResource> resources)
        {
            PartSet.ResourcePrioritySet set = new();
            set.lists.Add(resources);
            set.set.AddAll(resources);
            return set;
        }

        private static LinearExpr SumVariables(IEnumerable<Variable> vars)
        {
            IEnumerator<Variable> enumerator = vars.GetEnumerator();

            if (!enumerator.MoveNext())
                return null;
            LinearExpr expr = enumerator.Current;

            while (enumerator.MoveNext())
                expr += enumerator.Current;

            return expr;
        }
    }
}
