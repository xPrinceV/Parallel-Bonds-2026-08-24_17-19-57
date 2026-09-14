#!/usr/bin/env python3
"""Exact scene-only starter/catalogue and 90-second switch projection.

Compose AFTER the pinned shared-health projection. Never import remote scripts,
projectiles, hierarchy or player references. Reuse local controller GUIDs and
local Shield/PhysicsBullet prefabs; upstream486e9f3 supplies only weapon data.
--apply requires BOTH scenes to match the pre-starter health projection, checks
for concurrent edits, and preserves checkout EOL. Default/--self-test are read-only.
RunStageController is intentionally untouched: the separate runtime change owns
three rounds per world (a serialized default or the observed StateSwitchController
90s/3-residence constants). PlayerController must consume startingWeapons without
replacing the optional catalogue.
"""
import argparse
import hashlib
from pathlib import Path
import re
import subprocess
import sys

sys.dont_write_bytecode = True
import MapMergeChecks as maps

ROOT = Path(__file__).resolve().parents[1]
UPSTREAM = "486e9f3c100018048b033e54687188bbceef1292"
BASE = "e7e150f5d91c72f8f5e65187391570f7e9022a0f"
# player, Weapons transform, BuffController, original catalogue, new GO bases
HEROES = (
    (5576025, 367653038, 5576029,
     (1591325561, 123630154, 1881042334, 508784823, 273773926, 4508784823),
     (5600000100, 5600000110)),
    (1488475395, 1709888715, 1488475396,
     (1725765564, 1557428336, 620763871, 3508784823, 3273773926, 5508784823),
     (5600000200, 5600000210)),
)
WEAPONS = {
    "Shield": (441805113, "94e42513f263d434f844addfd7eb21c4",
               {"orbitDistance": "2.5", "orbitSpeed": "180", "damage": "11",
                "duration": "7", "cooldown": "2.5"}),
    "Sniper": (143472809, "5f509c35341f6404fa102572d64f1a92",
               {"attackSpeed": "0.2", "attackDamage": "20", "attackRange": "15"}),
}
PREFABS = {
    "Shield": ("Shield", 1825500607332492713, "f330b57cd9192a743981b2c473504d13",
               "shieldPrefab", "ShieldOrbitController", "a953f777e20a9e14897d0a283fc174da"),
    "Sniper": ("PhysicsBullet", 9132437147970937572, "e44e30c026ba5234b90ca6115ba14481",
               "bullet", "PhysicsBullet", "5f29fe2ef866fe64991d4bedccfd7e25"),
}
ADDED = {base + offset for hero in HEROES for base in hero[4] for offset in range(3)}
require = maps.require


def parse(data):
    return maps.Scene.parse((data.decode("utf-8") if isinstance(data, bytes) else data).replace("\r\n", "\n"))


def git_bytes(*args):
    return subprocess.check_output(["git", "--no-pager", "-c",
        f"safe.directory={ROOT.as_posix()}", "--no-optional-locks", *args], cwd=ROOT)


def replace_once(doc, old, new):
    require(doc.count(old) == 1, f"Expected exactly one {old!r}")
    return doc.replace(old, new, 1)


def field(doc, key, value):
    return replace_once(doc, f"  {key}: {maps.field(doc, key)}\n", f"  {key}: {value}\n")


def references(key, ids):
    return f"  {key}:\n" + "".join(f"  - {{fileID: {i}}}\n" for i in ids)


def remap(doc, ids):
    doc = re.sub(r"(?<=&)-?\d+", lambda m: str(ids.get(int(m[0]), int(m[0]))), doc)
    return re.sub(r"(?<=fileID: )-?\d+(?=\})",
                  lambda m: str(ids.get(int(m[0]), int(m[0]))), doc)


def weapon_documents(scene, upstream, name, base, parent, player, buff):
    """Local inactive Lantern is only a structural template (GO/Transform/header)."""
    ident, guid, tuning = WEAPONS[name]
    source = upstream.docs[ident]
    require(upstream.types[ident] == 114
            and maps.field(source, "m_EditorClassIdentifier") == f"Assembly-CSharp::{name}Controller"
            and maps.field(source, "m_Script") == f"{{fileID: 11500000, guid: {guid}, type: 3}}",
            f"Upstream {name} identity drift")
    for key, value in tuning.items():
        require(maps.field(source, key) == value, f"Upstream {name}.{key} tuning drift")
    ids = {123630152: base, 123630153: base + 1, 123630154: base + 2, 367653038: parent}
    go = field(remap(scene.docs[123630152], ids), "m_Name", name)
    require(maps.field(go, "m_IsActive") == "0", "Template must start inactive")
    transform = remap(scene.docs[123630153], ids)
    require(maps.field(transform, "m_Children") == "[]"
            and maps.field(transform, "m_LocalPosition") == "{x: 0, y: 0, z: 0}"
            and maps.field(transform, "m_LocalScale") == "{x: 1, y: 1, z: 1}",
            "Weapon template transform drift")
    header = remap(scene.docs[123630154].split("  stats:\n", 1)[0], ids)
    header = field(header, "m_Script", f"{{fileID: 11500000, guid: {guid}, type: 3}}")
    header = field(header, "m_EditorClassIdentifier", f"Assembly-CSharp::{name}Controller")
    require("  player:" not in header, "Template owner field drift")
    stats = re.findall(r"^  stats:\n(?:    [^\n]*\n)+", source, re.M)
    require(len(stats) == 1, f"Missing {name} stats")
    for key in "speed damage range attackSpeed amount duration bounces area".split():
        require(f"    {key}: 1\n" in stats[0], f"{name}: non-unit initial {key}")
    require(maps.field(source, "weaponLevel") == "0", "New weapons must start at level zero")
    doc = header + f"  player: {{fileID: {player}}}\n" + stats[0]
    for key in ("weaponLevel", "icon", "weaponName", "availableUpgrades", "weaponIcon"):
        doc += f"  {key}: {maps.field(source, key)}\n"
    _, prefab_id, prefab_guid, prefab_field, _, _ = PREFABS[name]
    doc += f"  {prefab_field}: {{fileID: {prefab_id}, guid: {prefab_guid}, type: 3}}\n"
    if name == "Shield":
        doc += "  shieldPivot: {fileID: 0}\n"
    for key, value in tuning.items():
        doc += f"  {key}: {value}\n"
    if name == "Sniper":
        # Explicit local initialized/swept projectile defaults, not remote AddForce behavior.
        doc += "  projectileSpeed: 973\n  projectileSpacing: 0.15\n"
    doc += f"  buffHolder: {{fileID: {buff}}}\n"
    return ((base, 1, go), (base + 1, 4, transform), (base + 2, 114, doc))


def project_starters(scene, upstream):
    """Pure fail-closed projection: six existing documents + twelve new documents."""
    scene.validate()
    maps.switch_controllers(scene)
    label = maps.switch_label(scene)
    require(maps.field(scene.docs[maps.ACTIVE_SWITCH], "timer") == "30"
            and maps.field(scene.docs[label], "m_text") == '"30s Switch"',
            "Pre-starter timer/label must be 30")
    require(not ADDED.intersection(scene.docs), "Starter document ID collision")
    for name, (_, guid, _) in WEAPONS.items():
        require(not any(f"guid: {guid}," in d or f"Assembly-CSharp::{name}Controller\n" in d
                        for d in scene.docs.values()), f"Unexpected existing {name}")
    result = parse(scene.text())
    changed = {maps.ACTIVE_SWITCH, label}
    for index, (player, parent, buff, catalogue, bases) in enumerate(HEROES):
        doc = scene.docs[player]
        require(maps.field(doc, "m_EditorClassIdentifier") == "Assembly-CSharp::PlayerController"
                and maps.field(doc, "assignedWeapons") == "[]"
                and "  startingWeapons:" not in doc, "Pre-starter player contract drift")
        world = scene.subtree(maps.CONTENTS[index])
        require({player, parent, buff, *catalogue} <= world, "Catalogue/owner crosses worlds")
        for weapon in catalogue:
            require(maps.field(scene.docs[maps.ref(scene.docs[weapon], "m_GameObject")], "m_IsActive") == "0",
                    "Original catalogue must remain inactive before Start")
        new_weapons = tuple(base + 2 for base in bases)
        doc = replace_once(doc, references("unassignedWeapons", catalogue),
                           references("unassignedWeapons", (*catalogue, *new_weapons)))
        starters = catalogue[:3] if index == 0 else new_weapons
        result.docs[player] = doc + references("startingWeapons", starters)
        children = maps.children(scene.docs[parent])
        require(len(children) == 6, "Pre-starter Weapons hierarchy drift")
        result.docs[parent] = replace_once(scene.docs[parent], references("m_Children", children),
            references("m_Children", (*children, *(base + 1 for base in bases))))
        for name, base in zip(WEAPONS, bases):
            for ident, kind, document in weapon_documents(scene, upstream, name, base, parent, player, buff):
                result.docs[ident], result.types[ident] = document, kind
        changed.update((player, parent))
    result.docs[maps.ACTIVE_SWITCH] = field(result.docs[maps.ACTIVE_SWITCH], "timer", "90")
    result.docs[label] = field(result.docs[label], "m_text", '"90s Switch"')
    result.docs[maps.ROOT_ID] = result.docs.pop(maps.ROOT_ID)
    require({i for i in scene.docs if scene.docs[i] != result.docs[i]} == changed,
            "Projection touched unexpected existing documents")
    require(set(result.docs) - set(scene.docs) == ADDED, "Projection added unexpected documents")
    result.validate()
    return result


def historical_timing_view(scene):
    """Prove 90s fields, then adapt ONLY those fields for the historical map gate.

    No documents/unknown fields are skipped; full-scene comparison happens before
    this adapter. MapMergeChecks retains its independent 15 -> 30 migration pin.
    """
    maps.switch_controllers(scene)
    label = maps.switch_label(scene)
    require(maps.field(scene.docs[maps.ACTIVE_SWITCH], "timer") == "90"
            and maps.field(scene.docs[label], "m_text") == '"90s Switch"', "Expected exact 90s switch")
    result = parse(scene.text())
    result.docs[maps.ACTIVE_SWITCH] = field(result.docs[maps.ACTIVE_SWITCH], "timer", "30")
    result.docs[label] = field(result.docs[label], "m_text", '"30s Switch"')
    return result


def compare(actual, expected, name):
    require(actual.prefix == expected.prefix and list(actual.docs) == list(expected.docs)
            and actual.types == expected.types, f"{name}: document identity/order/type/preamble drift")
    changed = [i for i in expected.docs if actual.docs[i] != expected.docs[i]]
    require(not changed, f"{name}: starter/preservation mismatch: {changed}")
    actual.validate()
    maps.timings(historical_timing_view(actual), name)


def local_assets():
    for name, (prefab, root, guid, _, controller, script) in PREFABS.items():
        path = f"Assets/Prefabs/{prefab}.prefab"
        data = parse((ROOT / path).read_bytes())
        # Prefab component IDs may be negative (PhysicsBullet); the scene-only
        # parser's positive-ID component helper is not suitable for these assets.
        components = [int(i) for i in re.findall(r"^  - component: \{fileID: (-?\d+)\}",
                                                 data.docs[root], re.M)]
        require(any(f"guid: {script}," in data.docs[i]
                    and f"Assembly-CSharp::{controller}\n" in data.docs[i] for i in components),
                f"{name}: missing local projectile root controller")
        require(f"guid: {guid}\n" in (ROOT / (path + ".meta")).read_text(), "Prefab GUID drift")
        for asset in (path, path + ".meta"):
            expected = git_bytes("show", f"{BASE}:{asset}").replace(b"\r\n", b"\n")
            if asset == "Assets/Prefabs/PhysicsBullet.prefab":
                # Only add the pistol's visual child; keep every physics/controller field pinned.
                pistol = git_bytes("show", f"{BASE}:Assets/Prefabs/Bullet.prefab").replace(b"\r\n", b"\n")
                visual = pistol[pistol.index(b"--- !u!1 &1356404543289259636\n"):
                                pistol.index(b"--- !u!1 &2019952897031522717\n")]
                visual = visual.replace(b"m_Father: {fileID: 8361674640618861563}",
                                        b"m_Father: {fileID: 8846754889022609262}")
                expected = expected.replace(b"  m_Children: []\n  m_Father: {fileID: 0}",
                                            b"  m_Children:\n  - {fileID: 3387112557502449583}\n  m_Father: {fileID: 0}")
                expected += visual
            require((ROOT / asset).read_bytes().replace(b"\r\n", b"\n") == expected,
                    f"Local projectile changed beyond its approved visual: {asset}")
        script_path = ROOT / f"Assets/Game/Features/Weapons/Controllers/{name}Controller.cs.meta"
        require(re.search(rf"^guid: {WEAPONS[name][1]}$", script_path.read_text(), re.M),
                "Local controller GUID drift")
    for path, guid, sprite in (
        ("Assets/sniper-rifle.png.meta", "a9c03bf3990ce9a44b7cf70d226fe649", "3351058555426389599"),
        ("Assets/craftpix-781198-free-shield-2d-game-assets-pack/PNG/Shield_4/4.png.meta",
         "1b88d48cbfd3d83488e65bb1d97778ec", "-4178046375966092550"),
    ):
        text = (ROOT / path).read_text()
        require(f"guid: {guid}\n" in text and sprite in text, "Local weapon icon reference drift")
    print("Local projectile preservation, Sniper pistol-sprite child, root controllers and asset references PASS")


def runtime_dependencies(strict=False):
    checks = (
        ("PlayerController serialized startingWeapons", "Assets/Game/Features/Player/PlayerController.cs",
         r"\[SerializeField\]\s+private\s+List<Weapon>\s+startingWeapons\b"),

    )
    pending = [label for label, path, pattern in checks
               if not re.search(pattern, (ROOT / path).read_text())]
    run = (ROOT / "Assets/Game/Features/GameFlow/RunStageController.cs").read_text()
    switch = (ROOT / "Assets/Game/Features/Worlds/StateSwitchController.cs").read_text()
    default_rounds = re.search(r"\bint\s+requiredRoundsPerWorld\s*=\s*3\s*;", run)
    fixed_rounds = (re.search(r"\bconst\s+int\s+RequiredResidencesPerWorld\s*=\s*3\s*;", switch)
                    and re.search(r"\bconst\s+float\s+ResidenceDuration\s*=\s*90f\s*;", switch)
                    and "StateSwitchController.RequiredResidencesPerWorld" in run)
    if not (default_rounds or fixed_rounds):
        pending.append("three-round default or fixed 90s/3-residence policy")
    if fixed_rounds:
        print("Runtime uses StateSwitchController 90s/3-residence constants; no scene round override needed")
    print("PENDING runtime dependencies: " + "; ".join(pending) if pending else
          "Runtime declarations present; starter/round behavior still requires runtime/Play Mode checks")
    require(not strict or not pending, "Runtime dependencies not ready")


def self_test(expected, before, upstream, name):
    mutations = [(i, lambda d: d + "  starterUnexpected: 1\n") for i in expected.docs]
    for player, parent, _, catalogue, bases in HEROES:
        mutations.extend((player, lambda d, k=k: replace_once(d, k, k.replace("fileID:", "wrongID:", 1)))
                         for k in (references("unassignedWeapons", (*catalogue, *(b + 2 for b in bases))),))
        mutations.append((player, lambda d: re.sub(r"(  startingWeapons:\n  - \{fileID: )\d+", r"\g<1>0", d)))
        mutations.append((parent, lambda d, b=bases[0]: replace_once(d, f"  - {{fileID: {b + 1}}}\n", "")))
        for weapon_name, base in zip(WEAPONS, bases):
            mutations.append((base, lambda d: field(d, "m_IsActive", "1")))
            for key in ("player", "buffHolder"):
                mutations.append((base + 2, lambda d, k=key: field(d, k, "{fileID: 0}")))
            for key in WEAPONS[weapon_name][2]:
                mutations.append((base + 2, lambda d, k=key: field(d, k, "999")))
            prefab_key = PREFABS[weapon_name][3]
            mutations.append((base + 2, lambda d, k=prefab_key: field(d, k, "{fileID: 0}")))
    mutations.extend((ident, mutate) for ident, mutate, _ in maps.timing_mutations(expected))
    for value in ("30", "89", "91"):
        mutations.append((maps.ACTIVE_SWITCH, lambda d, v=value: field(d, "timer", v)))
    for ident, mutate in mutations:
        broken = maps.Scene(expected.prefix, expected.docs.copy(), expected.types.copy())
        broken.docs[ident] = mutate(broken.docs[ident])
        require(broken.docs[ident] != expected.docs[ident], "Ineffective starter mutation")
        try:
            compare(broken, expected, name)
        except ValueError:
            continue
        raise ValueError(f"Accepted mutation on {ident}")
    original = before.text(), upstream.text()
    compare(project_starters(before, upstream), expected, name)
    require(original == (before.text(), upstream.text()), "Projection mutated input")
    compare(parse(expected.text().replace("\n", "\r\n")), expected, name)
    for broken in (expected,):
        try:
            project_starters(broken, upstream)
        except ValueError:
            continue
        raise ValueError("Projection accepted an already migrated input")
    print(f"{name}: {len(mutations)} mutation rejections; pure inputs, CRLF and repeat-apply rejection PASS")


def main():
    import HealthUiMergeChecks as health
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--require-runtime", action="store_true")
    args = parser.parse_args()
    local_assets()
    upstream = health.scene_at(UPSTREAM)
    planned = []
    for name in maps.NAMES:
        path = ROOT / f"Assets/Scenes/{name}.unity"
        original = path.read_bytes()
        before = health.project_health_ui(health.scene_at(BASE, name), upstream)
        expected = project_starters(before, upstream)
        if args.apply:
            health.compare_health_ui(parse(original), before, name)
        else:
            compare(parse(original), expected, name)
        if args.self_test:
            self_test(expected, before, upstream, name)
        newline = "\r\n" if b"\r\n" in original else "\n"
        output = expected.text().replace("\n", newline).encode("utf-8")
        planned.append((path, original, output))
        print(f"{name}: {len(before.docs)} -> {len(expected.docs)} docs; six changed, twelve added; "
              f"{len(before.docs) - 6} existing documents exact, including ALL health UI/RunStage documents")
        print(f"{name}: projected sha256={hashlib.sha256(output).hexdigest()}")
    runtime_dependencies(args.require_runtime)
    if args.apply:
        require(all(path.read_bytes() == old for path, old, _ in planned), "Concurrent scene drift; no writes")
        for path, _, output in planned:
            path.write_bytes(output)
        print("Applied only Main/DebugRun starter catalogue + 90s timer/label projection")
    else:
        print("PASS: exact scenes, eight local weapons per hero, explicit Material3/Echo2 starters; static acceptance only")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        sys.exit(f"FAIL: {error}")
