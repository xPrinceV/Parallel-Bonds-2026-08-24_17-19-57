using System;
using ParallelBonds.BuffChecks;

// run the production calculator and shared checks without starting Unity
internal static class Program
{
    private static int Main()
    {
        try
        {
            Console.WriteLine(DamageFormulaChecks.Run() + " damage formula checks passed");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
