using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

internal static class Program
{
    private static int assertions;

    private static int Main()
    {
        Action[] tests = { CumulativeMerge, EntryCurvePolicy, SuppliedTableCap, MergeIdempotency,
            RollbackRetainsEarned, LongExcessAndIntOverflow, GeneratedCurveOverflow,
            MultipleThresholdsThroughButtons, EarningsWhilePending, FusionSecondarySelection,
            StaleChoicesAndUnscaledDebounce, ForcedFusionEndRebuildsChoices,
            DeathCancellationDoesNotResume, SelectionAuthorization, NonpositiveAndCap,
            MissingSelectionUi };
        int failures = 0;
        foreach (Action test in tests)
        {
            int before = assertions;
            try
            {
                test();
                Console.WriteLine("PASS " + test.Method.Name + " (" + (assertions - before) + " assertions)");
            }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine("FAIL " + test.Method.Name + ": " + error);
            }
        }
        Console.WriteLine("EXPERIENCE_CHECKS " + (failures == 0 ? "PASS" : "FAIL") + ": "
            + tests.Length + " scenarios, " + assertions + " assertions, " + failures + " failures");
        return failures == 0 ? 0 : 1;
    }

    private static void CumulativeMerge()
    {
        var f = new Fixture();
        var entry = f.Hero(3, 4);
        var other = f.Hero(2, 2);
        f.Activate(entry);
        State(entry, 17, 3, 4, "entry cumulative XP");
        State(other, 7, 2, 2, "secondary cumulative XP");
        entry.Exp.BeginFusionExperience(other.Exp);
        State(entry, 24, 4, 0, "17 + 7 uses completed thresholds, not just remainders");
        State(other, 7, 2, 2, "borrowed source unchanged");
        Check(!f.Ui.levelUpPanel.activeSelf && Time.timeScale == 1 && entry.Exp.SelectionVersion == 0,
            "merge grants no retroactive choice and does not pause");
        Check(f.Ui.Experience == 0 && f.Ui.Level == 4 && f.Ui.Required == 15, "merged presentation");
        entry.Exp.EndFusionExperience();
        State(entry, 17, 3, 4, "merge rollback");
    }

    private static void EntryCurvePolicy()
    {
        var f = new Fixture();
        var expensive = f.Hero(3, 4);
        var cheap = f.Hero(3, 2, 5, new List<int> { 0, 2, 3, 4, 5 });
        f.Activate(expensive);
        expensive.Exp.BeginFusionExperience(cheap.Exp);
        State(expensive, 24, 4, 0, "default entry curve");
        State(cheap, 7, 3, 2, "source uses its own curve when contributing");
        expensive.Exp.EndFusionExperience();
        f.Activate(cheap);
        cheap.Exp.BeginFusionExperience(expensive.Exp);
        State(cheap, 24, 4, 15, "reverse entry uses cheap curve and its cap");
        Check(f.Ui.Required == 5 && cheap.Exp.SelectionVersion == 0, "entry presentation and no merge choice");
        cheap.Exp.EndFusionExperience();
        State(cheap, 7, 3, 2, "rollback uses the entry curve too");
    }

    private static void SuppliedTableCap()
    {
        var f = new Fixture();
        var table = new List<int> { 0, 5, 8, 11, 15, 19 };
        var entry = f.Hero(1, 0, 3, table);
        var other = f.Hero(3, 4);
        f.Activate(entry);
        Check(!ReferenceEquals(table, entry.Exp.expLevels) && table.Count == 6, "controller owns supplied table");
        entry.Exp.GetExp(24);
        State(entry, 24, 2, 19, "levelCount 3 caps at index 2 despite six supplied entries");
        f.Ui.levelUpButtons[0].SelectUpgrade();
        Check(!f.Ui.levelUpPanel.activeSelf && entry.Weapon.stats.damage == 0.25f, "only one earned choice at cap");
        entry.Exp.BeginFusionExperience(other.Exp);
        State(entry, 41, 2, 36, "SetTotalExperience also respects levelCount instead of table length");
        entry.Exp.EndFusionExperience();
        State(entry, 24, 2, 19, "cap rollback");
        var clamped = f.Hero(5, 1, 3, table);
        State(clamped, 6, 2, 1, "initial level is clamped to the configured maximum index");
    }

    private static void MergeIdempotency()
    {
        var f = new Fixture();
        var entry = f.Hero(3, 4);
        var other = f.Hero(2, 2);
        var third = f.Hero(2, 6);
        f.Activate(entry);
        entry.Exp.BeginFusionExperience(null);
        entry.Exp.BeginFusionExperience(entry.Exp);
        entry.Exp.EndFusionExperience();
        State(entry, 17, 3, 4, "null/self/unmatched end are inert");
        entry.Exp.BeginFusionExperience(other.Exp);
        entry.Exp.BeginFusionExperience(other.Exp);
        entry.Exp.BeginFusionExperience(third.Exp);
        State(entry, 24, 4, 0, "reentry cannot borrow either source twice");
        entry.Exp.EndFusionExperience();
        entry.Exp.EndFusionExperience();
        State(entry, 17, 3, 4, "end is idempotent");
        State(other, 7, 2, 2, "original source retained");
        State(third, 11, 2, 6, "rejected source retained");
    }

    private static void RollbackRetainsEarned()
    {
        var f = new Fixture(false);
        var entry = f.Hero(3, 4);
        var other = f.Hero(2, 2);
        f.Activate(entry);
        entry.Exp.BeginFusionExperience(other.Exp);
        entry.Exp.GetExp(15);
        State(entry, 39, 5, 0, "earn while fused");
        entry.Exp.EndFusionExperience();
        State(entry, 32, 4, 8, "only borrowed seven returned, earned fifteen retained");
        for (int i = 0; i < 3; i++)
        {
            entry.Exp.BeginFusionExperience(other.Exp);
            State(entry, 39, 5, 0, "repeat fusion does not duplicate earnings");
            entry.Exp.EndFusionExperience();
            State(entry, 32, 4, 8, "repeat rollback conserves own earnings");
        }
        State(other, 7, 2, 2, "secondary never receives duplicated earnings");
    }

    private static void LongExcessAndIntOverflow()
    {
        var f = new Fixture(false);
        var table = new List<int> { 0, int.MaxValue, int.MaxValue, int.MaxValue };
        var entry = f.Hero(3, int.MaxValue, 4, table);
        var other = f.Hero(3, int.MaxValue, 4, table);
        f.Activate(entry);
        State(entry, 6442450941L, 3, int.MaxValue, "completed costs sum in long");
        entry.Exp.BeginFusionExperience(other.Exp);
        State(entry, 12884901882L, 3, int.MaxValue, "fusion retains excess beyond the visible int remainder");
        entry.Exp.GetExp(int.MaxValue);
        entry.Exp.GetExp(0);
        entry.Exp.GetExp(int.MinValue);
        State(entry, 15032385529L, 3, int.MaxValue, "capped gain cannot wrap int or erase excess");
        entry.Exp.EndFusionExperience();
        State(entry, 8589934588L, 3, int.MaxValue, "large borrowed pool returned without losing earned excess");
        entry.Exp.BeginFusionExperience(other.Exp);
        State(entry, 15032385529L, 3, int.MaxValue, "large repeat fusion conserved");
        entry.Exp.EndFusionExperience();
        State(entry, 8589934588L, 3, int.MaxValue, "large repeat rollback conserved");
        State(other, 6442450941L, 3, int.MaxValue, "large source unchanged");
        Check(entry.Exp.SelectionVersion == 0, "cap does not create repeated selections");
    }

    private static void GeneratedCurveOverflow()
    {
        var f = new Fixture(false);
        var table = new List<int> { 0, int.MaxValue };
        var entry = f.Hero(1, 0, 5, table);
        f.Activate(entry);
        Check(table.Count == 2 && entry.Exp.expLevels.Count == 5, "generation does not mutate shared input");
        Check(entry.Exp.expLevels[2] == int.MaxValue && entry.Exp.expLevels[3] == int.MaxValue
            && entry.Exp.expLevels[4] == int.MaxValue, "generated thresholds saturate instead of overflowing");
        entry.Exp.GetExp(int.MaxValue);
        State(entry, 2147483647L, 2, 0, "first large threshold");
        entry.Exp.GetExp(int.MaxValue);
        State(entry, 4294967294L, 3, 0, "second large threshold");
        entry.Exp.GetExp(int.MaxValue);
        State(entry, 6442450941L, 4, 0, "generated cap reached");
        entry.Exp.GetExp(int.MaxValue);
        State(entry, 8589934588L, 4, int.MaxValue, "post-cap excess retained");
    }

    private static void MultipleThresholdsThroughButtons()
    {
        var f = new Fixture();
        var entry = f.Hero();
        f.Activate(entry);
        var buttons = (LevelUpSelectionButton[])f.Ui.levelUpButtons.Clone();
        entry.Exp.GetExp(24);
        State(entry, 24, 2, 19, "first threshold pauses queue");
        Check(f.Ui.levelUpPanel.activeSelf && Time.timeScale == 0, "selection pauses scaled time");
        Check(buttons[0].weaponIcon.sprite == entry.Weapon.icon && buttons[0].nameLevelText.text == "weapon"
            && !string.IsNullOrEmpty(buttons[0].upgradeDescText.text), "actual button display populated");
        buttons[0].SelectUpgrade();
        State(entry, 24, 3, 11, "first actual selection releases second threshold");
        Check(entry.Weapon.stats.damage == 0.25f && Time.timeScale == 0, "one applied upgrade, still paused");
        Time.unscaledTime = 0.2f;
        buttons[1].SelectUpgrade();
        State(entry, 24, 4, 0, "second actual selection releases third threshold");
        Check(entry.Weapon.stats.damage == 0.5f && f.Ui.levelUpPanel.activeSelf, "third earned choice remains open");
        Time.unscaledTime = 0.4f;
        buttons[2].SelectUpgrade();
        Check(entry.Weapon.stats.damage == 0.75f && !f.Ui.levelUpPanel.activeSelf && Time.timeScale == 1,
            "all three upgrades applied exactly once, resume only after queue drains");
        Check(ReferenceEquals(buttons[0], f.Ui.levelUpButtons[0]) && ReferenceEquals(buttons[1], f.Ui.levelUpButtons[1])
            && ReferenceEquals(buttons[2], f.Ui.levelUpButtons[2]) && UnityEngine.Object.InstantiateCalls == 0,
            "show reuses real buttons and weapon references without instantiation");
    }

    private static void EarningsWhilePending()
    {
        var f = new Fixture();
        var entry = f.Hero();
        f.Activate(entry);
        entry.Exp.GetExp(5);
        int version = entry.Exp.SelectionVersion;
        entry.Exp.GetExp(19);
        State(entry, 24, 2, 19, "XP can accumulate while a choice is pending");
        Check(entry.Exp.SelectionVersion == version, "new XP does not overwrite pending choices");
        for (int i = 0; i < 3; i++)
        {
            Time.unscaledTime = i;
            f.Ui.levelUpButtons[0].SelectUpgrade();
        }
        State(entry, 24, 4, 0, "queued earnings drain through actual selections");
        Check(entry.Weapon.stats.damage == 0.75f && Time.timeScale == 1, "no pending earnings lost");
    }

    private static void FusionSecondarySelection()
    {
        var f = new Fixture();
        var entry = f.Hero();
        var other = f.Hero(name: "secondary");
        f.Fuse(entry, other);
        other.Exp.enabled = false;
        other.Exp.BindAsCurrent();
        Check(ExperienceLevelController.instance == entry.Exp, "secondary cannot steal current binding");
        UnityEngine.Random.Indices.Enqueue(1);
        other.Exp.GetExp(24);
        State(entry, 24, 2, 19, "secondary pickup forwarded to fusion interaction hero");
        State(other, 0, 1, 0, "forwarded XP is not credited twice");
        Check(!entry.Player.assignedWeapons.Contains(other.Weapon) && entry.Player.HasEquippedWeapon(other.Weapon)
            && other.Weapon.SourceOwner == other.Player, "source-owned secondary authorized by fused inventory");
        Check(f.Ui.levelUpButtons[0].weaponIcon.sprite == other.Weapon.icon, "real show selected secondary weapon");
        f.Ui.levelUpButtons[0].SelectUpgrade();
        Check(other.Weapon.stats.damage == 0.25f && entry.Weapon.stats.damage == 0, "selection mutates source weapon, not clone");
        Time.unscaledTime = 0.2f;
        f.Ui.levelUpButtons[0].SelectUpgrade();
        Time.unscaledTime = 0.4f;
        f.Ui.levelUpButtons[0].SelectUpgrade();
        Check(entry.Weapon.stats.damage == 0.5f && other.Weapon.stats.damage == 0.25f && Time.timeScale == 1,
            "fusion multi-threshold queue applies three choices across source inventories");
        Check(other.Weapon.SourceOwner == other.Player && UnityEngine.Object.InstantiateCalls == 0, "no ownership transfer or clone");
    }

    private static void StaleChoicesAndUnscaledDebounce()
    {
        var f = new Fixture();
        var entry = f.Hero();
        f.Activate(entry);
        entry.Exp.GetExp(24);
        // Detached real button preserves an old sibling event; visible buttons are refreshed in place.
        var stale = f.Button();
        stale.UpdateButtonDisplay(entry.Weapon);
        int version = entry.Exp.SelectionVersion;
        Check(!entry.Exp.TryConsumeUpgradeSelection(version - 1), "wrong version rejected");
        entry.Exp.CompleteUpgradeSelection();
        Check(entry.Exp.SelectionVersion == version && Time.timeScale == 0, "completion without acceptance is inert");
        f.Ui.levelUpButtons[0].SelectUpgrade();
        int nextVersion = entry.Exp.SelectionVersion;
        Check(nextVersion > version, "accept and next panel invalidate previous version");
        f.Ui.levelUpButtons[0].SelectUpgrade();
        f.Ui.levelUpButtons[1].SelectUpgrade();
        stale.SelectUpgrade();
        Check(entry.Weapon.stats.damage == 0.25f && entry.Exp.SelectionVersion == nextVersion, "immediate double click and siblings ignored");
        Time.unscaledTime = 0.199f;
        f.Ui.levelUpButtons[1].SelectUpgrade();
        Check(entry.Weapon.stats.damage == 0.25f && Time.timeScale == 0, "guard lasts until 0.2 unscaled seconds while paused");
        Time.unscaledTime = 0.2f;
        stale.SelectUpgrade();
        Check(!entry.Exp.TryConsumeUpgradeSelection(version) && entry.Weapon.stats.damage == 0.25f,
            "stale version remains invalid after cooldown, independent of debounce");
        f.Ui.levelUpButtons[1].SelectUpgrade();
        Check(entry.Weapon.stats.damage == 0.5f && entry.Exp.currentLevels == 4, "fresh choice accepted exactly at 0.2 boundary");
        Time.unscaledTime = 0.399f;
        f.Ui.levelUpButtons[2].SelectUpgrade();
        Check(entry.Weapon.stats.damage == 0.5f, "second accepted choice restarts debounce");
        Time.unscaledTime = 0.4f;
        f.Ui.levelUpButtons[2].SelectUpgrade();
        int finalVersion = entry.Exp.SelectionVersion;
        Time.unscaledTime = 1;
        foreach (var button in f.Ui.levelUpButtons) button.SelectUpgrade();
        entry.Exp.CompleteUpgradeSelection();
        Check(entry.Weapon.stats.damage == 0.75f && entry.Exp.SelectionVersion == finalVersion
            && Time.timeScale == 1 && !f.Ui.levelUpPanel.activeSelf, "closed-panel clicks and repeated completion are inert");
    }

    private static void ForcedFusionEndRebuildsChoices()
    {
        var f = new Fixture();
        var entry = f.Hero();
        var other = f.Hero(2, 2, name: "secondary");
        f.Fuse(entry, other);
        UnityEngine.Random.Indices.Enqueue(1);
        entry.Exp.GetExp(6);
        State(entry, 13, 3, 0, "borrowed seven plus earned six unlock one pending choice");
        var stale = f.Button();
        stale.UpdateButtonDisplay(other.Weapon);
        int version = entry.Exp.SelectionVersion;
        // Change inventory before forced exit: rebuilding must use this, not cached fusion choices.
        var replacement = f.AddWeapon(entry, "replacement");
        entry.Player.assignedWeapons.Remove(entry.Weapon);
        f.EndFusion(entry, false);
        State(entry, 6, 2, 1, "rollback returns borrowed XP but retains earned choice's XP");
        Check(entry.Exp.SelectionVersion > version && f.Ui.levelUpPanel.activeSelf && Time.timeScale == 0,
            "forced exit rebuilds pending panel without resuming");
        foreach (var button in f.Ui.levelUpButtons)
            Check(button.weaponIcon.sprite == replacement.icon && button.nameLevelText.text == "replacement", "rebuilt from current local inventory");
        Check(!entry.Player.HasEquippedWeapon(other.Weapon), "secondary no longer authorized outside fusion");
        stale.SelectUpgrade();
        Check(other.Weapon.stats.damage == 0 && f.Ui.levelUpPanel.activeSelf, "old source choice cannot consume replacement panel");
        f.Ui.levelUpButtons[0].SelectUpgrade();
        Check(replacement.stats.damage == 0.25f && entry.Weapon.stats.damage == 0 && Time.timeScale == 1
            && !f.Ui.levelUpPanel.activeSelf, "earned choice survives forced exit and applies exactly once");
        f.Fuse(entry, other);
        State(entry, 13, 3, 0, "repeat fusion restores pool without another choice");
        Check(!f.Ui.levelUpPanel.activeSelf, "no duplicate earned choice on repeat fusion");
        f.EndFusion(entry, false);
        State(entry, 6, 2, 1, "repeat exit retains earnings");
        Check(replacement.stats.damage == 0.25f, "repeat exit does not duplicate upgrade");
    }

    private static void DeathCancellationDoesNotResume()
    {
        // Also exercise cancellation between acceptance and completion via the exposed protocol.
        foreach (bool accepted in new[] { false, true })
        {
            var f = new Fixture();
            var entry = f.Hero();
            var other = f.Hero(2, 2);
            f.Fuse(entry, other);
            entry.Exp.GetExp(6);
            if (accepted) Check(entry.Exp.TryConsumeUpgradeSelection(entry.Exp.SelectionVersion), "accept before cancellation");
            int version = entry.Exp.SelectionVersion;
            f.EndFusion(entry, true);
            Check(!f.Ui.levelUpPanel.activeSelf && Time.timeScale == 0 && entry.Exp.SelectionVersion > version,
                "death cancels pending/accepted selection without resuming time");
            Time.unscaledTime = 1;
            foreach (var button in f.Ui.levelUpButtons) button.SelectUpgrade();
            entry.Exp.CompleteUpgradeSelection();
            entry.Exp.ReconcileUpgradeSelection(true);
            Check(entry.Weapon.stats.damage == 0 && other.Weapon.stats.damage == 0 && Time.timeScale == 0
                && !f.Ui.levelUpPanel.activeSelf, "stale clicks, late completion, repeated cancel cannot revive a dead run");
            State(entry, 6, 2, 1, "death cleanup returns borrowed pool without erasing earned XP");
        }
    }

    private static void SelectionAuthorization()
    {
        var f = new Fixture();
        var entry = f.Hero();
        var other = f.Hero();
        f.Activate(entry);
        other.Exp.GetExp(24);
        State(other, 0, 1, 0, "inactive hero cannot earn outside fusion");
        entry.Exp.GetExp(5);
        int version = entry.Exp.SelectionVersion;
        f.Ui.levelUpPanel.SetActive(false);
        f.Ui.levelUpButtons[0].SelectUpgrade();
        Check(entry.Weapon.stats.damage == 0 && entry.Exp.SelectionVersion == version, "hidden panel cannot consume choice");
        f.Ui.levelUpPanel.SetActive(true);
        entry.Player.assignedWeapons.Clear();
        f.Ui.levelUpButtons[0].SelectUpgrade();
        Check(entry.Weapon.stats.damage == 0 && entry.Exp.SelectionVersion == version, "unequipped weapon cannot consume choice");
        entry.Player.assignedWeapons.Add(entry.Weapon);
        f.Activate(other);
        f.Ui.levelUpButtons[0].SelectUpgrade();
        Check(entry.Weapon.stats.damage == 0 && entry.Exp.SelectionVersion == version, "old selection owner cannot act after hero switch");
        f.Activate(entry);
        entry.Exp.enabled = false;
        f.Ui.levelUpButtons[0].SelectUpgrade();
        Check(entry.Weapon.stats.damage == 0, "disabled current controller cannot consume");
        entry.Exp.enabled = true;
        f.Ui.levelUpButtons[0].SelectUpgrade();
        Check(entry.Weapon.stats.damage == 0.25f && Time.timeScale == 1, "rejected attempts did not lose pending choice");
    }

    private static void NonpositiveAndCap()
    {
        var f = new Fixture();
        var entry = f.Hero(count: 3);
        f.Activate(entry);
        entry.Exp.GetExp(0);
        entry.Exp.GetExp(-1);
        entry.Exp.GetExp(int.MinValue);
        State(entry, 0, 1, 0, "zero and negative XP ignored");
        Check(entry.Exp.SelectionVersion == 0 && !f.Ui.levelUpPanel.activeSelf, "invalid amounts create no selection");
        entry.Exp.GetExp(5);
        int pending = entry.Exp.SelectionVersion;
        entry.Exp.GetExp(0);
        entry.Exp.GetExp(-8);
        Check(entry.Exp.SelectionVersion == pending && f.Ui.levelUpPanel.activeSelf, "invalid amounts do not disturb pending choice");
        f.Ui.levelUpButtons[0].SelectUpgrade();
        int version = entry.Exp.SelectionVersion;
        entry.Exp.GetExp(int.MaxValue);
        entry.Exp.GetExp(int.MaxValue);
        for (int i = 0; i < 3; i++)
        {
            Time.unscaledTime = i + 1;
            entry.Exp.LevelUp();
            f.Ui.levelUpButtons[0].SelectUpgrade();
            entry.Exp.CompleteUpgradeSelection();
        }
        State(entry, 4294967299L, 2, int.MaxValue, "cap stores long excess instead of looping thresholds");
        Check(entry.Exp.SelectionVersion == version && entry.Weapon.stats.damage == 0.25f
            && !f.Ui.levelUpPanel.activeSelf && Time.timeScale == 1, "cap cannot repeatedly offer or apply selections");
    }

    private static void MissingSelectionUi()
    {
        for (int variant = 0; variant < 6; variant++)
        {
            var f = new Fixture();
            var entry = f.Hero();
            f.Activate(entry);
            switch (variant)
            {
                case 0: UIController.instance = null; break;
                case 1: f.Ui.levelUpPanel = null; break;
                case 2: f.Ui.levelUpButtons = null; break;
                case 3: f.Ui.levelUpButtons = new LevelUpSelectionButton[3]; break;
                case 4: entry.Player.assignedWeapons.Clear(); break;
                case 5: entry.Weapon.availableUpgrades = Array.Empty<UpgradeType>(); break;
            }
            entry.Exp.GetExp(24);
            State(entry, 24, 4, 0, "missing UI/eligible choices consumes thresholds without blocking");
            Check(Time.timeScale == 1 && entry.Exp.SelectionVersion == 0, "fallback does not pause or invent choices");
        }
    }

    private static void State(Hero hero, long total, int level, int remainder, string message)
    {
        long actualTotal = hero.Exp.TotalExperience;
        Check(actualTotal == total && hero.Exp.currentLevels == level && hero.Exp.currentExperience == remainder,
            message + "; expected total/level/remainder " + total + "/" + level + "/" + remainder
            + ", got " + actualTotal + "/" + hero.Exp.currentLevels + "/" + hero.Exp.currentExperience);
    }
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Hero
    {
        public PlayerController Player;
        public ExperienceLevelController Exp;
        public Weapon Weapon;
    }
    private sealed class Fixture
    {
        public readonly UIController Ui = new UIController();
        public readonly WorldManager Manager = new WorldManager();
        public Fixture(bool withUi = true)
        {
            PlayerController.instance = null;
            ExperienceLevelController.instance = null;
            UIController.instance = withUi ? Ui : null;
            Time.timeScale = 1;
            Time.unscaledTime = 0;
            UnityEngine.Random.Indices.Clear();
            UnityEngine.Object.InstantiateCalls = 0;
            Ui.levelUpPanel.SetActive(false);
            Ui.levelUpButtons = new[] { Button(), Button(), Button() };
        }
        public LevelUpSelectionButton Button() => new LevelUpSelectionButton
        {
            weaponIcon = new Image(), nameLevelText = new TMP_Text(), upgradeDescText = new TMP_Text(), ui = Ui
        };
        public Hero Hero(int level = 1, int remainder = 0, int count = 10, List<int> curve = null, string name = "weapon")
        {
            var hero = new Hero { Player = new PlayerController(), Exp = new ExperienceLevelController() };
            var world = new World { Manager = Manager, Player = hero.Player };
            hero.Player.Owner = hero.Exp.Owner = world;
            hero.Exp.gameObject = hero.Player.gameObject;
            hero.Player.Components[typeof(ExperienceLevelController)] = hero.Exp;
            hero.Exp.Components[typeof(PlayerController)] = hero.Player;
            hero.Exp.currentLevels = level;
            hero.Exp.currentExperience = remainder;
            hero.Exp.levelCount = count;
            if (curve != null) hero.Exp.expLevels = curve;
            hero.Weapon = AddWeapon(hero, name);
            return hero;
        }
        public Weapon AddWeapon(Hero hero, string name)
        {
            var weapon = new Weapon { weaponName = name, SourceOwner = hero.Player, Owner = hero.Player.Owner };
            hero.Player.assignedWeapons.Add(weapon);
            return weapon;
        }
        public void Activate(Hero hero)
        {
            PlayerController.instance = hero.Player;
            hero.Exp.BindAsCurrent();
        }
        public void Fuse(Hero entry, Hero other)
        {
            Activate(entry);
            Manager.IsFused = true;
            Manager.FusionPlayer = entry.Player;
            Manager.FusionWeapons.Clear();
            Manager.FusionWeapons.AddRange(entry.Player.assignedWeapons);
            Manager.FusionWeapons.AddRange(other.Player.assignedWeapons);
            entry.Exp.BeginFusionExperience(other.Exp);
            entry.Exp.BindAsCurrent();
        }
        public void EndFusion(Hero entry, bool death)
        {
            // WorldManager.EndFusion's inspected ordering; this does not test that manager itself.
            entry.Exp.EndFusionExperience();
            Manager.FusionWeapons.Clear();
            Manager.IsFused = false;
            Manager.FusionPlayer = null;
            if (!death) Activate(entry);
            entry.Exp.ReconcileUpgradeSelection(death);
        }
    }
}
