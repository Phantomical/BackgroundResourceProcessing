using System.Collections.Generic;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Inventory;

namespace BackgroundResourceProcessing.Module;

/// <summary>
/// An interface that allows <see cref="PartModule"/>s to implement their
/// own background converter behaviour.
/// </summary>
///
/// <remarks>
/// If you implement this on your module then you can register it with
/// BRP by writing a <c>BACKGROUND_CONVERTER</c> entry like this:
/// <code>
/// BACKGROUND_CONVERTER
/// {
///     name = MyModule
///     adapter = ModuleAdapter
/// }
/// </code>
/// </remarks>
public interface IBackgroundConverter
{
    /// <summary>
    /// Get the behaviour of this part module.
    /// </summary>
    /// <returns>
    ///   An <see cref="ModuleBehaviour"/>, or <c>null</c> if the
    ///   there is nothing active on the current module.
    /// </returns>
    ModuleBehaviour GetBehaviour();

    /// <summary>
    /// A callback that is called when the vessel is being restored.
    /// </summary>
    /// <param name="converter"></param>
    void OnRestore(ResourceConverter converter);
}

/// <summary>
/// An interface that allows <see cref="PartModule"/>s to implement their
/// own background inventory code.
/// </summary>
///
/// <remarks>
/// If you implement this on your module then you can register it with
/// BRP by writing a <c>BACKGROUND_INVENTORY</c> entry like this:
/// <code>
/// BACKGROUND_INVENTORY
/// {
///     name = MyModule
///     adapter = ModuleAdapter
/// }
/// </code>
/// </remarks>
public interface IBackgroundInventory
{
    /// <summary>
    /// Get a list of <see cref="FakePartResource"/>s that are present in
    /// this module.
    /// </summary>
    ///
    /// <remarks>
    /// Each resource here will create a new inventory in the resource
    /// processor.
    /// </remarks>
    List<FakePartResource> GetResources();

    /// <summary>
    /// Update this resource with the new state for one of its inventories.
    /// </summary>
    ///
    /// <remarks>
    /// This method will be called once for every resource that was returned
    /// from <see cref="GetResources"/>.
    /// </remarks>
    void UpdateResource(ResourceInventory inventory);
}
