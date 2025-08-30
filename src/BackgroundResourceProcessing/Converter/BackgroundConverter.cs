using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Inventory;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Converter;

/// <summary>
/// The net behaviour of a part module.
/// </summary>
///
/// <remarks>
/// In most cases this will just be a wrapper around a single
/// <see cref="ConverterBehaviour"/> but it also allows you to set things
/// up for more advanced use cases involving fake inventories or multiple
/// converters on the same part.
/// </remarks>
public class ModuleBehaviour()
{
    /// <summary>
    /// A list of <see cref="ConverterBehaviour"/>s.
    /// </summary>
    public List<ConverterBehaviour> Converters
    {
        get => converters ??= [];
        set => converters = value;
    }
    internal List<ConverterBehaviour> converters = null;

    /// <summary>
    /// A set of <see cref="PartModule"/>s to inspect for background
    /// inventories. Any part modules that have a valid
    /// <see cref="BackgroundInventory"/> entry will be added to the set of
    /// inventories that the converters in <see cref="Converters"/> can push
    /// resource to.
    /// </summary>
    public List<PartModule> Push
    {
        get => push ??= [];
        set => push = value;
    }
    internal List<PartModule> push = null;

    /// <summary>
    /// A set of <see cref="PartModule"/>s to inspect for background
    /// inventories. Any part modules that have a valid
    /// <see cref="BackgroundInventory"/> entry will be added to the set of
    /// inventories that the converters in <see cref="Converters"/> can pull
    /// resource from.
    /// </summary>
    public List<PartModule> Pull
    {
        get => pull ??= [];
        set => pull = value;
    }
    internal List<PartModule> pull = null;

    /// <summary>
    /// A set of <see cref="PartModule"/>s to inspect for background
    /// inventories. Any part modules that have a valid
    /// <see cref="BackgroundInventory"/> entry will be added to the set of
    /// inventories that the converters in <see cref="Converters"/>
    /// reference for constraints.
    /// </summary>
    public List<PartModule> Constraint
    {
        get => constraint ??= [];
        set => constraint = value;
    }
    internal List<PartModule> constraint = null;

    public ModuleBehaviour(ConverterBehaviour behaviour)
        : this()
    {
        Converters = [behaviour];
    }

    public ModuleBehaviour(List<ConverterBehaviour> behaviours)
        : this()
    {
        Converters = behaviours;
    }

    /// <summary>
    /// Add a new <see cref="ConverterBehaviour"/>.
    /// </summary>
    /// <param name="behaviour"></param>
    public void Add(ConverterBehaviour behaviour) => Converters.Add(behaviour);

    /// <summary>
    /// Add a part module that this converter will push resources to.
    /// </summary>
    ///
    /// <remarks>
    /// This is only for use with <see cref="BackgroundInventory"/> modules.
    /// Regular resources are connected using KSPs flow rules.
    /// </remarks>
    public void AddPushModule(PartModule module) => Push.Add(module);

    /// <summary>
    /// Add a part module that this converter will push resources to.
    /// </summary>
    ///
    /// <remarks>
    /// This is only for use with <see cref="BackgroundInventory"/> modules.
    /// Regular resources are connected using KSPs flow rules.
    /// </remarks>
    public void AddPullModule(PartModule module) => Pull.Add(module);

    /// <summary>
    /// Add a part module that this converter will refer to when determining
    /// whether its requirements are satisfied.
    /// </summary>
    ///
    /// <remarks>
    /// This is only for use with <see cref="BackgroundInventory"/> modules.
    /// Regular resources are connected using KSPs flow rules.
    /// </remarks>
    public void AddConstraintModule(PartModule module) => Constraint.Add(module);
}

/// <summary>
/// An adapter for a part module that defines the behaviour said converter
/// will have when run in the background.
/// </summary>
public abstract class BackgroundConverter : IRegistryItem
{
    public const string NodeName = "BACKGROUND_CONVERTER";
    private static readonly TypeRegistry<BackgroundConverter> registry = new(NodeName);

    private readonly BaseFieldList fields;
    private readonly List<PriorityBlock> priorities = [];

    public BackgroundConverter()
    {
        fields = new(this);
    }

    /// <summary>
    /// Get the behaviour for a specific part module.
    /// </summary>
    /// <param name="module"></param>
    /// <returns>
    ///   An <see cref="ModuleBehaviour"/>, or <c>null</c> if the
    ///   there is nothing active on <paramref name="module"/>.
    /// </returns>
    public abstract ModuleBehaviour GetBehaviour(PartModule module);

    /// <summary>
    /// Called during vessel restore for each <see cref="ConverterBehaviour"/>
    /// that was emitted by this adapter.
    /// </summary>
    /// <param name="module"></param>
    /// <param name="converter"></param>
    public virtual void OnRestore(PartModule module, ResourceConverter converter) { }

    /// <summary>
    /// Load your adapter from a <see cref="ConfigNode"/>.
    /// </summary>
    ///
    /// <remarks>
    /// The base method will automatically deserialize any fields annotated
    /// with <c>[KSPField]</c>.
    /// </remarks>
    protected virtual void OnLoad(ConfigNode node)
    {
        fields.Load(node);

        var target = GetTargetType(node);
        foreach (var priority in node.GetNodes("PRIORITY"))
            priorities.Add(PriorityBlock.Load(priority, target));
    }

    /// <summary>
    /// Determine the correct priority for the given module based on the
    /// <c>PRIORITY</c> blocks on the current converter.
    /// </summary>
    public virtual int GetModulePriority(PartModule module)
    {
        foreach (var priority in priorities)
        {
            if (priority.Condition.Evaluate(module))
                return priority.Value;
        }

        return 0;
    }

    void IRegistryItem.OnLoad(ConfigNode node)
    {
        OnLoad(node);
    }

    public static BackgroundConverter Load(ConfigNode node)
    {
        string adapter = null;
        if (!node.TryGetValue("adapter", ref adapter))
        {
            LogUtil.Error($"{NodeName} did not have an `adapter` key");
            return null;
        }

        Type adapterType = AssemblyLoader.GetClassByName(typeof(BackgroundConverter), adapter);
        if (adapterType == null)
        {
            LogUtil.Error($"{NodeName}: Unable to find BackgroundConverter {adapter}");
            return null;
        }

        try
        {
            var instance = (BackgroundConverter)Activator.CreateInstance(adapterType);
            instance.OnLoad(node);
            return instance;
        }
        catch (Exception e)
        {
            LogUtil.Error($"{NodeName}: {adapter} load threw an exception: {e}");
            return null;
        }
    }

    public static BackgroundConverter GetConverterForType(Type type)
    {
        return registry.GetEntryForType(type);
    }

    public static BackgroundConverter GetConverterForModule(PartModule module)
    {
        return GetConverterForType(module.GetType());
    }

    public static Type GetTargetType(ConfigNode node)
    {
        string name = null;
        if (!node.TryGetValue("name", ref name))
            throw new Exception("GetTargetType: ConfigNode was missing a `name` field");

        var type = AssemblyLoader.GetClassByName(typeof(PartModule), name);
        if (type == null)
            throw new NullReferenceException(
                $"GetTargetType: No PartModule type exists with name `{name}`"
            );
        return type;
    }

    internal static void LoadAll()
    {
        registry.LoadAll();
    }

    private struct PriorityBlock()
    {
        public ConditionalExpression Condition = ConditionalExpression.Always;
        public int Value = 0;

        public static PriorityBlock Load(ConfigNode node, Type target)
        {
            PriorityBlock block = new();
            node.TryGetValue("Value", ref block.Value);

            string condition = null;
            if (node.TryGetValue("Condition", ref condition))
                block.Condition = ConditionalExpression.Compile(condition, node, target);

            return block;
        }
    }
}

/// <summary>
/// A helper class that allows you to implement <c>GetBehaviour</c> for the
/// type you actually care about, instead of <c><see cref="PartModule"/></c>.
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class BackgroundConverter<T> : BackgroundConverter
    where T : PartModule
{
    public sealed override ModuleBehaviour GetBehaviour(PartModule module)
    {
        if (module == null)
            return GetBehaviour((T)null);

        if (module is not T downcasted)
        {
            LogUnexpectedType(module);
            return null;
        }

        return GetBehaviour(downcasted);
    }

    public abstract ModuleBehaviour GetBehaviour(T module);

    public sealed override int GetModulePriority(PartModule module)
    {
        if (module == null)
            return GetModulePriority((T)null);

        if (module is not T downcasted)
        {
            LogUnexpectedType(module);
            return 0;
        }

        return GetModulePriority(downcasted);
    }

    public virtual int GetModulePriority(T module)
    {
        return base.GetModulePriority(module);
    }

    public sealed override void OnRestore(PartModule module, ResourceConverter converter)
    {
        if (module is not T downcasted)
            LogUnexpectedType(module);
        else
            OnRestore(downcasted, converter);
    }

    public virtual void OnRestore(T module, ResourceConverter converter) { }

    private void LogUnexpectedType(PartModule module)
    {
        LogUtil.Error(
            $"{GetType().Name}: Expected a part module derived from {typeof(T).Name} but got {module.GetType().Name} instead"
        );
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        var target = GetTargetType(node);
        if (typeof(T).IsAssignableFrom(target))
            return;

        LogUtil.Error(
            $"{GetType().Name}: Adapter expected a type assignable to {typeof(T).Name} but {target.Name} is not"
        );
    }
}
