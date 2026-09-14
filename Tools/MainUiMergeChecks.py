#!/usr/bin/env python3
"""Read-only UI merge gate; Python stdlib, no Unity/index/refs or file writes.

119208b supplies visuals; 41ff8e2 supplies gameplay and shared-HUD bindings.
Raw comparisons normalize checkout line endings only; the gameplay projection
also permits exactly RunStageController.bossHealth 300 -> 600 for Rift Lord,
the active world timer 15 -> 30 and its 15s Switch -> 30s Switch label.
The shared health UI is projected separately from pinned main 486e9f3, followed
by StarterSceneChecks' exact Material3/Echo2 catalogue and 30 -> 90 timer/label
projection. Disabled legacy timers and all other fields remain protected.
Run --self-test; --require-runtime additionally gates runtime declarations.
"""
import argparse
from pathlib import Path
import re
import subprocess
import sys

sys.dont_write_bytecode = True
import MapMergeChecks as maps
import HealthUiMergeChecks as health
import StarterSceneChecks as starters

ROOT = Path(__file__).resolve().parents[1]
LOCAL = "41ff8e20424ee5e7a875da965b2809d99cea27b3"
MAIN = "119208bd5f645b890c7ad140955d1c62e0af2d37"
HUD, HOST, MANAGER = 179380105, 179380101, 417984749
VICTORY, UPGRADE = 2065265774, 1621958287
AUDIO = "669bbd907a143824eb90dd2d7cb8b1b3"
VICTORY_CALLS = {1854232803: "Restart", 343408623: "MainMenu", 835314908: "Quit"}
LOCAL_UI = {179380106, 179380107, 221654011, 544970448, 2064884799}
MENU_INSTRUCTIONS_TMP = 1243867567
require = maps.require


def git(*args):
    return subprocess.check_output([
        "git", "--no-pager", "--no-optional-locks", "-c",
        f"safe.directory={ROOT.as_posix()}", *args,
    ], cwd=ROOT).decode("utf-8").replace("\r\n", "\n")


def scene_at(revision, name):
    return maps.Scene.parse(git("show", f"{revision}:Assets/Scenes/{name}.unity"))


def load(name):
    text = (ROOT / f"Assets/Scenes/{name}.unity").read_text(encoding="utf-8")
    require(not re.search(r"^(<<<<<<<|=======|>>>>>>>)", text, re.M), f"{name}: conflict markers")
    return maps.Scene.parse(text)


def replace_field(doc, key, value):
    doc, count = re.subn(r"^(  " + re.escape(key) + r":)[^\n]*$",
                         lambda m: m[1] + " " + value, doc, flags=re.M)
    require(count == 1, f"Expected one field: {key}")
    return doc


def run_stage_controller(scene):
    found = []
    for ident, doc in scene.docs.items():
        by_guid = "  m_Script: {fileID: 11500000, guid: 6c50b95882b322b4f9f75359ba3ebc05, type: 3}\n" in doc
        by_class = "  m_EditorClassIdentifier: Assembly-CSharp::RunStageController\n" in doc
        if by_guid or by_class:
            require(by_guid and by_class and scene.types[ident] == 114,
                    f"RunStageController: GUID/class mismatch on {ident}")
            found.append(ident)
    require(len(found) == 1, "Expected exactly one RunStageController document")
    return found[0]


def expected_game(local, visual):
    """Compose exact HUD, health, boss, historical interval and starter projections."""
    result = maps.Scene.parse(local.text())
    old, incoming = local.subtree(HUD), visual.subtree(HUD)
    require(old - incoming == {MANAGER}, "Unexpected local-only HUD documents")
    require(not ((incoming - old) & set(local.docs)), "Incoming HUD ID collision")
    for ident in sorted(incoming):
        result.docs[ident] = visual.docs[ident]
        result.types[ident] = visual.types[ident]
    # Scene-local dependencies must never be imported from the single-world HUD.
    for ident in {HOST, MANAGER, *LOCAL_UI}:
        result.docs[ident] = local.docs[ident]
    require("victoryUI:" not in result.docs[MANAGER], "Local authority contract changed")
    result.docs[MANAGER] += "  victoryUI: {fileID: 2065265773}\n"
    for ident, method in VICTORY_CALLS.items():
        doc, count = re.subn(r"^        m_MethodName:[^\n]*$",
                             "        m_MethodName: " + method,
                             result.docs[ident], flags=re.M)
        require(count == 1, f"Unexpected Victory event count: {ident}")
        result.docs[ident] = doc
    run = run_stage_controller(local)
    doc = local.docs[run]
    require(maps.field(doc, "bossHealth") == "300" and "  bossHealth: 300\n" in doc,
            "Historical RunStageController.bossHealth must be exactly 300")
    result.docs[run] = replace_field(doc, "bossHealth", "600")
    result.docs[maps.ROOT_ID] = result.docs.pop(maps.ROOT_ID)
    result = maps.project_switch_interval(result)
    upstream = scene_at(health.UPSTREAM, "Main")
    result = health.project_health_ui(result, upstream)
    return starters.project_starters(result, upstream)


def decorative_images(scene):
    buttons = {maps.ref(d, "m_GameObject") for d in scene.docs.values() if "  m_OnClick:" in d}
    return {i for i, d in scene.docs.items()
            if "m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.Image\n" in d
            and maps.ref(d, "m_GameObject") not in buttons}


def expected_menu(local, visual):
    result = maps.Scene.parse(visual.text())
    removed = visual.subtree(1869351070)  # Legacy Main Menu BGM root.
    for ident in removed:
        del result.docs[ident]
        del result.types[ident]
    audio_ids = {i for i, d in local.docs.items() if f"guid: {AUDIO}," in d}
    require(audio_ids == {7094705692613444434, 5431352697369275453}, "Menu audio identity changed")
    require(not (audio_ids & set(result.docs)), "Menu audio ID collision")
    for ident in sorted(audio_ids):
        result.docs[ident], result.types[ident] = local.docs[ident], local.types[ident]
    result.docs[maps.ROOT_ID] = local.docs[maps.ROOT_ID]
    for ident in decorative_images(visual):
        result.docs[ident] = replace_field(result.docs[ident], "m_RaycastTarget", "0")
    # Native raycasts hit the oversized instruction TMP before PLAY/QUIT;
    # valid onClick bindings do not prove that pointer events reach the buttons.
    doc = result.docs[MENU_INSTRUCTIONS_TMP]
    require(maps.ref(doc, "m_GameObject") == 1243867565
            and "m_EditorClassIdentifier: Unity.TextMeshPro::TMPro.TextMeshProUGUI\n" in doc,
            "Menu instruction TMP identity changed")
    result.docs[MENU_INSTRUCTIONS_TMP] = replace_field(doc, "m_RaycastTarget", "0")
    result.docs[maps.ROOT_ID] = result.docs.pop(maps.ROOT_ID)
    return result


def compare(actual, expected, name):
    actual.validate()  # local refs, unique ownership, reciprocal hierarchy and roots
    require(actual.prefix == expected.prefix, f"{name}: YAML preamble changed")
    require(set(actual.docs) == set(expected.docs), f"{name}: unexpected document IDs")
    changed = [i for i in expected.docs if actual.docs[i] != expected.docs[i]]
    require(not changed, f"{name}: visual/binding/gameplay preservation mismatch: {changed}")
    audio = [i for i, d in actual.docs.items() if actual.types[i] == 1001
             and f"m_SourcePrefab: {{fileID: 100100000, guid: {AUDIO}," in d]
    require(len(audio) == 1, f"{name}: expected exactly one GameAudio prefab")
    require(82 not in actual.types.values(), f"{name}: legacy scene AudioSource remains")
    require(not re.search(r"^  gameOverManager:", actual.text(), re.M), f"{name}: stale PlayerHealth field")
    for ident, doc in actual.docs.items():
        if "m_OnClick:" in doc:
            require(not re.search(r"^        m_MethodName:\s*$", doc, re.M), f"{name}: empty button event {ident}")


def game_checks(actual, local, visual, name):
    expected = expected_game(local, visual)
    compare(actual, expected, name)
    require(maps.ref(actual.docs[MANAGER], "m_GameObject") == HOST, "Manager must use shared HUD")
    require(maps.ref(actual.docs[MANAGER], "runController") == maps.ref(local.docs[MANAGER], "runController"),
            "Run controller binding changed")
    for field, target in (("gameOverUI", 760710911), ("victoryUI", 2065265773)):
        require(maps.ref(actual.docs[MANAGER], field) == target, f"Wrong {field}")
        require(maps.field(actual.docs[target], "m_IsActive") == "0", f"{field} must start hidden")
    for ident, method in VICTORY_CALLS.items():
        doc = actual.docs[ident]
        require(f"m_Target: {{fileID: {MANAGER}}}" in doc and f"m_MethodName: {method}\n" in doc
                and "m_TargetAssemblyTypeName: GameOverManager, Assembly-CSharp\n" in doc
                and "m_CallState: 2\n" in doc, f"Wrong Victory event {ident}")
    maps.timings(starters.historical_timing_view(actual), name)
    map_ids = visual.subtree(maps.MAP_ROOTS[0]) | visual.subtree(maps.MAP_ROOTS[1])
    require(len(map_ids) == 51, "Main map authority changed")
    require(all(actual.docs[i] == local.docs[i] for i in map_ids), "Local map changed")
    for ident in map_ids:
        doc = actual.docs[ident]
        for mono in maps.MONOS:
            doc = doc.replace(f"  - component: {{fileID: {mono}}}\n", "")
        require(doc == visual.docs[ident], f"Main map visual data changed: {ident}")
    non_ui = set(local.docs) - local.subtree(HUD)
    require(all(actual.docs[i] == expected.docs[i] for i in non_ui),
            "Non-UI document changed beyond exact health UI, bossHealth, starter and timer/label projections")
    require("SELECT UPGRADE" in "".join(actual.docs[i] for i in actual.subtree(UPGRADE)), "Upgrade title missing")
    print(f"{name}: {len(actual.docs)} docs; {len(non_ui)} non-UI docs exact to 41ff8e2 "
          "except exact shared health UI, bossHealth 300 -> 600, Material3/Echo2 starters, "
          "eight-weapon catalogues and active timer/label 15 -> 30 -> 90; "
          "51 map docs preserved; 90/5/.8/480; legacy timers 15 PASS")


def build_checks():
    path = ROOT / "ProjectSettings/EditorBuildSettings.asset"
    text = path.read_text(encoding="utf-8")
    entries = re.findall(r"  - enabled: ([01])\n    path: ([^\n]+)\n    guid: ([^\n]+)", text)
    names = ["Main Menu", "Main", "DebugRun"]
    paths = [f"Assets/Scenes/{n}.unity" for n in names]
    require([p for _, p, _ in entries[:3]] == paths, "Build order must be Menu/Main/DebugRun")
    require(len(entries) in (3, 4) and len({p for _, p, _ in entries}) == len(entries), "Duplicate/unexpected build entries")
    if len(entries) == 4:
        require(entries[3][:2] == ("0", "Assets/Scenes/SampleScene.unity"), "SampleScene must be disabled")
    for enabled, path, guid in entries:
        meta = (ROOT / (path + ".meta")).read_text(encoding="utf-8")
        require(f"guid: {guid}\n" in meta, f"Build GUID mismatch: {path}")
        if path in paths:
            require(enabled == "1", f"Disabled entry: {path}")
            original = git("show", f"{LOCAL}:{path}.meta")
            # Unity's empty importer values may retain trailing spaces after merge.
            require([line.rstrip() for line in meta.splitlines()] ==
                    [line.rstrip() for line in original.splitlines()], f"Scene meta changed: {path}")
    print("BuildSettings: unique Menu first, Main, DebugRun; scene GUIDs preserved PASS")


def script_guids(scenes):
    available = set()
    for meta in (ROOT / "Assets").rglob("*.cs.meta"):
        match = re.search(r"^guid: (\w+)$", meta.read_text(encoding="utf-8"), re.M)
        if match:
            available.add(match[1])
    required = set()
    for scene in scenes:
        for doc in scene.docs.values():
            if ("m_EditorClassIdentifier: Assembly-CSharp::" in doc
                    or "m_EditorClassIdentifier: '::'" in doc):
                required.update(re.findall(r"m_Script: \{fileID: 11500000, guid: (\w+),", doc))
    require(required <= available, f"Missing scene MonoScript GUIDs: {required - available}")
    print(f"Scene project MonoScript GUID existence: {len(required)} PASS (class duplication/import not covered)")


def runtime_contracts(strict):
    checks = {
        "optional serialized GameOverManager.victoryUI": ("Assets/Game/Presentation/UI/GameOverManager.cs",
            r"\[SerializeField\]\s+private\s+GameObject\s+victoryUI\s*[;=]"),
        "SoundId.MusicMenu": ("Assets/Game/Features/Audio/SoundId.cs", r"\bMusicMenu\b"),
        "GameAudioEvents menu music branch": ("Assets/Game/Features/Audio/GameAudioEvents.cs", r"SoundId\.MusicMenu\b"),
    }
    pending = [label for label, (path, pattern) in checks.items()
               if not re.search(pattern, (ROOT / path).read_text(encoding="utf-8"))]
    if pending:
        print("PENDING runtime contracts: " + "; ".join(pending))
    require(not strict or not pending, "Runtime contracts not ready")
    if not pending:
        print("Runtime contract declarations present (behavior still requires Unity validation)")


def self_test(game, menu, expected_game_scene, expected_menu_scene):
    run = run_stage_controller(expected_game_scene)
    mutations = [
        (game, expected_game_scene, run, lambda d: replace_field(d, "bossHealth", "300")),
        (game, expected_game_scene, run, lambda d: replace_field(d, "bossHealth", "599")),
        (game, expected_game_scene, run, lambda d: replace_field(d, "bossHealth", "601")),
        (game, expected_game_scene, run, lambda d: d + "  bossHealth: 600\n"),
        (game, expected_game_scene, run, lambda d: replace_field(d, "bossDistance", "7")),
        (game, expected_game_scene, run, lambda d: replace_field(d, "m_EditorClassIdentifier", "Assembly-CSharp::OtherController")),
        (game, expected_game_scene, MANAGER, lambda d: d + "  bossHealth: 600\n"),
        (game, expected_game_scene, HOST, lambda d: d.replace("  m_Layer:", f"  - component: {{fileID: {MANAGER}}}\n  m_Layer:")),
        (game, expected_game_scene, MANAGER, lambda d: d.replace("fileID: 2065265773", "fileID: 999999999999")),
        (game, expected_game_scene, 1854232803, lambda d: d.replace("m_MethodName: Restart", "m_MethodName:")),
        (game, expected_game_scene, 508784823, lambda d: d + "  invalidBalance: 1\n"),
        (game, expected_game_scene, 5576027, lambda d: d + "  gameOverManager: {fileID: 417984749}\n"),
        (menu, expected_menu_scene, 1260757538, lambda d: d.replace("m_RaycastTarget: 0", "m_RaycastTarget: 1")),
        (menu, expected_menu_scene, MENU_INSTRUCTIONS_TMP, lambda d: replace_field(d, "m_RaycastTarget", "1")),
        (menu, expected_menu_scene, 1468259094, lambda d: d.replace("  m_Pivot:", "  changedLayout: 1\n  m_Pivot:")),
    ]
    mutations.extend((game, expected_game_scene, ident, mutate)
                     for ident, mutate, _ in maps.timing_mutations(expected_game_scene))
    for scene, expected, ident, mutate in mutations:
        broken = maps.Scene.parse(scene.text())
        broken.docs[ident] = mutate(broken.docs[ident])
        require(broken.docs[ident] != scene.docs[ident], f"Ineffective mutation {ident}")
        try:
            compare(broken, expected, "self-test")
        except ValueError:
            continue
        raise ValueError(f"Self-test accepted mutation {ident}")
    print(f"Self-tests: {len(mutations)} timing/label/boss-health/scope/ownership/ref/event/balance/stale-field/image+TMP-raycast/layout rejections PASS")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--require-runtime", action="store_true")
    args = parser.parse_args()
    visual = scene_at(MAIN, "Main")
    scenes = []
    for name in ("Main", "DebugRun"):
        local, actual = scene_at(LOCAL, name), load(name)
        game_checks(actual, local, visual, name)
        scenes.append(actual)
    local_menu, visual_menu = scene_at(LOCAL, "Main Menu"), scene_at(MAIN, "Main Menu")
    menu, expected = load("Main Menu"), expected_menu(local_menu, visual_menu)
    compare(menu, expected, "Main Menu")
    scenes.append(menu)
    print(f"Main Menu: full main visuals/button layout/events; {len(decorative_images(menu))} decorative Images "
          f"and instruction TMP {MENU_INSTRUCTIONS_TMP} non-raycast; singleton GameAudio PASS")
    build_checks()
    script_guids(scenes)
    runtime_contracts(args.require_runtime)
    if args.self_test:
        self_test(scenes[0], menu, expected_game(scene_at(LOCAL, "Main"), visual), expected)
    print("PASS: static scene acceptance only; no Unity/import/Play Mode validation")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
