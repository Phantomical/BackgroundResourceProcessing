namespace BackgroundResourceProcessing.Integration.SystemHeat;

/// <summary>
/// A dummy PartModule added to the vessel root part to anchor per-loop
/// heat-flux fake inventories. It has no logic of its own; BackgroundSystemHeatFluxInventory
/// creates the FakePartResources and BackgroundConverter adapters reference
/// those inventories via Push/Pull on this module.
/// </summary>
public class BRPSystemHeatMarker : PartModule { }
