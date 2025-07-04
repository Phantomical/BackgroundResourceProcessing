using BackgroundResourceProcessing.Tracing;

namespace BackgroundResourceProcessing.Solver;

internal static class LinAlg
{
    public static void GaussianEliminationOrdered(Matrix matrix)
    {
        using var span = new TraceSpan("LinAlg.GaussianEliminationOrdered");

        int scol = 0;
        for (int row = 0; row < matrix.Height; ++row)
        {
            int col = -1;
            for (int x = scol; x < matrix.Width; ++x)
            {
                if (matrix[x, row] != 0.0)
                {
                    col = x;
                    break;
                }
            }

            if (col == -1)
                continue;
            scol = col + 1;

            for (int x = col + 1; x < matrix.Width; ++x)
                matrix[x, row] /= matrix[col, row];
            matrix[col, row] = 1.0;

            for (int y = 0; y < matrix.Height; ++y)
            {
                if (y == row)
                    continue;
                if (matrix[col, y] == 0.0)
                    continue;

                for (int x = col + 1; x < matrix.Width; ++x)
                    matrix[x, y] -= matrix[col, y] * matrix[x, row];
                matrix[col, y] = 0.0;
            }
        }
    }

    public static int FindFirstNonZeroInColumn(Matrix matrix, int column)
    {
        for (int y = 0; y < matrix.Height; ++y)
        {
            if (matrix[column, y] != 0)
                return y;
        }

        return -1;
    }

    public static int FindFirstNonZeroInRow(Matrix matrix, int row)
    {
        for (int x = 0; x < matrix.Width; ++x)
        {
            if (matrix[x, row] != 0)
                return x;
        }

        return -1;
    }
}
