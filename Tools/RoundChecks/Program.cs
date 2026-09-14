using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Links both production controllers unchanged. Only external services/time are doubled.
internal static class Program
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static int assertions;

    private static void Main()
    {
        foreach (Action test in new Action[] { NaturalRun, EchoStart, ManualRequests, AutoCancellation,
            AcceptedFlipFinishes, RejectedCommit, PauseAndUpgrade, FinalWarningAndRetry,
            ManualDuringFinalResidence, DebugOverrideAndStageGuards, DeathGuard, LongFrame,
            ConfigurableInterval, ManualCommitResetsDebt })
        {
            test();
            Console.WriteLine("PASS " + test.Method.Name);
        }
        Console.WriteLine("ROUND_CHECKS PASS: 14 scenarios, " + assertions + " assertions");
    }

    private static void NaturalRun()
    {
        var f = new Fixture();
        Check(f.Flow.SwitchInterval == 90 && f.Run.FinaleStartTime == 540, "scene/default 90s / nominal 9min");
        Counts(f, 0, 0);
        Near(f.Flow.RemainingUntilFinale, 540, "initial residence is pending, not already completed");
        for (int residence = 1; residence <= 5; residence++)
        {
            f.Advance(90);
            Counts(f, (residence + 1) / 2, residence / 2);
            Check(f.Manager.SuccessfulCommits == residence && f.Midpoints == residence, "one successful auto midpoint per residence");
            Near(f.Run.RemainingUntilFinale, 540 - residence * 90, "remaining active-time estimate");
        }
        f.Advance(30);
        Check(f.Run.ElapsedTime == 480 && !f.Run.IsFinaleStarted, "old 480s deadline cannot start finale");
        f.Advance(59.5f);
        Check(f.Flow.IsFinalResidence && !f.Flow.IsFlipping && f.Flow.WarningProgress > 0.8f, "final warning before half-flip edge");
        f.Advance(0.375f);
        Check(!f.Flow.IsFlipping && !f.Run.IsFinaleStarted && f.Flow.FlipProgress == 0, "hold warning past ordinary flip start");
        f.Step();
        Counts(f, 3, 3);
        Check(f.Run.ElapsedTime == 540 && f.Run.IsFinaleStarted && f.Manager.IsFinalFusion, "sixth full interval starts irreversible finale");
        Check(f.Manager.CurrentWorldId == WorldId.Echo && f.Manager.SuccessfulCommits == 5 && f.Midpoints == 5, "fusion enters from Echo without sixth normal flip");
        Check(!f.Flow.enabled && f.Flow.RemainingResidences == 0 && f.Run.RemainingUntilFinale == 0, "final status and disabled flow");
        Check(!f.Run.TryStartFinale() && !f.Run.TryEnterStage(0) && !f.Flow.RequestNextWorldSwitch(), "finale is one-shot and stage/switch guarded");
        f.Advance(1);
        Check(f.Manager.FusionAttempts == 1 && f.Run.RemainingBosses == 0, "no repeated finale or boss before completion");
        f.Manager.FusionTransition.Complete();
        f.Manager.IsFusionTransitioning = false;
        Call(f.Run, "Update");
        Check(f.Run.RemainingBosses == 0, "boss never spawns in completion frame");
        Time.timeScale = 0;
        f.Advance(1);
        Check(f.Run.RemainingBosses == 0 && !f.Run.IsDefeated, "paused completion retains pending boss");
        Time.timeScale = 1;
        f.Step();
        Check(f.Run.RemainingBosses == 1 && f.Run.IsBossPhase, "existing boss path resumes on eligible next frame");
        f.Manager.FusionTransition.Complete();
        f.Step();
        Check(f.Run.RemainingBosses == 1, "completion cannot duplicate boss");
        foreach (EnemyController boss in new List<EnemyController>(Get<HashSet<EnemyController>>(f.Run, "bosses"))) boss.Die();
        f.Step();
        Check(f.Run.IsCompleted && !f.Run.IsDefeated && Time.timeScale == 0, "existing boss death sequence completes run");
    }

    private static void EchoStart()
    {
        var f = new Fixture(WorldId.Echo);
        f.Advance(90);
        Counts(f, 0, 1);
        f.Advance(450);
        Counts(f, 3, 3);
        Check(f.Manager.CurrentWorldId == WorldId.Material && f.Manager.SuccessfulCommits == 5, "initial Echo residence counts symmetrically");
    }

    private static void ManualRequests()
    {
        var f = new Fixture();
        f.Advance(55);
        Check(!f.Flow.RequestSwitch(WorldId.Material) && !f.Flow.RequestSwitch((WorldId)99), "reject current/invalid debug world");
        Check(f.Flow.RequestNextWorldSwitch() && !f.Flow.RequestNextWorldSwitch(), "one pending manual request");
        Near(f.Flow.RemainingUntilFinale, 545, "manual pending estimate discards incomplete outgoing residence");
        f.Flow.AutomaticSwitchingEnabled = false;
        f.Advance(6);
        Counts(f, 0, 0);
        Check(f.Manager.SuccessfulCommits == 1 && !f.Flow.IsFlipping, "accepted request finishes with auto off without credit");
        Near(f.Flow.RemainingTime, 90, "off resets newly entered incomplete residence");
        f.Flow.AutomaticSwitchingEnabled = true;
        f.Advance(90);
        Counts(f, 0, 1);
    }

    private static void AutoCancellation()
    {
        var f = new Fixture();
        f.Advance(90);
        f.Advance(86);
        Check(f.Flow.WarningProgress > 0, "automatic warning active");
        f.Flow.AutomaticSwitchingEnabled = false;
        Near(f.Flow.RemainingTime, 90, "toggle off resets incomplete residence");
        Check(f.Flow.WarningProgress == 0, "toggle off clears warning");
        f.Advance(1000);
        Counts(f, 1, 0);
        Check(f.Manager.SuccessfulCommits == 1 && !f.Run.IsFinaleStarted, "auto off cannot schedule timer finale even after 540s");
        f.Flow.AutomaticSwitchingEnabled = true;
        f.Advance(89.875f);
        Counts(f, 1, 0);
        f.Step();
        Counts(f, 1, 1);
    }

    private static void AcceptedFlipFinishes()
    {
        var f = new Fixture();
        f.Advance(89.75f);
        Check(f.Flow.IsFlipping && f.Manager.SuccessfulCommits == 0, "automatic flip accepted before midpoint");
        f.Flow.AutomaticSwitchingEnabled = false;
        f.Advance(1);
        Counts(f, 1, 0);
        Check(f.Manager.SuccessfulCommits == 1 && !f.Flow.IsFlipping, "accepted automatic flip finishes and credits once after off");
        f.Advance(200);
        Counts(f, 1, 0);
        Near(f.Flow.RemainingTime, 90, "next incomplete residence stays reset");
    }

    private static void RejectedCommit()
    {
        var f = new Fixture();
        f.Manager.RejectCommit = true;
        f.Advance(90);
        Counts(f, 0, 0);
        Check(f.Midpoints == 0 && !f.Flow.IsFlipping, "failed commit gives no credit or midpoint event");
        Near(f.Flow.RemainingTime, 90, "failed commit restarts full residence");
        f.Manager.RejectCommit = false;
        f.Advance(90);
        Counts(f, 1, 0);
        Check(f.Midpoints == 1, "subsequent successful automatic commit counts");
    }

    private static void PauseAndUpgrade()
    {
        var f = new Fixture();
        f.Advance(85.5f);
        float remaining = f.Flow.RemainingTime, warning = f.Flow.WarningProgress;
        Time.timeScale = 0;
        f.Advance(600);
        Near(f.Flow.RemainingTime, remaining, "pause freezes residence");
        Check(f.Flow.WarningProgress == warning && !f.Flow.RequestNextWorldSwitch() && !f.Run.TryStartFinale(), "pause freezes warning and rejects requests");
        Time.timeScale = 1;
        UIController.instance = new UIController { levelUpPanel = new GameObject() };
        f.Advance(600);
        Near(f.Flow.RemainingTime, remaining, "upgrade freezes with nonzero timeScale");
        Check(!f.Run.TryStartFinale(), "upgrade retains finale guard");
        UIController.instance = null;
        f.Advance(4.25f);
        float flip = f.Flow.FlipProgress;
        Time.timeScale = 0;
        f.Advance(2);
        Check(f.Flow.FlipProgress == flip && f.Manager.SuccessfulCommits == 0, "pause freezes accepted flip before commit");
        Time.timeScale = 1;
        f.Advance(0.25f);
        Counts(f, 1, 0);
    }

    private static void FinalWarningAndRetry()
    {
        var f = new Fixture();
        f.Advance(539.75f);
        Check(!f.Flow.IsFlipping, "final interval never accepts ordinary flip");
        Time.timeScale = 0;
        f.Advance(1);
        Counts(f, 3, 2);
        Check(!f.Run.IsFinaleStarted, "paused final boundary cannot fuse");
        Time.timeScale = 1;
        f.Manager.RejectFusion = true;
        f.Advance(0.25f);
        Counts(f, 3, 2);
        Check(!f.Run.IsFinaleStarted && !f.Flow.IsFlipping && f.Flow.WarningProgress == 1, "rejected fusion holds warning without credit");
        Near(f.Flow.RemainingTime, 0, "failed hook cancellation does not restart final timer");
        f.Manager.RejectFusion = false;
        f.Step();
        Counts(f, 3, 3);
        Check(f.Manager.FusionAttempts == 2 && f.Manager.SuccessfulCommits == 5, "retry accepts finale once without extra world flip");

        f = new Fixture();
        f.Advance(539.875f);
        f.Flow.AutomaticSwitchingEnabled = false;
        f.Advance(1000);
        Counts(f, 3, 2);
        Check(!f.Run.IsFinaleStarted, "auto off cancels even last incomplete residence");
        f.Flow.AutomaticSwitchingEnabled = true;
        f.Advance(89.875f);
        Check(!f.Run.IsFinaleStarted, "final residence must be repeated in full after cancellation");
        f.Step();
        Counts(f, 3, 3);
    }

    private static void ManualDuringFinalResidence()
    {
        var f = new Fixture();
        f.Advance(539.75f);
        Check(f.Flow.RequestNextWorldSwitch(), "manual override allowed during held final warning");
        f.Advance(0.25f);
        Counts(f, 3, 2);
        Check(!f.Run.IsFinaleStarted && f.Manager.CurrentWorldId == WorldId.Material, "manual final-world departure is not completed residence");
        Near(f.Flow.RemainingUntilFinale, 180, "unbalanced counts include extra capped-world residence");
        f.Advance(90);
        Counts(f, 3, 2);
        Check(!f.Run.IsFinaleStarted, "repeated Material cannot substitute for missing Echo");
        f.Advance(90);
        Counts(f, 3, 3);
        Check(f.Run.IsFinaleStarted, "missing Echo completion eventually fuses");
    }

    private static void DebugOverrideAndStageGuards()
    {
        var f = new Fixture();
        f.Flow.AutomaticSwitchingEnabled = false;
        Check(f.Run.TryEnterStage(1) && f.Run.TryEnterStage(2), "existing shared stage entry remains available");
        Counts(f, 0, 0);
        Check(!f.Run.TryEnterStage(2) && !f.Run.TryEnterStage(-1) && !f.Run.TryEnterStage(4), "same/invalid stage guards retained");
        Check(f.Run.TryEnterStage(3) && f.Run.IsFinaleStarted, "explicit debug finale bypasses rounds and auto off");
        Counts(f, 0, 0);
        Check(!f.Run.TryEnterStage(0) && !f.Run.TryStartFinale(), "debug final fusion also locks stages");
        f = new Fixture(sharedWaves: false);
        Check(!f.Run.TryEnterStage(1), "legacy waves cannot enter shared stages");
        f.Advance(540);
        Check(f.Run.IsFinaleStarted, "residence scheduling also works with legacy waves");
    }

    private static void DeathGuard()
    {
        var f = new Fixture();
        f.Advance(539.875f);
        f.Manager.CurrentWorld.Player.GetComponent<PlayerHealth>().IsDead = true;
        f.Step();
        Counts(f, 3, 2);
        Check(f.Run.IsDefeated && !f.Run.IsFinaleStarted && !f.Run.TryStartFinale(), "death at final edge defeats instead of fusing");
        Check(!f.Flow.RequestNextWorldSwitch(), "dead run cannot accept switch");
    }

    private static void LongFrame()
    {
        var f = new Fixture();
        f.Step(95);
        Counts(f, 1, 0);
        Check(f.Flow.FlipProgress == 0.5f && f.Midpoints == 1, "long frame still clamps once at midpoint");
        f = new Fixture();
        f.Advance(539);
        f.Step(2);
        Counts(f, 3, 3);
        Check(f.Manager.SuccessfulCommits == 5 && f.Manager.FusionAttempts == 1, "long final frame fuses without ordinary flip");
    }

    private static void ConfigurableInterval()
    {
        var field = typeof(StateSwitchController).GetField("timer", Members);
        Check(field != null && !field.IsLiteral && field.IsDefined(typeof(SerializeField), false)
            && field.IsDefined(typeof(MinAttribute), false), "timer remains serialized, bounded and reflection-writable");
        Near(new StateSwitchController().SwitchInterval, 90, "new component defaults to 90 seconds");
        var f = new Fixture(interval: 30);
        Near(f.Flow.SwitchInterval, 30, "configured legacy/fixture interval is respected, not forcibly migrated");
        Near(f.Flow.RemainingUntilFinale, 180, "remaining estimate uses configured interval");
        f.Advance(30);
        Counts(f, 1, 0);
        f.Advance(150);
        Counts(f, 3, 3);
        Check(f.Run.IsFinaleStarted && f.Manager.SuccessfulCommits == 5, "configured interval still requires three residences per world");
    }

    private static void ManualCommitResetsDebt()
    {
        foreach (float longFrame in new[] { 5.125f, 100f })
        {
            var f = new Fixture();
            f.Advance(55);
            Check(f.Flow.RequestNextWorldSwitch(), "manual request before overshooting frame");
            if (longFrame == 100f)
            {
                f.Advance(4.75f);
                Check(f.Flow.IsFlipping, "manual flip already accepted before long frame");
            }
            f.Step(longFrame);
            Counts(f, 0, 0);
            Check(f.Manager.CurrentWorldId == WorldId.Echo && f.Midpoints == 1, "manual midpoint commits without credit");
            Near(f.Flow.RemainingTime, 90, "manual commit discards all prior frame debt");
            Near(f.Flow.RemainingUntilFinale, 540, "manual entry starts six full pending residences");
            f.Advance(89.875f);
            Counts(f, 0, 0);
            Check(f.Manager.SuccessfulCommits == 1, "no automatic credit before full residence after manual entry");
            f.Step();
            Counts(f, 0, 1);
            Check(f.Manager.SuccessfulCommits == 2, "automatic credit at full 90 seconds after manual entry");
        }
    }

    private sealed class Fixture
    {
        public readonly WorldManager Manager = new WorldManager();
        public readonly StateSwitchController Flow = new StateSwitchController();
        public readonly RunStageController Run = new RunStageController();
        public int Midpoints;
        public Fixture(WorldId initial = WorldId.Material, bool sharedWaves = true, float interval = 90f)
        {
            Time.deltaTime = 0.125f; Time.timeScale = 1; Time.frameCount = 1; UIController.instance = null;
            Manager.CurrentWorldId = initial;
            Manager.Worlds = new[] { new World { Manager = Manager }, new World { Manager = Manager } };
            foreach (World world in Manager.Worlds) world.Player.Components.Add(typeof(PlayerHealth), new PlayerHealth());
            Set(Run, "worldManager", Manager);
            Set(Run, "spawners", new[] { new EnemySpawner { Owner = Manager.Worlds[0] }, new EnemySpawner { Owner = Manager.Worlds[1] } });
            Set(Run, "useSharedWaves", sharedWaves);
            var prefab = new TitanEnemyController();
            prefab.gameObject.scene = new UnityEngine.SceneManagement.Scene(0);
            Set(Run, "bossPrefab", prefab);
            Set(Flow, "worldManager", Manager);
            Set(Flow, "timer", interval);
            Call(Run, "Awake"); Call(Run, "Start"); Call(Flow, "Start");
            Flow.TransitionMidpoint += () => Midpoints++;
        }
        public void Step(float seconds = 0.125f)
        {
            Time.deltaTime = seconds * Time.timeScale;
            Time.frameCount++;
            if (Run.isActiveAndEnabled) Call(Run, "Update");
            if (Flow.isActiveAndEnabled) Call(Flow, "Update");
        }
        public void Advance(float seconds)
        {
            int frames = (int)(seconds / 0.125f);
            for (int i = 0; i < frames; i++) Step();
        }
    }
    private static void Counts(Fixture f, int material, int echo)
    {
        Check(f.Flow.CompletedMaterialResidences == material && f.Flow.CompletedEchoResidences == echo,
            "counts expected M=" + material + ",E=" + echo + " got " + f.Flow.CompletedMaterialResidences + "," + f.Flow.CompletedEchoResidences);
        Check(f.Run.CompletedMaterialResidences == material && f.Run.CompletedEchoResidences == echo, "run exposes same counts");
    }
    private static void Near(float actual, float expected, string message) => Check(Math.Abs(actual - expected) < 0.001f, message + ": " + actual);
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Members).Invoke(target, null);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Members).GetValue(target);
}
