using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Solver;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.BurstSolver;

/// <summary>
/// This tracks internal errors that could conceivably occur, so that we can
/// report them in a bug report at the end.
/// </summary>
///
/// <remarks>
/// Any exception thrown within burst basically gets turned directly into a
/// null dereference, which takes down the entire game. That's acceptable in
/// for cases where we can be confident enough in our testing, but in general
/// we would like to avoid that. This is the alternative.
///
/// As a rule of thumb, I have been giving everything that is not an OOB
/// access on an array its own error code here.
/// </remarks>
internal enum Error : byte
{
    Success = 0,

    /// <summary>
    /// The linear problem was either unsolvable or has no fixed maximum.
    /// </summary>
    Unsolvable,

    /// <summary>
    /// The solver produced a solution that didn't actually satisfy the
    /// constraints it was constructed with.
    /// </summary>
    SolutionDidNotSatisfyConstraints,

    /// <summary>
    /// We tried to access a variable map entry that we expected to exist.
    /// </summary>
    MissingVarMapEntry,

    /// <summary>
    /// There was a duplicate variable entry when constructing the inverse
    /// variable map.
    /// </summary>
    DuplicateVarMapEntry,
}

internal static class BurstError
{
    static bool UseResultErrors => BurstUtil.IsBurstCompiled;

    public static void ThrowRepresentativeError(this Error err)
    {
        switch (err)
        {
            case Error.Unsolvable:
                throw new UnsolvableProblemException();
            case Error.SolutionDidNotSatisfyConstraints:
                throw new InvalidOperationException(
                    "computed solution did not satisfy the problem constraints"
                );
            case Error.MissingVarMapEntry:
                throw new KeyNotFoundException("variable map was missing a variable entry");
            case Error.DuplicateVarMapEntry:
                throw new InvalidOperationException(
                    "variable map had multiple entries mapping to the same value"
                );
        }

        throw new Exception(err.ToString());
    }

    public static Error Unsolvable()
    {
        // We always return unsolvable as a result because it gets handled by
        // SolveBranchAndBound.
        return Error.Unsolvable;
    }

    public static Error SolutionDidNotSatisfyConstraints() =>
        Error.SolutionDidNotSatisfyConstraints;

    [IgnoreWarning(1370)]
    public static Error MissingVarMapEntry(int index)
    {
        if (UseResultErrors)
            throw new KeyNotFoundException($"variable map missing entry for variable {index}");
        else
            return Error.MissingVarMapEntry;
    }

    [IgnoreWarning(1370)]
    public static Error DuplicateVarMapEntry(int index)
    {
        if (UseResultErrors)
            throw new InvalidOperationException(
                $"variable map had duplicate entries for variable {index}"
            );
        else
            return Error.DuplicateVarMapEntry;
    }
}
