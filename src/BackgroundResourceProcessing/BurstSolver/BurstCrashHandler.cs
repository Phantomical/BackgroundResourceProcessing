using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BackgroundResourceProcessing.Shim;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using static UnityEngine.Application;

namespace BackgroundResourceProcessing.BurstSolver;

internal class BurstException(string message) : Exception(message) { }

internal class UnreachableCodeException(string message = "reached unreachable code")
    : Exception(message) { }

/// <summary>
/// Crash handling for burst-compiled code.
/// </summary>
///
/// <remarks>
/// Burst does not support exceptions directly. It converts any throw statements
/// effectively into a segfault, which then takes down the whole game. This is,
/// frankly, terrible, and will result it a whole bunch of pain for users of
/// BRP.
///
/// To work around this, we call a managed callback that takes care of actually
/// throwing the exception for us. Mono has specific support for unwinding across
/// native frames, so this actually works out great for us.
/// </remarks>
internal static unsafe class BurstCrashHandler
{
    delegate void CrashHandlerDelegate(Error err, ref int? param);

    struct CrashHandlerVTable
    {
        public FunctionPointer<CrashHandlerDelegate> CrashHandler;
    }

    #region Initialization
    static readonly SharedStatic<CrashHandlerVTable> Shared;

    static BurstCrashHandler()
    {
        if (BurstUtil.UseTestAllocator)
            return;

        Shared = SharedStatic<CrashHandlerVTable>.GetOrCreate<CrashHandlerVTable>();
        InitShared();
    }

    [BurstDiscard]
    static void InitShared()
    {
        ref var vtable = ref Shared.Data;
        vtable.CrashHandler = new(Marshal.GetFunctionPointerForDelegate(CrashHandler));
    }

    public static void Init() { }
    #endregion

    #region Crash API
    [IgnoreWarning(1370)]
    public static void Crash(Error err, int? param = null)
    {
        if (!BurstUtil.IsBurstCompiled)
            CrashHandlerManaged(err, param);
        CrashImpl(err, param);

        // This cannot be a throw statement since LLVM seems to move that before
        // the actual call to CrashImpl.
        //
        // We put Assume(false) here instead as otherwise the optimizer cannot
        // tell that CrashImpl never returns and is unable to optimize away future
        // branch conditions.
        //
        // In practice this means that burst puts an `int3` instruction just after
        // the method call, so even if we did return it would still result in the
        // application aborting.
        Hint.Assume(false);
    }

    [IgnoreWarning(1370)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CrashImpl(Error err, int? param)
    {
        Shared.Data.CrashHandler.Invoke(err, ref param);

        // If the crash handler returns then we should at least attempt to print
        // out some useful information
        if (param is not null)
            UnityEngine.Debug.LogError(
                $"[BackgroundResourceProcessing] burst solver crashed unexpectedly with error code {(int)err} and parameter {(int)param}"
            );
        else
            UnityEngine.Debug.LogError(
                $"[BackgroundResourceProcessing] burst solver crashed unexpectedly with error code {(int)err}"
            );

        throw new BurstException("crash handler did not terminate the process");
    }

    [BurstDiscard]
    private static void CrashHandlerManaged(Error err, int? param) =>
        err.ThrowRepresentativeError(param);

    [BurstDiscard]
    private static void CrashHandler(Error err, ref int? param)
    {
        try
        {
            err.ThrowRepresentativeError(param);
        }
        catch (Exception exc)
        {
            // We _explicitly_ want native stack traces here, so we can at
            // least get a semblance of proper debug info.
            var logType = GetStackTraceLogType(UnityEngine.LogType.Exception);
            SetStackTraceLogType(UnityEngine.LogType.Exception, UnityEngine.StackTraceLogType.Full);

            UnityEngine.Debug.LogException(exc);

            SetStackTraceLogType(UnityEngine.LogType.Exception, logType);
            throw;
        }
        throw new BurstException(err.ToString());
    }
    #endregion
}
