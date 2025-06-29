namespace BackgroundResourceProcessing.Converter
{
    public class BackgroundSolarPanel : BackgroundConverter<ModuleDeployableSolarPanel>
    {
        public override ModuleBehaviour GetBehaviour(ModuleDeployableSolarPanel module)
        {
            if (module.flowRate == 0.0)
                return null;

            ResourceRatio ratio = new()
            {
                ResourceName = module.resourceName,
                FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE,
            };

            return new(new ConstantProducer([ratio]));
        }
    }
}
