using System;

namespace BackgroundResourceProcessing.Expr;

public class EvaluationException : Exception
{
    public EvaluationException(string message, Exception inner)
        : base(message, inner) { }

    public EvaluationException(string message)
        : base(message) { }
}

public class CompilationException(string message) : Exception(message) { }

public class NullValueException() : Exception("value was null but target type was not nullable") { }
