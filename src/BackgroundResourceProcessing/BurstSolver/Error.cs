using System;
using System.Collections.Generic;
using System.Data;
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

    RawIntMap_KeyNotFound,
    RawIntMap_KeyOutOfBounds,
    RawIntMap_KeyExists,

    GraphConverter_DifferentInputResources,
    GraphConverter_DifferentOutputResources,

    GraphInventory_DifferentResources,

    LinearEquation_DestinationTooSmall,

    LinearPresolve_StatesSpanMismatch,
    LinearPresolve_StopIndexOutOfRange,

    LinearProblem_CreateVariables_CountIsNegative,
    LinearProblem_StandardizeConstraint_Equality,
    LinearProblem_StandardizeConstraint_InvalidRelation,
    LinearProblem_BuildVarMap_WrongVariableCount,

    Simplex_SolveTableau_SelectedTooSmall,

    RawList_PopEmpty,
    RawList_IndexOutOfRange,
    RawList_SizeIsNegative,
    RawList_CapacityIsNegative,
    RawList_Truncate_IndexIsNegative,

    AdjacencyMatrix_RemoveUnequalColumns_WrongSpanCapacity,
    AdjacencyMatrix_RemoveUnequalColumns_ColumnIndexOutOfRange,
    AdjacencyMatrix_FillUpperDiagonal_WrongMatrixSize,
    AdjacencyMatrix_RemoveUnequalRows_WrongMatrixSize,
    AdjacencyMatrix_RowIndexOutOfRange,
    AdjacencyMatrix_RowSizeOutOfRange,
    AdjacencyMatrix_ColSizeOutOfRange,

    BitSpan_IndexOutOfRange,
    BitSpan_MismatchedCapacities,

    LinearMap_KeyNotFound,
    LinearMap_KeyAlreadyExists,

    MemorySpan_IndexOutOfRange,
    MemorySpan_NegativeLength,
    MemorySpan_CopyTo_DestinationTooShort,

    RawArray_IndexOutOfRange,

    Matrix_RowOutOfRange,
    Matrix_ColOutOfRange,
    Matrix_ArrayTooSmall,
    Matrix_NegativeCols,
    Matrix_NegativeRows,
    Matrix_PivotOutOfRange,

    Result_UnwrapNotOk,
    Result_UnwrapNotErr,
}

internal static class BurstError
{
    static bool UseResultErrors => BurstUtil.IsBurstCompiled;

    public static void ThrowRepresentativeError(this Error err, int? param = null)
    {
        throw err switch
        {
            Error.Unsolvable => new UnsolvableProblemException(),
            Error.SolutionDidNotSatisfyConstraints => new InvalidOperationException(
                "computed solution did not satisfy the problem constraints"
            ),
            Error.MissingVarMapEntry => new KeyNotFoundException(
                "variable map was missing a variable entry"
            ),
            Error.DuplicateVarMapEntry => new InvalidOperationException(
                "variable map had multiple entries mapping to the same value"
            ),

            Error.RawIntMap_KeyNotFound => new KeyNotFoundException(
                $"key {param} was not found in the map"
            ),
            Error.RawIntMap_KeyOutOfBounds => new KeyNotFoundException(
                $"key {param} was outside of the int map range"
            ),
            Error.RawIntMap_KeyExists => new ArgumentException(
                $"key {param} already exists in the map"
            ),

            Error.RawList_PopEmpty => new InvalidOperationException(
                "cannot pop an item from an empty list"
            ),
            Error.RawList_IndexOutOfRange => new ArgumentOutOfRangeException(
                "index",
                $"list index was out of range (index {param})"
            ),
            Error.RawList_SizeIsNegative => new ArgumentOutOfRangeException(
                "size",
                "requested size was negative"
            ),
            Error.RawList_CapacityIsNegative => new ArgumentOutOfRangeException(
                "size",
                "requested capacity was negative"
            ),
            Error.RawList_Truncate_IndexIsNegative => new ArgumentOutOfRangeException(
                "index",
                "truncate index was negative"
            ),

            Error.GraphConverter_DifferentInputResources => new InvalidOperationException(
                "attempted to merge converters with different input resources"
            ),
            Error.GraphConverter_DifferentOutputResources => new InvalidOperationException(
                "attempted to merge converters with different output resources"
            ),

            Error.GraphInventory_DifferentResources => new InvalidOperationException(
                "attempted to merge two inventories with different resources"
            ),

            Error.LinearEquation_DestinationTooSmall => new InvalidOperationException(
                "destination was too small to store all equation coefficients"
            ),

            Error.LinearPresolve_StatesSpanMismatch => new ArgumentException(
                "states span was not the same length as matrix rows"
            ),
            Error.LinearPresolve_StopIndexOutOfRange => new ArgumentOutOfRangeException(
                "stop",
                $"stop index was larger than the matrix height (stop index {param})"
            ),

            Error.LinearProblem_CreateVariables_CountIsNegative => new ArgumentOutOfRangeException(
                "count",
                "attempted to create a negative number of variables"
            ),
            Error.LinearProblem_StandardizeConstraint_Equality => new ArgumentException(
                "cannot standardize an equality constraint"
            ),
            Error.LinearProblem_StandardizeConstraint_InvalidRelation =>
                new InvalidOperationException("invalid constraint relation"),
            Error.LinearProblem_BuildVarMap_WrongVariableCount => new ArgumentException(
                "variable count was not the right length"
            ),

            Error.Simplex_SolveTableau_SelectedTooSmall => new ArgumentException(
                "selected was too small for tableau"
            ),

            Error.AdjacencyMatrix_RemoveUnequalColumns_ColumnIndexOutOfRange =>
                new ArgumentOutOfRangeException("column", "column index was out of range"),
            Error.AdjacencyMatrix_RemoveUnequalColumns_WrongSpanCapacity => new ArgumentException(
                "span capacity did not match matrix columns"
            ),
            Error.AdjacencyMatrix_FillUpperDiagonal_WrongMatrixSize =>
                new InvalidOperationException(
                    "cannot fill the upper diagonal on a matrix that is taller than it is wide"
                ),
            Error.AdjacencyMatrix_RemoveUnequalRows_WrongMatrixSize => new ArgumentException(
                "equal matrix was too small",
                "equal"
            ),
            Error.AdjacencyMatrix_RowIndexOutOfRange => new IndexOutOfRangeException(
                $"row index ({param}) was out of range"
            ),
            Error.AdjacencyMatrix_RowSizeOutOfRange => new ArgumentOutOfRangeException("rows"),
            Error.AdjacencyMatrix_ColSizeOutOfRange => new ArgumentOutOfRangeException("cols"),

            Error.BitSpan_IndexOutOfRange => new IndexOutOfRangeException(
                $"bitspan index ({param}) was out of range"
            ),
            Error.BitSpan_MismatchedCapacities => new ArgumentException(
                "bitspan instances have different capacities"
            ),

            Error.LinearMap_KeyNotFound => new KeyNotFoundException(
                "key not found in the linear map"
            ),
            Error.LinearMap_KeyAlreadyExists => new ArgumentException(
                "an item with the same key already exists in the map"
            ),

            Error.MemorySpan_IndexOutOfRange => new IndexOutOfRangeException(
                $"span index ({param}) was out of range"
            ),
            Error.MemorySpan_NegativeLength => new ArgumentOutOfRangeException(
                "span length was negative"
            ),
            Error.MemorySpan_CopyTo_DestinationTooShort => new ArgumentException(
                "MemorySpan.CopyTo destination was too short"
            ),

            Error.RawArray_IndexOutOfRange => new IndexOutOfRangeException(
                $"array index ({param}) out of range"
            ),

            Error.Matrix_RowOutOfRange => new IndexOutOfRangeException(
                $"row index ({param}) was out of range"
            ),
            Error.Matrix_ColOutOfRange => new IndexOutOfRangeException(
                $"column index ({param}) was out of range"
            ),
            Error.Matrix_ArrayTooSmall => new ArgumentException(
                "provided array is too small for matrix size"
            ),
            Error.Matrix_NegativeCols => new ArgumentException("cols"),
            Error.Matrix_NegativeRows => new ArgumentException("rows"),
            Error.Matrix_PivotOutOfRange => new ArgumentOutOfRangeException(
                "pivot",
                $"pivot index ({param}) was out of range"
            ),

            Error.Result_UnwrapNotOk => new InvalidOperationException(
                "attempted to get the Ok variant of a result that was not Ok"
            ),
            Error.Result_UnwrapNotErr => new InvalidOperationException(
                "attempted to get the Err variant of a result that was not Err"
            ),

            _ => new BurstException(err.ToString()),
        };
    }

    public static void ThrowUnsolvable() => BurstCrashHandler.Crash(Error.Unsolvable);

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
