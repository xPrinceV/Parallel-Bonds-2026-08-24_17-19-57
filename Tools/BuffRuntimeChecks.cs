using System;
using ParallelBonds.BuffChecks;

// compiled with the buff sources by the editor verification command, not shipped in builds
public static class BuffRuntimeChecks
{
    public static string Run()
    {
        int checks = RunStackChecks() + RunLifecycleChecks() + RunControllerChecks();
        return checks + " checks passed";
    }

    // allow each group to run independently with fresh state and guaranteed cleanup
    public static int RunStackChecks()
    {
        return RunGroup("Stack", StackChecks.Run);
    }

    public static int RunLifecycleChecks()
    {
        return RunGroup("Lifecycle", LifecycleChecks.Run);
    }

    public static int RunControllerChecks()
    {
        return RunGroup("Controller", ControllerChecks.Run);
    }

    private static int RunGroup(string name, Action<BuffCheckContext> run)
    {
        using (var context = new BuffCheckContext(name))
        {
            run(context);
            return context.CheckCount;
        }
    }
}
