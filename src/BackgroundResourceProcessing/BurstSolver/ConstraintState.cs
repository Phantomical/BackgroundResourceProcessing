namespace BackgroundResourceProcessing.BurstSolver;

internal enum ConstraintState : byte
{
    VALID = 0,
    UNSOLVABLE,
    VACUOUS,
}
