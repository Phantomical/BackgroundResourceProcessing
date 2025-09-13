using System;

namespace BackgroundResourceProcessing.Utils;

[AttributeUsage(
    AttributeTargets.Constructor
        | AttributeTargets.Method
        | AttributeTargets.Class
        | AttributeTargets.Struct
)]
internal class MustUseReturnValueAttribute : Attribute { }
