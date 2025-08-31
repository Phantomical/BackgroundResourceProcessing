namespace BackgroundResourceProcessing;

/// <summary>
/// Additional options controlling how a converter gets added to a resource
/// processor or simulator.
/// </summary>
public struct AddConverterOptions()
{
    /// <summary>
    /// This converter should be linked to every non-module inventory that
    /// contains a relevant inventory in its inputs, outputs, or requirements.
    /// </summary>
    public bool LinkToAll = false;
}
