using System;
using BackgroundResourceProcessing.Shim;
using Unity.Burst;
using Unity.Collections;

namespace BackgroundResourceProcessing.BurstSolver;

internal class BurstAssertException(string message) : Exception(message) { }

// internal static class BurstException
// {
//     delegate void ThrowExceptionDelegate(FixedString128 message);

//     static readonly FunctionPointer<ThrowExceptionDelegate> ThrowExceptionFp = default;

//     [BurstDiscard]
//     static void DoThrowException(FixedString128 message)
//     {
//         var msg = message.ToString();
//         LogUtil.Error(msg);

//         throw new BurstAssertException(msg);
//     }
// }
