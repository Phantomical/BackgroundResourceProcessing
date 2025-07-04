using BackgroundResourceProcessing.Module;

namespace BackgroundResourceProcessing.Converter;

/// <summary>
/// A converter adapter that defers to the implementation of the
/// <see cref="IBackgroundConverter"/> interface on the part module.
/// </summary>
public class ModuleAdapter : BackgroundConverter<IBackgroundConverter>
{
    public override ModuleBehaviour GetBehaviour(IBackgroundConverter module)
    {
        return module.GetBehaviour();
    }

    public override void OnRestore(IBackgroundConverter module, ResourceConverter converter)
    {
        module.OnRestore(converter);
    }
}
