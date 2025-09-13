using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.BurstSolver;

internal readonly struct Result
{
    readonly Error err;

    public bool IsOk => err == Error.Success;
    public bool IsErr => !IsOk;

    public static Result Ok => Error.Success;

    public static Result Err(Error err) => new(err);

    public Error Error
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!TryGetError(out var err))
                ThrowNotErr();
            return err;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result() => err = Error.Success;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result(Error err) => this.err = err;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Result(Error err) => new(err);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Match(out Error err) => !TryGetError(out err);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetError(out Error err)
    {
        err = this.err;
        return IsErr;
    }

    public readonly void Unwrap()
    {
        if (err == Error.Success)
            return;

        err.ThrowRepresentativeError();
    }

    [IgnoreWarning(1370)]
    static void ThrowNotErr() =>
        throw new InvalidOperationException(
            "attempted to get the Err variant of a result that was not Err"
        );
}

[DebuggerDisplay("{DebugDisplay}")]
internal readonly struct Result<T>
{
    enum State : byte
    {
        Invalid = 0,
        Ok,
        Err,
    }

    readonly Error err;
    readonly T ok;

    [DebuggerHidden]
    public bool IsOk => err == Error.Success;

    [DebuggerHidden]
    public bool IsErr => !IsOk;

    [DebuggerHidden]
    private object DebugDisplay
    {
        get
        {
            if (Match(out var ok, out var err))
                return $"Ok {ok}";
            else
                return $"Err {err}";
        }
    }

    [DebuggerHidden]
    public T Ok
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!TryGetOk(out T ok))
                ThrowNotOk();
            return ok;
        }
    }

    [DebuggerHidden]
    public Error Err
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!TryGetErr(out var err))
                ThrowNotErr();
            return err;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result(T ok)
    {
        this.err = Error.Success;
        this.ok = ok;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result(Error err)
    {
        this.err = err;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Result<T>(T ok) => new(ok);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Result<T>(Error err) => new(err);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Result(Result<T> result) => new(result.err);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetOk(out T ok)
    {
        if (err == Error.Success)
        {
            ok = this.ok;
            return true;
        }
        else
        {
            ok = default;
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetErr(out Error err)
    {
        err = this.err;
        return err != Error.Success;
    }

    [IgnoreWarning(1370)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Match(out T ok, out Error err)
    {
        ok = this.ok;
        err = this.err;

        return IsOk;
    }

    public readonly T Unwrap()
    {
        if (Match(out var ok, out var err))
            return ok;

        err.ThrowRepresentativeError();
        throw new NotImplementedException();
    }

    [IgnoreWarning(1370)]
    static void ThrowNotOk() =>
        throw new InvalidOperationException(
            "attempted to get the Ok variant of a result that was not Ok"
        );

    [IgnoreWarning(1370)]
    static void ThrowNotErr() =>
        throw new InvalidOperationException(
            "attempted to get the Err variant of a result that was not Err"
        );
}
