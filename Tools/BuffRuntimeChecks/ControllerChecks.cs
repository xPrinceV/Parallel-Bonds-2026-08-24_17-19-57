namespace ParallelBonds.BuffChecks
{
    internal static class ControllerChecks
    {
        public static void Run(BuffCheckContext context)
        {
            // exercise the real holder, not only the stack state machine
            var owner = context.CreateObject("owner");
            var target = context.CreateObject("target");
            var holder = owner.AddComponent<BuffController>();
            var reward = context.DamageReward();
            var recipe = context.Recipe(context.Trigger(reward, StackConsumeMode.ConsumeRequiredStacks));
            context.Check(holder.TryAddBuff(recipe), "holder grants recipe");
            var original = holder.FindBuff(recipe);
            holder.Tick(3f);
            context.Check(holder.TryAddBuff(recipe) && ReferenceEquals(original, holder.FindBuff(recipe))
                && original.RemainingDuration == 10f, "regrant refreshes without duplication");
            for (int i = 0; i < 5; i++) holder.ReportHit(target, 10f);
            context.Check(holder.FindBuff(reward) != null && holder.GetModifiers().Length == 3,
                "hit event grants reward modifiers");
            context.Check(DamageCalculator.CalculateDamage(100f, holder.GetModifiers()) == 150f,
                "reward reaches calculator");
            context.Check(holder.RemoveBuff(reward) && holder.GetModifiers().Length == 0,
                "remove stops modifier contribution");

            // a newly granted trigger cannot consume the event that created it
            var followup = context.Recipe(context.Trigger(reward, StackConsumeMode.TriggerOnce));
            var first = context.Trigger(followup, StackConsumeMode.TriggerOnce);
            context.Set(first, "stackCount", 1);
            var firstRecipe = context.Recipe(first);
            holder.RemoveBuff(recipe);
            holder.TryAddBuff(firstRecipe);
            holder.ReportHit(target, 10f);
            var followupInstance = holder.FindBuff(followup);
            context.Check(followupInstance != null && followupInstance.StackInstances[0].CurrentStacks == 0,
                "new buff excluded from current event");
            holder.Tick(10f);
            context.Check(holder.FindBuff(firstRecipe) == null && holder.FindBuff(followup) == null,
                "holder expiry cleanup");
            holder.TryAddBuff(reward);
            holder.enabled = false;
            // edit-mode checks invoke the lifecycle hook explicitly; Play Mode owns dispatch
            context.Invoke(holder, "OnDisable");
            context.Check(holder.FindBuff(reward) == null && holder.GetModifiers().Length == 0,
                "disable ends held buffs");
        }
    }
}
