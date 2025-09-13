using System;

namespace BackgroundResourceProcessing.BurstSolver;

internal class UnsolvableProblemException(string message = "Linear problem has no valid solutions")
    : Exception(message) { }
