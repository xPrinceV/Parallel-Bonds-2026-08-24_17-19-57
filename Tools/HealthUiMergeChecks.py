#!/usr/bin/env python3
"""Exact health-only projection; stdlib/raw Unity documents, never a YAML serializer.

Parent gates: project_health_ui(scene, upstream) -> NEW MapMergeChecks.Scene.
Inputs are the pre-health local projection and pinned upstream Main; unrelated
map/assets/runtime inheritance stays with the caller. compare_health_ui(actual,
expected, name) compares ALL documents, order and preamble (checkout EOL only).
CLI read-only checks compose health then StarterSceneChecks against pinned BASE
for BOTH scenes, protecting every document rather than ignoring gameplay deltas.
--self-test runs mutation rejection tests. Historical --apply remains health-only:
it requires both scenes still equal BASE and preserves checkout newlines. Use
StarterSceneChecks.py --apply for the separately approved starter configuration.
"""
import argparse
import hashlib
from pathlib import Path
import re
import subprocess
import sys

sys.dont_write_bytecode = True
import MapMergeChecks as maps
import StarterSceneChecks as starters

ROOT = Path(__file__).resolve().parents[1]
BASE = "e7e150f5d91c72f8f5e65187391570f7e9022a0f"
UPSTREAM = "486e9f3c100018048b033e54687188bbceef1292"
NAMES = ("Main", "DebugRun")
CANVAS, PARENT, HUD = 2000288233, 5576024, 179380105
SLIDER, TEXT = 1945700038, 1847001157
ECHO_CANVAS, ECHO_GO = 2621169, 2621168
HEALTH = (5576027, 1488475399)
ADDED = {720703110, 720703111, 720703112, 720703113}
VISUAL = {1847001156, 1856098901, 1945700037, 2107953942}
GRAPHICS = {1282045163, TEXT, 2107953943, 720703112}
CHANGED = VISUAL | {CANVAS, PARENT, maps.ROOT_ID, SLIDER, 2000288234,
                    ECHO_GO, HEALTH[1]} | (GRAPHICS - ADDED)
ART = "Assets/Vampire Survival Assets/Art/Hp bar.png"
require = maps.require


def git_bytes(*args):
    return subprocess.check_output(["git", "--no-pager", "--no-optional-locks", "-c",
                                    f"safe.directory={ROOT.as_posix()}", *args], cwd=ROOT)


def parse(data):
    if isinstance(data, bytes):
        data = data.decode("utf-8")
    return maps.Scene.parse(data.replace("\r\n", "\n"))


def scene_at(revision, name="Main"):
    return parse(git_bytes("show", f"{revision}:Assets/Scenes/{name}.unity"))


def replace_once(doc, old, new):
    require(doc.count(old) == 1, f"Expected exactly one {old!r}")
    return doc.replace(old, new, 1)


def field(doc, key, value):
    old = maps.field(doc, key)
    return replace_once(doc, f"  {key}: {old}\n", f"  {key}: {value}\n")


def project_health_ui(scene, upstream):
    """Pure, fail-closed pre-health -> shared decorative health UI projection.

    Does not fetch refs, modify inputs, change map projections, or touch assets.
    Preserves every unlisted document verbatim; rejects unexpected health input
    layouts. CLI pins the complete baseline; parent gates retain their own pins.
    """
    local, remote = parse(scene.text()), parse(upstream.text())
    local.validate()
    old, incoming = local.subtree(CANVAS), remote.subtree(CANVAS)
    require(len(old) == 22 and len(incoming) == 26 and incoming - old == ADDED
            and not old - incoming, "Unexpected upstream health subtree")
    require(not ADDED.intersection(local.docs), "Incoming health ID collision")
    require({i for i in old if local.docs[i] != remote.docs[i]} == VISUAL,
            "Unexpected common health visual differences")
    # Prove the remote common deltas are exactly the requested upstream design.
    allowed = {1847001156: field(local.docs[1847001156], "m_IsActive", "0")}
    for ident in (1856098901, 2107953942):
        doc = field(local.docs[ident], "m_LocalScale", "{x: 0.5, y: 0.5, z: 1}")
        doc = field(doc, "m_AnchoredPosition", "{x: -16.5, y: -5}")
        allowed[ident] = field(doc, "m_SizeDelta", "{x: 33, y: -10}")
    allowed[1945700037] = replace_once(local.docs[1945700037],
        "  - {fileID: 1856098901}\n", "  - {fileID: 1856098901}\n  - {fileID: 720703111}\n")
    require(all(remote.docs[i] == d for i, d in allowed.items()), "Upstream layout drift")
    require(maps.ref(local.docs[CANVAS], "m_Father") == PARENT, "Canonical owner changed")
    require(maps.ref(local.docs[ECHO_CANVAS], "m_GameObject") == ECHO_GO
            and len(local.subtree(ECHO_CANVAS)) == 22, "Echo identity changed")
    for ident, slider, text in ((HEALTH[0], SLIDER, TEXT), (HEALTH[1], 1768936873, 317760695)):
        doc = local.docs[ident]
        require(maps.ref(doc, "healthSlider") == slider and maps.ref(doc, "healthText") == text
                and maps.field(doc, "storedCurrentHealth") == "0"
                and maps.field(doc, "storedMaxHealth") == "100", "Pre-health shared HP/binding drift")
    result = parse(local.text())
    for ident in sorted(VISUAL | ADDED):
        result.docs[ident], result.types[ident] = remote.docs[ident], remote.types[ident]
    result.docs[CANVAS] = field(result.docs[CANVAS], "m_Father", "{fileID: 0}")
    result.docs[PARENT] = replace_once(result.docs[PARENT], f"  - {{fileID: {CANVAS}}}\n", "")
    result.docs[maps.ROOT_ID] = replace_once(result.docs[maps.ROOT_ID],
        f"  - {{fileID: {HUD}}}\n", f"  - {{fileID: {CANVAS}}}\n  - {{fileID: {HUD}}}\n")
    result.docs[ECHO_GO] = field(result.docs[ECHO_GO], "m_IsActive", "0")
    for ident in HEALTH:
        result.docs[ident] = field(result.docs[ident], "healthSlider", f"{{fileID: {SLIDER}}}")
        result.docs[ident] = field(result.docs[ident], "healthText", f"{{fileID: {TEXT}}}")
    result.docs[SLIDER] = field(result.docs[SLIDER], "m_Interactable", "0")
    result.docs[SLIDER] = replace_once(result.docs[SLIDER], "    m_Mode: 3\n", "    m_Mode: 0\n")
    require({i for i in result.subtree(CANVAS) if "  m_RaycastTarget:" in result.docs[i]} == GRAPHICS,
            "Unexpected decorative graphics")
    for ident in GRAPHICS:
        result.docs[ident] = field(result.docs[ident], "m_RaycastTarget", "0")
    result.docs[2000288234] = field(result.docs[2000288234], "m_Enabled", "0")
    result.docs[maps.ROOT_ID] = result.docs.pop(maps.ROOT_ID)
    require({i for i in local.docs if local.docs[i] != result.docs[i]} == CHANGED,
            "Health projection changed unexpected documents")
    result.validate()
    return result


def compare_health_ui(actual, expected, name="scene"):
    """Validate structure and exact whole-document bytes/order, normalizing EOL."""
    def normalized(scene):
        return maps.Scene(scene.prefix.replace("\r\n", "\n"),
                          {i: d.replace("\r\n", "\n") for i, d in scene.docs.items()}, scene.types)
    actual, expected = normalized(actual), normalized(expected)
    require(actual.prefix == expected.prefix, f"{name}: YAML preamble drift")
    require(list(actual.docs) == list(expected.docs), f"{name}: document IDs/order drift")
    require(actual.types == expected.types, f"{name}: document types drift")
    changed = [i for i in expected.docs if actual.docs[i] != expected.docs[i]]
    require(not changed, f"{name}: health projection mismatch: {changed}")
    actual.validate()


def verify_artwork():
    """Verify PNG against upstream LFS content identity and exact importer bytes."""
    rows = []
    for path in (ART, ART + ".meta"):
        local = (ROOT / path).read_bytes()
        remote = git_bytes("show", f"{UPSTREAM}:{path}")
        pointer = re.fullmatch(rb"version https://git-lfs.github.com/spec/v1\noid sha256:([0-9a-f]{64})\nsize (\d+)\n", remote)
        digest = hashlib.sha256(local).hexdigest()
        if pointer:
            require(digest == pointer[1].decode() and len(local) == int(pointer[2]), "Upstream LFS artwork mismatch")
        else:
            require(local == remote, f"Upstream asset byte mismatch: {path}")
        rows.append((path, len(local), digest))
    require(b"guid: d8665a9e010ab704c8a5c6f1e6784dd7\n" in (ROOT / (ART + ".meta")).read_bytes(), "Sprite GUID drift")
    return rows


def self_test(expected, baseline, upstream):
    mutations = []
    def change(label, ident, key, value):
        mutations.append((label, ident, lambda d: field(d, key, value)))
    change("secondary active", ECHO_GO, "m_IsActive", "1")
    change("canonical hidden", 2000288232, "m_IsActive", "0")
    change("secondary slider reference", HEALTH[1], "healthSlider", "{fileID: 1768936873}")
    change("secondary text reference", HEALTH[1], "healthText", "{fileID: 317760695}")
    change("Material max HP", HEALTH[0], "storedMaxHealth", "101")
    change("Echo current HP", HEALTH[1], "storedCurrentHealth", "50")
    change("numeric text visible", 1847001156, "m_IsActive", "1")
    change("interactive slider", SLIDER, "m_Interactable", "1")
    mutations.append(("navigation", SLIDER, lambda d: replace_once(d, "    m_Mode: 0\n", "    m_Mode: 3\n")))
    change("raycaster", 2000288234, "m_Enabled", "1")
    for ident in GRAPHICS:
        change(f"graphic raycast {ident}", ident, "m_RaycastTarget", "1")
    change("frame layout", 720703111, "m_SizeDelta", "{x: 381, y: 250}")
    change("fill layout", 1856098901, "m_LocalScale", "{x: 1, y: 1, z: 1}")
    change("background layout", 2107953942, "m_AnchoredPosition", "{x: 0, y: 0}")
    change("canvas sorting", 2000288236, "m_SortingOrder", "1")
    change("canvas authored scale", CANVAS, "m_LocalScale", "{x: 1, y: 1, z: 1}")
    change("sprite reference", 720703112, "m_Sprite", "{fileID: 0}")
    for key, value in (("bossHealth", "599"), ("finaleStartTime", "479"),
                       ("timer", "29"), ("warningDuration", "4"), ("flipDuration", "0.9")):
        ids = [i for i, d in expected.docs.items() if f"  {key}:" in d]
        require(ids, f"Missing mutation fixture {key}")
        for ident in ids:
            change(key, ident, key, value)
    # Every retained doc is protected, not just selected health/gameplay fields.
    for ident in baseline.docs:
        mutations.append((f"unrelated bytes {ident}", ident, lambda d: d + "  healthUiUnexpected: 1\n"))
    for label, ident, mutate in mutations:
        broken = maps.Scene(expected.prefix, expected.docs.copy(), expected.types.copy())
        broken.docs[ident] = mutate(broken.docs[ident])
        try:
            compare_health_ui(broken, expected, label)
        except ValueError:
            continue
        raise ValueError(f"Mutation accepted: {label}")
    for label in ("roots behind HUD", "remove design", "extra doc", "preamble", "order"):
        broken = maps.Scene(expected.prefix, expected.docs.copy(), expected.types.copy())
        if label == "roots behind HUD":
            broken.docs[maps.ROOT_ID] = replace_once(broken.docs[maps.ROOT_ID],
                f"  - {{fileID: {CANVAS}}}\n  - {{fileID: {HUD}}}\n",
                f"  - {{fileID: {HUD}}}\n  - {{fileID: {CANVAS}}}\n")
        elif label == "remove design":
            del broken.docs[720703112]
        elif label == "extra doc":
            broken.docs[999999999999] = "--- !u!114 &999999999999\nMonoBehaviour:\n  m_Enabled: 1\n"
        elif label == "preamble":
            broken.prefix += "# unexpected\n"
        else:
            first = next(iter(broken.docs)); broken.docs[first] = broken.docs.pop(first)
        try:
            compare_health_ui(broken, expected, label)
        except ValueError:
            continue
        raise ValueError(f"Mutation accepted: {label}")
    before = baseline.text(), upstream.text()
    project_health_ui(baseline, upstream)
    require(before == (baseline.text(), upstream.text()), "Projection mutated input")
    compare_health_ui(parse(expected.text().replace("\n", "\r\n")), expected, "CRLF")
    print(f"Mutation rejection PASS: {len(mutations) + 5}; pure inputs and CRLF PASS")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    for path, size, digest in verify_artwork():
        print(f"Upstream asset PASS: {path}; bytes={size}; sha256={digest}")
    upstream = scene_at(UPSTREAM)
    planned = []
    for name in NAMES:
        path = ROOT / f"Assets/Scenes/{name}.unity"
        original = path.read_bytes()
        base = scene_at(BASE, name)
        expected = project_health_ui(base, upstream)
        if not args.apply:
            expected = starters.project_starters(expected, upstream)
        if args.self_test:
            self_test(expected, base, upstream)
        compare_health_ui(parse(original), base if args.apply else expected, name)
        newline = "\r\n" if b"\r\n" in original else "\n"
        output = expected.text().replace("\n", newline).encode("utf-8")
        planned.append((path, original, output))
        changed = sum(base.docs[i] != expected.docs[i] for i in base.docs)
        print(f"{name}: {len(base.docs)} -> {len(expected.docs)} docs; {changed} changed, "
              f"{len(expected.docs) - len(base.docs)} added; {len(base.docs) - changed} existing docs exact; "
              "shared subtree26; old Echo22 retained; explicit health/starter/timer projections only")
        print(f"{name}: before sha256={hashlib.sha256(original).hexdigest()}; projected sha256={hashlib.sha256(output).hexdigest()}")
    if args.apply:
        require(git_bytes("rev-parse", "HEAD").decode().strip() == BASE, "HEAD drift; no scene writes")
        require(all(p.read_bytes() == old for p, old, _ in planned), "Concurrent scene drift; no writes")
        for path, _, output in planned:
            path.write_bytes(output)
        print("Applied only Main/DebugRun exact health UI projection")
    else:
        print("PASS: both pinned scenes exact to composed health + starter/timer projections; no broad ignores")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, subprocess.CalledProcessError) as error:
        sys.exit(f"FAIL: {error}")
