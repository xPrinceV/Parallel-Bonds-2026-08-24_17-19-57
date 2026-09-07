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
            context.Check(holder.CalculateWeaponDamage(20f) == 20f, "empty holder preserves weapon damage");
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
            context.Check(holder.CalculateWeaponDamage(100f) == 150f,
                "reward reaches calculator");
            context.Check(holder.RemoveBuff(reward) && holder.GetModifiers().Length == 0,
                "remove stops modifier contribution");
            context.Check(holder.CalculateWeaponDamage(100f) == 100f, "removed reward no longer changes damage");

            // independent recipes reach the same formula through the holder entry point
            var firstDamage = new DamageBuffAtom();
            context.Set(firstDamage, "damagePrefix", 5f);
            context.Set(firstDamage, "damageMultiplier", 1.2f);
            context.Set(firstDamage, "damagePostfix", 4f);
            var secondDamage = new DamageBuffAtom();
            context.Set(secondDamage, "damagePrefix", 3f);
            context.Set(secondDamage, "damageMultiplier", 1.5f);
            var firstDamageRecipe = context.Recipe(firstDamage);
            var secondDamageRecipe = context.Recipe(secondDamage);
            context.Check(holder.TryAddBuff(firstDamageRecipe) && holder.TryAddBuff(secondDamageRecipe),
                "holder accepts independent damage recipes");
            context.Check(System.Math.Abs(holder.CalculateWeaponDamage(20f) - 54.4f) < 0.0001f,
                "holder preserves prefix multiplier postfix formula");
            holder.RemoveBuff(firstDamageRecipe);
            holder.RemoveBuff(secondDamageRecipe);

            bool invalidDamageRejected = false;
            try
            {
                holder.CalculateWeaponDamage(float.NaN);
            }
            catch (System.ArgumentOutOfRangeException)
            {
                invalidDamageRejected = true;
            }
            context.Check(invalidDamageRejected, "holder preserves calculator input validation");

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
            context.Check(holder.CalculateWeaponDamage(20f) == 20f, "disabled holder contributes no damage buffs");
        }
    }
}
