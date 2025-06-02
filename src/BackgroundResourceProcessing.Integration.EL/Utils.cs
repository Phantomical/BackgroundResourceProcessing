using System;

namespace BackgroundResourceProcessing.Integration.EL
{
    internal static class ResourceRatioExtension
    {
        public static ResourceRatio WithMultiplier(this ResourceRatio res, double multiplier)
        {
            res.Ratio *= multiplier;
            return res;
        }
    }

    internal static class LinkedModuleUtil
    {
        public static T GetLinkedModule<T>(PartModule self, ref uint? cachedModuleId)
            where T : PartModule
        {
            var module =
                GetLinkedModuleCached<T>(self, ref cachedModuleId)
                ?? self.part.FindModuleImplementing<T>();
            if (module == null)
                return null;

            cachedModuleId = module.PersistentId;
            return module;
        }

        private static T GetLinkedModuleCached<T>(PartModule self, ref uint? cachedModuleId)
            where T : PartModule
        {
            if (cachedModuleId == null)
                return null;

            var moduleId = (uint)cachedModuleId;
            var module = self.part.Modules[moduleId];

            return module as T;
        }
    }

    internal static class MathUtil
    {
        public static double Clamp01(double v)
        {
            return Math.Min(Math.Max(v, 0.0), 1.0);
        }
    }
}
