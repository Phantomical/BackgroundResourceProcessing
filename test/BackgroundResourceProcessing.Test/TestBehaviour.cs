using BackgroundResourceProcessing.Behaviour;

namespace BackgroundResourceProcessing.Test;

/// <summary>
/// This behaviour type doesn't actually implement anything but it can be used
/// to replace behaviours from integrations in tests.
/// </summary>
public class TestBehaviour : ConverterBehaviour
{
    public override ConverterResources GetResources(VesselState state) =>
        throw new NotImplementedException();
}
