// using System.Collections.Generic;
// using Smooth.Collections;
// using BackgroundResourceProcessing.Collections;
// using BackgroundResourceProcessing.Modules;

// namespace BackgroundResourceProcessing.Solver
// {
//     /*
//         Solving for rates of change in all inventories on a ship.

//         When we build a model for an unloaded vessel we end up with a graph
//         formed of converters and inventories. Converters pull resources from
//         inventories and then push other resources out to inventories. This
//         graph may have arbitrary cycles.

//         Solving this whole problem in one go is too hard. Instead, we're going
//         to try to break down the problem into something more manageable.

//         The first thing we can do is to combine together multiple inventories
//         that have the same set of converters and consumers interacting with
//         them. (e.g. combine all the batteries on a ship into one logical
//         "super-battery").

//         The next thing we can do is to combine together multiple converters
//         that produce/consume from the same set of inventories. (e.g. combine
//         together all solar panels). Note that inventories are specific to
//         a resource so this naturally keeps things separated.

//         The next thing we can do is to classify edges based on the state of
//         the inventory they are attached to.
//         - For full inventories we need input rate <= output rate.
//         - For empty inventories we need input rate >= output rate.
//         - Otherwise there is no constraint on the rate into the inventory.

//         We can then remove any edges that either:
//         - go solely through unconstrained inventories,
//         - push to a combination of full inventories with no consumers and
//           unconstrained inventories, or,
//         - pull from a combination of empty inventories with no producers
//           and unconstrained inventories.

//         Note that having production that goes to non-uniform sets of inventories
//         is somewhat rare in KSP.

//         Now that we've broken some edges we should hopefully end up with a
//         (much smaller) multigraph with multiple sub-graph.

//         These graphs may have cycles, and those make things hard. So we want to
//         get rid of them. We do this by repeating the following procedure:
//         - Find a loop within the graph (ideally the smallest)
//         - Collapse the vertices making up the loop into a single "summary"
//           converter vertex that has the net inputs/outputs of the loop
//         - Go back to step 1 until there are no more loops

//         In practice, the way this works is that we use Tarjan's algorithm to
//         find the strongly-connected components of the graph and then do
//         the cycle-finding procedure above on each component until it is
//         collapsed to a single vertex.

//         At this point we have a DAG. I'll figure this out later.

//      */

//     internal enum InventoryState
//     {
//         /// <summary>
//         /// There is no constraint on flow through this inventory.
//         /// </summary>
//         Unconstrained = 0,

//         /// <summary>
//         /// This inventory is empty. Inflow must be greater than or equal to outflow.
//         /// </summary>
//         Empty = 1,

//         /// <summary>
//         /// This inventory is full. Inflow must be greater than or equal to inflow.
//         /// </summary>
//         Full = 2,

//         /// <summary>
//         /// This inventory has size zero. Inflow must equal outflow.
//         /// </summary>
//         Zero = 3,
//     }

//     internal class ConverterNode
//     {
//         public int priority;

//         public HashSet<InventoryId> inputs = [];
//         public HashSet<InventoryId> outputs = [];

//         public HashSet<string> constraints = [];
//         public HashSet<string> dumpExcess = [];

//         public Converter converter;

//         public ConverterNode(BackgroundProcessorModule module, Converter converter)
//         {
//             priority = converter.behaviour.Priority;

//             foreach (var inv in converter.pull) {

//             }
//         }
//     }

//     internal class InventoryNode
//     {
//         public InventoryState state = InventoryState.Zero;
//         public Dictionary<int, double> inflow = [];
//         public Dictionary<int, double> outflow = [];

//         public double amount = 0.0;
//         public double maxAmount = 0.0;

//         public HashSet<InventoryId> ids = [];

//         public InventoryNode() { }

//         public InventoryNode(BackgroundProcessorModule module, ResourceInventory inventory)
//         {
//             amount = inventory.amount;
//             maxAmount = inventory.maxAmount;
//             ids = [inventory.Id];

//             state = InventoryState.Unconstrained;
//             if (inventory.Full)
//                 state |= InventoryState.Full;
//             if (inventory.Empty)
//                 state |= InventoryState.Empty;
//         }

//         /// <summary>
//         /// Merge <paramref name="other"/> into this inventory node.
//         /// </summary>
//         /// <param name="other"></param>
//         public void Merge(InventoryNode other)
//         {
//             ids.AddAll(other.ids);
//             state &= other.state;
//             amount += other.amount;
//             maxAmount += other.maxAmount;

//             foreach (var (key, value) in other.inflow)
//             {
//                 if (!inflow.TryAdd(key, value))
//                     inflow[key] += value;
//             }

//             foreach (var (key, value) in other.outflow)
//             {
//                 if (!outflow.TryAdd(key, value))
//                     outflow[key] += value;
//             }
//         }
//     }

//     internal class ResolvedConverterNode
//     {
//         public double weight = 0.0;

//         public Dictionary<int, double> inputs = [];
//         public Dictionary<int, double> outputs = [];
//     }

//     internal class ResourceGraph
//     {
//         Dictionary<InventoryId, int> inventoryIds;
//         Dictionary<int, InventoryNode> inventories;
//         Dictionary<int, ConverterNode> converters;

//         UnionFind inventoryRef;
//         UnionFind converterRef;

//         public ResourceGraph(BackgroundProcessorModule module)
//         {
//             Dictionary<string, double> resourceTotals = [];
//             foreach (var (_, inventory) in module.inventories)
//             {
//                 if (!resourceTotals.TryAdd(inventory.resourceName, inventory.amount))
//                     resourceTotals[inventory.resourceName] += inventory.amount;
//             }

//             BuildInventories(module);
//             BuildConverters(module);
//         }

//         private void BuildInventories(BackgroundProcessorModule module)
//         {
//             inventoryIds = new(module.inventories.Count);
//             inventories = new(module.inventories.Count);
//             inventoryRef = new(module.inventories.Count);

//             int index = 0;
//             foreach (var (id, inventory) in module.inventories)
//             {
//                 inventoryIds.Add(id, index);
//                 inventories.Add(index, new InventoryNode(module, inventory));
//             }
//         }

//         private void BuildConverters(BackgroundProcessorModule module)
//         {
//             converters = new(module.converters.Count);
//             converterRef = new(module.converters.Count);

//             int index = 0;
//             foreach (var converter in module.converters)
//             {
//                 index += 1;
//             }
//         }
//     }
// }
