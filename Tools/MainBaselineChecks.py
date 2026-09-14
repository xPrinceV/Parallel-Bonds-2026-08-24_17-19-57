#!/usr/bin/env python3
"""Read-only six-weapon integration gate. Run with Python 3, optionally --self-test.

Balance authority: 402fc0f; preservation authority: 463de47, not the merge index.
HUD projection: 119208b through MainUiMergeChecks; gameplay remains independently gated.
Match MonoScript GUID + controller class + one instance per World Content subtree.
Only eight upgrade arrays, availableUpgrades and listed base scalars may change.
All runtime stats, references, loadouts, active flags and unrelated documents stay
as committed locally. Sniper/Shield are explicitly excluded (no local instances).
Bow amount is adapted from upstream multiplication (1 * stats.amount) to local
addition (0 + stats.amount). No Config asset or controller code is modified.
No Unity, writes, Git state changes, moving refs or Library backups are required.
"""

import argparse
import math
import re
import subprocess
import sys

sys.dont_write_bytecode = True
import MapMergeChecks as maps
import MainUiMergeChecks as ui

ROOT = maps.ROOT
BASE = "463de478144746f0fc484708edf728951ae2d6e8"
UPSTREAM = "402fc0f032f21aaed5894b75d3c8c02e38f4d718"
# Explicit allowlist: never transplant player/prefab references or runtime stats.
WEAPONS = {
    "Pistol": ("a340e0224149240478c1b0d4d49b5fcb", "attackSpeed attackDamage attackRange projectileSpeed"),
    "Lantern": ("562a62d8efa211b40b3ddd2b927314f6", "attackDamage attackSpeed duration amount"),
    "Lightning": ("b87ec39bd89e15c47beacf9c0d1d7c41", "attackDamage attackSpeed attackRange amount"),
    "Bow": ("fa448029ac02ef747990352686cbd659", "attackSpeed attackDamage amount projectileSpeed"),
    "Dagger": ("a0cb96ba1a742a443a512719a5964d2b", "attackDamage attackSpeed duration amount attackRange projectileSpeed bounces"),
    "Scythe": ("d9c6dcae6cec34642818f37685f37b49", "attackSpeed attackDamage area"),
}
EXCLUDED = {
    "Sniper": "5f509c35341f6404fa102572d64f1a92",
    "Shield": "94e42513f263d434f844addfd7eb21c4",
}
STATS = "damage speed range attackSpeed amount duration bounces area".split()
require = maps.require


def git(*args):
    return subprocess.check_output([
        "git", "--no-pager", "--no-optional-locks", "-c",
        f"safe.directory={ROOT.as_posix()}", *args,
    ], cwd=ROOT).decode("utf-8")


def scene_at(revision, name):
    return maps.Scene.parse(git("show", f"{revision}:Assets/Scenes/{name}.unity"))


def block(doc, key, indent):
    spaces = " " * indent
    pattern = rf"^{spaces}{re.escape(key)}:[^\n]*\n(?:{spaces}- [^\n]*\n)*"
    matches = list(re.finditer(pattern, doc, re.M))
    require(len(matches) == 1, f"Expected exactly one field {key}")
    return matches[0]


def replace_block(doc, key, indent, replacement):
    match = block(doc, key, indent)
    return doc[:match.start()] + replacement + doc[match.end():]


def instances(scene, name, guid):
    found = []
    for ident, doc in scene.docs.items():
        by_guid = f"guid: {guid}," in doc
        by_class = f"  m_EditorClassIdentifier: Assembly-CSharp::{name}Controller\n" in doc
        if by_guid or by_class:
            require(by_guid and by_class and scene.types[ident] == 114,
                    f"{name}: GUID/class mismatch on {ident}")
            found.append(ident)
    return found


def balance_fields(name, source):
    result = {(key + "Upgrades", 4): block(source, key + "Upgrades", 4)[0] for key in STATS}
    for key in ["availableUpgrades", *WEAPONS[name][1].split()]:
        result[key, 2] = block(source, key, 2)[0]
    if name == "Bow":
        require(maps.field(source, "amount") == "1", "Review changed upstream Bow count formula")
        result["amount", 2] = "  amount: 0\n"
    return result


def validate_choices(doc):
    encoded = maps.field(doc, "availableUpgrades")
    require(bool(re.fullmatch(r"(?:[0-9a-f]{8})+", encoded)), "Invalid upgrade enum array")
    choices = [int.from_bytes(bytes.fromhex(encoded[i:i + 8]), "little")
               for i in range(0, len(encoded), 8)]
    require(len(set(choices)) == len(choices), "Duplicate upgrade choices")
    for choice in choices:
        require(choice < len(STATS), f"Unknown upgrade enum {choice}")
        values = re.findall(r"^    - (.+)$", block(doc, STATS[choice] + "Upgrades", 4)[0], re.M)
        require(values and all(math.isfinite(float(v)) and float(v) > 0 for v in values),
                f"Empty/invalid selectable upgrade {STATS[choice]}")


def expected_scene(before, source):
    """Pure in-memory projection; all non-allowlisted bytes are preserved."""
    before.validate()
    expected = maps.Scene.parse(before.text())
    worlds = [before.subtree(content) for content in maps.CONTENTS]
    changes = []
    for name, guid in EXCLUDED.items():
        require(not instances(before, name, guid), f"Baseline unexpectedly enables {name}")
    for name, (guid, _) in WEAPONS.items():
        incoming = instances(source, name, guid)
        local = instances(before, name, guid)
        require(len(incoming) == 1, f"Expected one upstream {name}")
        require(len(local) == 2 and all(len(set(local) & world) == 1 for world in worlds),
                f"Expected one {name} per local world")
        fields = balance_fields(name, source.docs[incoming[0]])
        for ident in local:
            doc = before.docs[ident]
            altered = []
            for (key, indent), value in fields.items():
                old = block(doc, key, indent)[0]
                if old != value:
                    altered.append(key)
                doc = replace_block(doc, key, indent, value)
            validate_choices(doc)
            expected.docs[ident] = doc
            changes.append((name, ident, altered))
    return expected, changes


def verify(actual, expected, source, name, report=True):
    actual.validate()
    require(actual.prefix == expected.prefix and list(actual.docs) == list(expected.docs),
            f"{name}: changed document identity/order or preamble")
    changed = [i for i in expected.docs if actual.docs[i] != expected.docs[i]]
    require(not changed, f"{name}: balance/preservation mismatch on documents {changed}")
    ids = source.subtree(maps.MAP_ROOTS[0]) | source.subtree(maps.MAP_ROOTS[1])
    maps.validate_migrated(actual, source, ids, maps.runtime_contract("boundaryRoot"), name, report)


def runtime_contract():
    controllers = ROOT / "Assets/Game/Features/Weapons/Controllers"
    for name, (guid, _) in WEAPONS.items():
        require(re.search(rf"^guid: {guid}$", (controllers / f"{name}Controller.cs.meta").read_text(), re.M),
                f"{name}: runtime MonoScript GUID changed")
    bow = (controllers / "BowController.cs").read_text()
    bow = re.sub(r"//[^\n]*|/\*.*?\*/", "", bow, flags=re.S)
    require(re.search(r"Mathf\.FloorToInt\(\s*amount\s*\+\s*stats\.amount\s*\)", bow),
            "Bow no longer uses additive count; review amount adaptation")


def self_test(expected, source):
    weapon = instances(expected, "Scythe", WEAPONS["Scythe"][0])[0]
    mutations = [
        (weapon, lambda d: replace_block(d, "attackDamage", 2, "  attackDamage: 10\n")),
        (weapon, lambda d: replace_block(d, "areaUpgrades", 4, "    areaUpgrades: []\n")),
        (weapon, lambda d: replace_block(d, "availableUpgrades", 2, "  availableUpgrades: 0000000003000000\n")),
        (weapon, lambda d: replace_block(d, "damage", 4, "    damage: 2\n")),
        (weapon, lambda d: d + "  player: {fileID: 999999999}\n"),
        (maps.WORLDS[0], lambda d: d.replace("4300134801", "4300134802")),
        (373904104, lambda d: replace_block(d, "m_SortingLayerID", 2, "  m_SortingLayerID: 0\n")),
    ]
    for ident, mutate in mutations:
        broken = maps.Scene.parse(expected.text())
        broken.docs[ident] = mutate(broken.docs[ident])
        require(broken.docs[ident] != expected.docs[ident], "Ineffective self-test")
        try:
            verify(broken, expected, source, "Main", False)
        except ValueError:
            continue
        raise ValueError(f"Self-test accepted mutation on {ident}")
    print(f"Self-tests: {len(mutations)} balance/state/reference/map/preservation rejection cases PASS")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    runtime_contract()
    source = scene_at(UPSTREAM, "Main")
    source.validate()
    visual = ui.scene_at(ui.MAIN, "Main")
    print(f"Read-only: local preservation {BASE}; upstream balance {UPSTREAM}; HUD {ui.MAIN}")
    for name in maps.NAMES:
        before = scene_at(BASE, name)
        expected, changes = expected_scene(before, source)
        expected = ui.expected_game(expected, visual)
        path = ROOT / f"Assets/Scenes/{name}.unity"
        actual = maps.Scene.parse(maps.read(path).replace("\r\n", "\n"))
        verify(actual, expected, source, name)
        print(f"{name}: 12 weapon instances; allowed balance fields and reviewed main HUD projection only")
        for weapon, ident, fields in changes:
            print(f"  {weapon} {ident}: {', '.join(fields) or 'already aligned'}")
        if args.self_test and name == "Main":
            self_test(expected, source)
    print("PASS: all unrelated documents/runtime stats/references/loadouts/active flags preserved (LF/CRLF normalized)")
    print("Excluded: Sniper/Shield (no scene components). Adapted: Bow amount 1 upstream -> 0 local (multiply -> add)")
    print("Static scene acceptance only; Config/import/Play Mode and upstream weapon behavior are not runtime-tested")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
