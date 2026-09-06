namespace ParallelBonds.BuffChecks
{
    internal static class LifecycleChecks
    {
        public static void Run(BuffCheckContext context)
        {
            var receiver = new RecordingBuffReceiver { Owner = context.CreateObject("owner") };
            var timed = new BuffInstance(context.DamageReward(), receiver);
            timed.Tick(4f);
            context.Check(timed.RemainingDuration == 6f, "lifetime ticks");
            timed.RefreshDuration();
            context.Check(timed.RemainingDuration == 10f, "refresh lifetime");
            timed.Tick(10f);
            context.Check(!timed.IsActive && timed.RemainingDuration == 0f, "expiry removes instance");

            // permanence is explicit rather than inferred from a zero duration
            var permanent = context.Recipe(new DamageBuffAtom());
            context.Set(permanent, "isPermanent", true);
            var permanentInstance = new BuffInstance(permanent, receiver);
            permanentInstance.Tick(100f);
            context.Check(permanentInstance.IsActive, "permanent buff does not expire");

            context.Check(!context.Recipe().isValid, "empty recipe rejected");
            var invalid = context.Recipe(new DamageBuffAtom());
            context.Set(invalid, "duration", 0f);
            context.Check(!invalid.isValid, "zero timed duration rejected");
            context.Check(!new StackToActivationBuffAtom().isValid, "missing reward rejected");
        }
    }
}
