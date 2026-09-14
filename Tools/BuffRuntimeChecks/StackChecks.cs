namespace ParallelBonds.BuffChecks
{
    internal static class StackChecks
    {
        public static void Run(BuffCheckContext context)
        {
            var owner = context.CreateObject("owner");
            var target = context.CreateObject("target");
            var receiver = new RecordingBuffReceiver { Owner = owner };
            var hit = new StackEventContext(owner, target, 10f);
            var reward = context.DamageReward();
            var trigger = context.Trigger(reward, StackConsumeMode.ConsumeRequiredStacks);
            var recipe = context.Recipe(trigger);
            context.Check(recipe.isValid && reward.isValid, "valid recipes");
            var instance = new BuffInstance(recipe, receiver);
            var stack = instance.StackInstances[0];

            // unrelated, zero, invalid and self-inflicted damage must not add stacks
            instance.ProcessEvent(new StackEventContext(target, owner, 10f));
            instance.ProcessEvent(new StackEventContext(owner, target, 0f));
            instance.ProcessEvent(new StackEventContext(owner, target, float.NaN));
            instance.ProcessEvent(new StackEventContext(owner, owner, 10f));
            context.Check(stack.CurrentStacks == 0, "invalid and unrelated hits ignored");
            for (int i = 0; i < 4; i++) instance.ProcessEvent(hit);
            context.Check(stack.CurrentStacks == 4 && receiver.Grants == 0, "below threshold");
            instance.ProcessEvent(hit);
            context.Check(stack.CurrentStacks == 0 && receiver.Grants == 1, "threshold grants and consumes");

            // a failed grant keeps every stack, including hits above the threshold
            receiver.Accept = false;
            for (int i = 0; i < 6; i++) instance.ProcessEvent(hit);
            context.Check(stack.CurrentStacks == 6 && receiver.Grants == 1, "failure keeps all stacks");
            receiver.Accept = true;
            instance.ProcessEvent(hit);
            context.Check(stack.CurrentStacks == 2 && receiver.Grants == 2, "successful retry carries surplus");
            var second = new BuffInstance(recipe, receiver);
            context.Check(second.StackInstances[0].CurrentStacks == 0, "independent instance state");
            context.Check(trigger.RequiredStackCount == 5, "configuration was not used as counter");
            instance.Remove();
            stack.ProcessEvent(hit, receiver, recipe);
            context.Check(!stack.IsActive && stack.CurrentStacks == 2, "removed child cannot process events");

            CheckOtherConsumeModes(context, reward, receiver, hit);
        }

        private static void CheckOtherConsumeModes(BuffCheckContext context, BuffDefinition reward,
            RecordingBuffReceiver receiver, StackEventContext hit)
        {
            var once = new BuffInstance(
                context.Recipe(context.Trigger(reward, StackConsumeMode.TriggerOnce)), receiver);
            int before = receiver.Grants;
            for (int i = 0; i < 12; i++) once.ProcessEvent(hit);
            context.Check(receiver.Grants == before + 1 && once.StackInstances[0].HasTriggered, "trigger once");

            var clear = new BuffInstance(
                context.Recipe(context.Trigger(reward, StackConsumeMode.ConsumeAllStacks)), receiver);
            receiver.Accept = false;
            for (int i = 0; i < 6; i++) clear.ProcessEvent(hit);
            receiver.Accept = true;
            clear.ProcessEvent(hit);
            context.Check(clear.StackInstances[0].CurrentStacks == 0, "clear all surplus stacks");
        }
    }
}
