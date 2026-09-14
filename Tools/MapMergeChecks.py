#!/usr/bin/env python3
"""Surgical map migration and compact checks; Python standard library only.

Default: validate fully wired scenes against pinned upstream 43a1348; no Library
    backup or package cache required. The agreed boundary field is boundaryRoot.
--baseline DIR: additionally prove byte-for-byte preservation of pre-edit scenes
    and reusable prefabs. Required for --migrate and --wire, never inferred.
--upstream REF: explicitly override the pinned reference for read-only checks.
--check-assets: also resolve map asset GUIDs in Assets/Packages/Library cache;
    run after Unity restores packages. Subasset import still needs Unity.
--analyze: read-only collision/hierarchy report.
--migrate: replace only the two old Content/Grid subtrees (backup required).
--wire --boundary-field NAME: finish a staged migration using the REAL runtime
    WorldMap.cs.meta GUID and its declared Transform/GameObject boundary field.
--allow-pending: explicitly check/stage maps without runtime wiring; never emit
    placeholder script GUIDs, components, or serialized field names.
--self-test: exercise rejection paths in memory, without changing scene files.

Raw YAML documents are retained, not round-tripped through a YAML serializer.
Structural comparisons tolerate checkout CRLF/LF conversion; preservation proof
and write preconditions compare exact bytes. Missing requested backups,
unexpected references, collisions, or concurrent edits fail closed.
"""

import argparse
from dataclasses import dataclass
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
SCENES = ROOT / "Assets/Scenes"

PIN = "43a1348b46530ec5abd254bbf3115dda7aac7610"
NAMES = ("Main", "DebugRun")
MAP_ROOTS = (1898056173, 1054028104)
MAP_GOS = (51126211, 1249588891)
MONOS = (4300134801, 4300134802)
WORLDS = (1546997187, 1627129257)
CONTENTS = (2027904951, 1068728230)
OLD_GRIDS = (1898056173, 1550107300)
ROOT_ID = 9223372036854775807
ACTIVE_SWITCH, SWITCH_MANAGER = 1787880877, 1787880878
LEGACY_SWITCHES = (1664862857, 1985488289)
SWITCH_LABELS = {787555867: 787555865, 756469090: 756469088}
SWITCH_GUID = "6951560efd6bb0a47bed9289a27408a1"
PREFABS = ("UpstreamRealWorldMap", "UpstreamMirrorWorldMap", "UpstreamMapBoundaries")
HEADER = re.compile(r"^--- !u!(\d+) &(-?\d+)(?: stripped)?\r?\n", re.M)
LOCAL_REF = re.compile(r"\{fileID: (-?\d+)\}")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read(path):
    return path.read_bytes().decode("utf-8")


def git(*args):
    return subprocess.check_output(["git", "--no-pager", *args], cwd=ROOT).decode("utf-8")


def field(doc, name):
    matches = re.findall(r"^  " + re.escape(name) + r": ([^\r\n]*)", doc, re.M)
    require(len(matches) == 1, f"Expected exactly one {name} field")
    return matches[0]


def ref(doc, name):
    match = LOCAL_REF.fullmatch(field(doc, name))
    require(match is not None, f"Expected local reference: {name}")
    return int(match[1])


def components(doc):
    return [int(i) for i in re.findall(r"^  - component: \{fileID: (\d+)\}", doc, re.M)]


def children(doc):
    return [int(i) for i in re.findall(r"^  - \{fileID: (\d+)\}", doc, re.M)]


@dataclass
class Scene:
    prefix: str
    docs: dict
    types: dict

    @classmethod
    def parse(cls, text):
        headers = list(HEADER.finditer(text))
        require(bool(headers), "No Unity YAML documents")
        docs, types = {}, {}
        for index, match in enumerate(headers):
            ident = int(match[2])
            require(ident not in docs, f"Duplicate document ID {ident}")
            end = headers[index + 1].start() if index + 1 < len(headers) else len(text)
            docs[ident] = text[match.start():end]
            types[ident] = int(match[1])
        return cls(text[:headers[0].start()], docs, types)

    def text(self):
        return self.prefix + "".join(self.docs.values())

    def subtree(self, transform):
        found = set()

        def visit(ident):
            require(ident not in found, f"Cycle/repeated transform {ident}")
            require(self.types.get(ident) in (4, 224), f"Invalid transform {ident}")
            go = ref(self.docs[ident], "m_GameObject")
            owned = components(self.docs[go])
            require(ident in owned, f"Transform {ident} not owned by {go}")
            found.update([go, *owned])
            for child in children(self.docs[ident]):
                visit(child)

        visit(transform)
        return found

    def validate(self):
        for ident, doc in self.docs.items():
            for target in LOCAL_REF.findall(doc):
                require(int(target) == 0 or int(target) in self.docs,
                        f"Document {ident}: missing local fileID {target}")
        transforms = {i for i, kind in self.types.items() if kind in (4, 224)}
        parents = {}
        for ident in transforms:
            doc = self.docs[ident]
            if " stripped\n" in doc or " stripped\r\n" in doc:
                instance = ref(doc, "m_PrefabInstance")
                match = re.search(r"m_TransformParent: \{fileID: (\d+)\}", self.docs[instance])
                require(match is not None, f"Stripped transform {ident}: missing prefab parent")
                parents[ident] = int(match[1])
                continue
            parents[ident] = ref(doc, "m_Father")
            kids = children(doc)
            require(len(kids) == len(set(kids)), f"Repeated child of {ident}")
            for child in kids:
                require(child in transforms, f"Non-transform child {child} of {ident}")
        for ident, parent in parents.items():
            require(parent == 0 or parent in transforms, f"Invalid parent of {ident}")
            if parent:
                require(ident in children(self.docs[parent]), f"Parent omits {ident}")
            for child in children(self.docs[ident]):
                require(parents.get(child) == ident, f"Nonreciprocal child {child} of {ident}")
            seen, cursor = set(), ident
            while cursor:
                require(cursor not in seen, f"Transform cycle at {cursor}")
                seen.add(cursor)
                cursor = parents[cursor]
        root_docs = [i for i, kind in self.types.items() if kind == 1660057539]
        require(root_docs == [ROOT_ID], "Unexpected SceneRoots document identity")
        roots = children(self.docs[ROOT_ID])
        require(len(roots) == len(set(roots)), "Repeated SceneRoots entry")
        require(set(roots) == {i for i, parent in parents.items() if not parent},
                "SceneRoots does not match root transforms")
        for ident, kind in self.types.items():
            doc = self.docs[ident]
            if kind == 1:
                owned = components(doc)
                require(len(owned) == len(set(owned)), f"Repeated component on {ident}")
                require(sum(c in transforms for c in owned) == 1, f"Invalid transform count on {ident}")
                for component in owned:
                    require(ref(self.docs[component], "m_GameObject") == ident,
                            f"Component {component} ownership mismatch")
            elif re.search(r"^  m_GameObject:", doc, re.M):
                go = ref(doc, "m_GameObject")
                require(self.types.get(go) == 1 and ident in components(self.docs[go]),
                        f"Orphan component {ident}")


def upstream(revision, check_assets=False):
    source = Scene.parse(git("show", f"{revision}:Assets/Scenes/Main.unity"))
    source.validate()
    ids = source.subtree(MAP_ROOTS[0]) | source.subtree(MAP_ROOTS[1])
    for transform, name in zip(MAP_ROOTS, ("Grid", "Bounding Box")):
        require(ref(source.docs[transform], "m_Father") == 0, f"Upstream {name} is not a root")
        require(field(source.docs[ref(source.docs[transform], "m_GameObject")], "m_Name") == name,
                f"Upstream root identity changed: {name}")
    require(children(source.docs[MAP_ROOTS[0]]) == [51126212, 1249588892],
            "Upstream Real/Mirror hierarchy changed")
    for go, name in zip(MAP_GOS, ("Real World", "Mirror World")):
        require(field(source.docs[go], "m_Name") == name, f"Upstream world identity changed: {go}")
    if check_assets:
        check_map_assets(source, ids)
    return source, ids


def check_map_assets(source, ids):
    # Opt-in: a clean checkout need not have Unity's ignored package cache yet.
    # Document comparison always protects GUIDs and sprite/tile subasset IDs.
    guids = set(re.findall(r"guid: ([0-9a-f]{32})", "".join(source.docs[i] for i in ids)))
    available = set()
    for asset_root in (ROOT / "Assets", ROOT / "Packages", ROOT / "Library/PackageCache"):
        for meta in asset_root.rglob("*.meta"):
            match = re.search(r"^guid: ([0-9a-f]{32})", read(meta), re.M)
            if match:
                available.add(match[1])
    missing = {g for g in guids - available if not g.startswith("0000000000000000")}
    require(not missing, f"Missing upstream asset GUIDs: {sorted(missing)}; restore Unity packages if needed")
    print(f"Incoming asset GUID existence: PASS ({len(guids)} GUIDs; subasset import not checked)")


def old_maps(scene):
    removed = set()
    for world, content, grid, name in zip(WORLDS, CONTENTS, OLD_GRIDS, ("MaterialWorld", "EchoWorld")):
        world_go = ref(scene.docs[world], "m_GameObject")
        require(field(scene.docs[world_go], "m_Name") == name, f"Unexpected World owner {world}")
        content_go = ref(scene.docs[world], "contentRoot")
        require(ref(scene.docs[content], "m_GameObject") == content_go, "Content reference changed")
        require(field(scene.docs[content_go], "m_Name") == "Content", "Unexpected Content name")
        parent = ref(scene.docs[content], "m_Father")
        require(ref(scene.docs[parent], "m_GameObject") == world_go, "Content is not a direct World child")
        require(ref(scene.docs[grid], "m_Father") == content, f"Old Grid {grid} is not under Content")
        ids = scene.subtree(grid)
        names = sorted(field(scene.docs[i], "m_Name") for i in ids if scene.types[i] == 1)
        require(names == ["Grid", "Tilemap"], f"Unexpected objects in old map subtree: {names}")
        require(not (removed & ids), "Old map subtrees overlap")
        removed |= ids
    return removed


def collision_check(local, ids, removed):
    collisions = (set(local.docs) - removed) & (ids | set(MONOS))
    require(not collisions, f"Refusing to overwrite retained local document IDs: {sorted(collisions)}")
    require(not (set(MONOS) & (set(local.docs) | ids)), "Reserved WorldMap IDs are not unused")


def runtime_contract(boundary_field):
    require(boundary_field is not None, "Final wiring needs --boundary-field with the runtime owner's agreed field")
    require(re.fullmatch(r"[A-Za-z_]\w*", boundary_field), "Invalid boundary field name")
    paths = list((ROOT / "Assets").rglob("WorldMap.cs"))
    require(len(paths) == 1, "Exactly one real WorldMap.cs is required before wiring")
    script = paths[0]
    meta = Path(str(script) + ".meta")
    require(meta.is_file(), "WorldMap.cs.meta has not been created by the runtime owner")
    guid = re.search(r"^guid: ([0-9a-f]{32})\r?$", read(meta), re.M)
    require(guid is not None and int(guid[1], 16) != 0, "Invalid WorldMap script GUID")
    code = re.sub(r"//[^\n]*|/\*.*?\*/", "", read(script), flags=re.S)
    require(re.search(r"class\s+WorldMap\s*:\s*MonoBehaviour\b", code), "Unexpected WorldMap class")
    declaration = re.search(r"\[SerializeField\]\s+private\s+(Transform|GameObject)\s+"
                            + re.escape(boundary_field) + r"\s*(?:;|=)", code)
    require(declaration is not None,
            "Boundary contract must be an actual [SerializeField] private Transform/GameObject field; "
            "review/extend this checker for other contracts, do not guess")
    worlds = list((ROOT / "Assets").rglob("World.cs"))
    require(len(worlds) == 1, "Exactly one World.cs required")
    world_code = re.sub(r"//[^\n]*|/\*.*?\*/", "", read(worlds[0]), flags=re.S)
    require(re.search(r"\[SerializeField\]\s+private\s+WorldMap\s+map\s*(?:;|=)", world_code),
            "World.cs does not yet declare serialized WorldMap map")
    boundary = 1054028104 if declaration[1] == "Transform" else 1054028103
    return guid[1], boundary_field, boundary


def map_game_object(doc, mono):
    require(doc.count("  m_Layer:") == 1, "Invalid map GameObject")
    newline = "\r\n" if "\r\n" in doc else "\n"
    return doc.replace("  m_Layer:", f"  - component: {{fileID: {mono}}}{newline}  m_Layer:")


def mono_document(ident, go, contract):
    guid, boundary_field, boundary = contract
    return (f"--- !u!114 &{ident}\nMonoBehaviour:\n"
            "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n"
            "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n"
            f"  m_GameObject: {{fileID: {go}}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
            f"  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}\n"
            "  m_Name: \n  m_EditorClassIdentifier: Assembly-CSharp::WorldMap\n"
            f"  {boundary_field}: {{fileID: {boundary}}}\n")


def build(before, source, ids, contract):
    removed = old_maps(before)
    collision_check(before, ids, removed)
    result = Scene.parse(before.text())
    for ident in removed:
        del result.docs[ident]
    for content, grid in zip(CONTENTS, OLD_GRIDS):
        pattern = rf"^  - \{{fileID: {grid}\}}\r?\n"
        result.docs[content], count = re.subn(pattern, "", result.docs[content], flags=re.M)
        require(count == 1, f"Content {content} must list old Grid once")
    # A retained reference to a replaced ID could silently change its meaning.
    # Only the Content child links above are allowed to refer into the old maps.
    for ident, doc in result.docs.items():
        stale = removed & {int(i) for i in LOCAL_REF.findall(doc)}
        require(not stale, f"Retained document {ident} references old map IDs {sorted(stale)}")
    root_doc = result.docs.pop(ROOT_ID)
    for ident, doc in source.docs.items():
        if ident in ids:
            result.docs[ident] = doc
    if contract:
        for world, go, mono in zip(WORLDS, MAP_GOS, MONOS):
            result.docs[go] = map_game_object(result.docs[go], mono)
            require(not re.search(r"^  map:", result.docs[world], re.M), "Backup already has map wiring")
            result.docs[world] += f"  map: {{fileID: {mono}}}\n"
            result.docs[mono] = mono_document(mono, go, contract)
    newline = "\r\n" if "\r\n" in root_doc else "\n"
    result.docs[ROOT_ID] = root_doc + "".join(f"  - {{fileID: {i}}}{newline}" for i in MAP_ROOTS)
    result = Scene.parse(result.text())
    result.validate()
    return result


def replace_timing_field(doc, key, value):
    field(doc, key)  # Reject missing/duplicate fields, preserving all other bytes.
    return re.sub(r"^(  " + re.escape(key) + r": )[^\r\n]*",
                  lambda m: m[1] + value, doc, flags=re.M)


def switch_controllers(scene):
    switches = {i for i, d in scene.docs.items()
                if "Assembly-CSharp::StateSwitchController" in d or f"guid: {SWITCH_GUID}," in d}
    require(switches == {ACTIVE_SWITCH, *LEGACY_SWITCHES}, "Changed switch controller identities")
    for ident in switches:
        doc = scene.docs[ident]
        require(scene.types[ident] == 114
                and field(doc, "m_EditorClassIdentifier") == "Assembly-CSharp::StateSwitchController"
                and field(doc, "m_Script") == f"{{fileID: 11500000, guid: {SWITCH_GUID}, type: 3}}",
                f"Switch GUID/class mismatch: {ident}")
        active = ident == ACTIVE_SWITCH
        require(field(doc, "m_Enabled") == ("1" if active else "0")
                and ref(doc, "worldManager") == (SWITCH_MANAGER if active else 0),
                f"Changed active/legacy switch binding: {ident}")
    manager = scene.docs[SWITCH_MANAGER]
    require(scene.types[SWITCH_MANAGER] == 114 and field(manager, "m_Enabled") == "1"
            and field(manager, "m_EditorClassIdentifier") == "Assembly-CSharp::WorldManager"
            and field(manager, "m_Script") == "{fileID: 11500000, guid: be0dea2d226413541af95dd2ba11b1a9, type: 3}"
            and ref(manager, "m_GameObject") == ref(scene.docs[ACTIVE_SWITCH], "m_GameObject"),
            "Active switch must belong to its enabled WorldManager")
    return switches


def switch_label(scene):
    found = set(SWITCH_LABELS) & set(scene.docs)
    require(len(found) == 1, "Expected exactly one scene-specific switch label")
    ident = found.pop()
    doc = scene.docs[ident]
    require(scene.types[ident] == 114 and ref(doc, "m_GameObject") == SWITCH_LABELS[ident]
            and field(doc, "m_EditorClassIdentifier") == "Unity.TextMeshPro::TMPro.TextMeshProUGUI",
            "Switch label TMP identity changed")
    return ident


def project_switch_interval(scene):
    """Exact historical 15 -> 30 active timer/label only; never skip whole documents."""
    switch_controllers(scene)
    require(field(scene.docs[ACTIVE_SWITCH], "timer") == "15", "Historical active timer must be exactly 15")
    label = switch_label(scene)
    require(field(scene.docs[label], "m_text") == '"15s Switch"', "Historical switch label must be exactly 15s Switch")
    result = Scene.parse(scene.text())
    result.docs[ACTIVE_SWITCH] = replace_timing_field(result.docs[ACTIVE_SWITCH], "timer", "30")
    result.docs[label] = replace_timing_field(result.docs[label], "m_text", '"30s Switch"')
    return result


def timings(scene, name, active_interval="30"):
    # Explicit historical mode is only for the old map migration/backup workflow.
    require(active_interval in ("15", "30"), "Unsupported timing contract")
    for ident in switch_controllers(scene):
        doc = scene.docs[ident]
        interval = active_interval if ident == ACTIVE_SWITCH else "15"
        for key, value in (("timer", interval), ("warningDuration", "5"), ("flipDuration", "0.8")):
            require(field(doc, key) == value, f"Changed local timing: {ident}.{key}")
    if active_interval == "30":
        require(field(scene.docs[switch_label(scene)], "m_text") == '"30s Switch"', "Changed switch label")
    finales = [d for d in scene.docs.values() if re.search(r"^  finaleStartTime:", d, re.M)]
    require(len(finales) == 1 and field(finales[0], "finaleStartTime") == "480", "Changed local finale")
    require(field(finales[0], "useSharedWaves") == ("1" if name == "DebugRun" else "0"),
            "Changed per-scene shared waves setting")


def validate_migrated(actual, source, ids, contract, name, report=True, active_interval="30"):
    """Checkout-safe acceptance without pretending to prove historical preservation."""
    actual.validate()
    expected = {i: source.docs[i] for i in ids}
    if contract:
        for go, mono in zip(MAP_GOS, MONOS):
            expected[go] = map_game_object(expected[go], mono)
            expected[mono] = mono_document(mono, go, contract)
    require(set(expected) <= set(actual.docs),
            f"{name}: missing migrated document IDs {sorted(set(expected) - set(actual.docs))}")
    changed = [i for i, doc in expected.items()
               if actual.docs[i].replace("\r\n", "\n") != doc.replace("\r\n", "\n")]
    require(not changed, f"{name}: map/wiring documents differ from pinned source/contract: {changed}")
    actual_map = actual.subtree(MAP_ROOTS[0]) | actual.subtree(MAP_ROOTS[1])
    require(actual_map == set(expected), f"{name}: unexpected map subtree documents")
    for kind in (156049354, 1839735485):
        require({i for i, t in actual.types.items() if t == kind}
                == {i for i in ids if source.types[i] == kind}, f"{name}: extra or missing Grid/Tilemap")
    map_components = {i for i, doc in actual.docs.items()
                      if re.search(r"^  m_EditorClassIdentifier: Assembly-CSharp::WorldMap\r?$", doc, re.M)
                      or (contract and f"guid: {contract[0]}" in doc)}
    require(map_components == (set(MONOS) if contract else set()), f"{name}: unexpected WorldMap components")
    for index, (world, content, mono, world_name) in enumerate(zip(
            WORLDS, CONTENTS, MONOS, ("MaterialWorld", "EchoWorld"))):
        require(actual.types.get(world) == 114 and actual.types.get(content) == 4,
                f"{name}: missing World/Content identity")
        world_doc = actual.docs[world]
        world_go = ref(world_doc, "m_GameObject")
        require(field(actual.docs[world_go], "m_Name") == world_name, f"{name}: wrong World owner")
        require(field(world_doc, "m_EditorClassIdentifier") == "Assembly-CSharp::World"
                and field(world_doc, "worldId") == str(index), f"{name}: wrong World identity")
        content_go = ref(world_doc, "contentRoot")
        require(ref(actual.docs[content], "m_GameObject") == content_go
                and field(actual.docs[content_go], "m_Name") == "Content", f"{name}: wrong Content binding")
        parent = ref(actual.docs[content], "m_Father")
        require(parent != 0 and ref(actual.docs[parent], "m_GameObject") == world_go,
                f"{name}: Content must remain a direct World child")
        if contract:
            require(ref(world_doc, "map") == mono, f"{name}: wrong World.map binding on {world}")
        else:
            require(not re.search(r"^  map:", world_doc, re.M), f"{name}: unexpected staged map wiring")
    timings(actual, name, active_interval)
    if report:
        print(f"{name}: {len(actual.docs)} docs; structural/map/wiring checks PASS; "
              f"{len(ids) - (2 if contract else 0)}/{len(ids)} upstream docs unchanged "
              "(checkout line endings ignored)")


def compare(actual, expected, before, source, ids, contract, name, report=True):
    actual.validate()
    require(actual.prefix == before.prefix, f"{name}: YAML preamble changed")
    require(set(actual.docs) == set(expected.docs),
            f"{name}: unexpected document identities; missing={sorted(set(expected.docs) - set(actual.docs))}, "
            f"extra={sorted(set(actual.docs) - set(expected.docs))}")
    changed = [i for i in expected.docs if actual.docs[i] != expected.docs[i]]
    require(not changed, f"{name}: unexpected document content: {changed}")
    exact = [i for i in ids if actual.docs[i] == source.docs[i]]
    require(len(exact) == len(ids) - (2 if contract else 0), "Unexpected upstream map document changes")
    timings(actual, name, field(expected.docs[ACTIVE_SWITCH], "timer"))
    if report:
        old, new = set(before.docs), set(actual.docs)
        altered = sorted(i for i in old & new if before.docs[i] != actual.docs[i])
        nonmap = sorted(i for i in old - old_maps(before) if before.docs[i] != actual.docs[i])
        print(f"{name}: {len(actual.docs)} docs; IDs +{len(new-old)}/-{len(old-new)}, "
              f"changed {len(altered)}; exact upstream {len(exact)}/{len(ids)}")
        print(f"  changed IDs: {altered}; retained nonmap changes: {nonmap}")
        print("  identities, local refs, component ownership, hierarchy, roots, map bytes, "
              "unrelated docs and exact expected active/legacy timing: PASS")


def self_test(before, source, ids, expected, contract):
    def rejects(label, action):
        try:
            action()
        except ValueError:
            return
        raise ValueError(f"Self-test did not reject: {label}")

    rejects("duplicate identity", lambda: Scene.parse(before.text() + before.docs[WORLDS[0]]))
    collision = Scene.parse(before.text())
    incoming = next(i for i in ids if i not in before.docs)
    collision.docs[incoming] = source.docs[incoming]
    rejects("retained ID collision", lambda: collision_check(collision, ids, old_maps(before)))
    reference = Scene.parse(before.text())
    reference.docs[WORLDS[0]] += f"  unexpectedMapRef: {{fileID: {OLD_GRIDS[0]}}}\n"
    rejects("stale ref to reused map ID", lambda: build(reference, source, ids, contract))
    broken = Scene.parse(expected.text())
    broken.docs[WORLDS[0]] += "  invalid: {fileID: 999999999999}\n"
    rejects("unresolved local reference", broken.validate)
    broken = Scene.parse(expected.text())
    broken.docs[MAP_ROOTS[0]] = broken.docs[MAP_ROOTS[0]].replace("  - {fileID: 51126212}\n", "")
    rejects("broken hierarchy", broken.validate)
    for ident, label in ((1982272475, "tile data"), (67443747, "boundary collider"),
                         (WORLDS[0], "unrelated World field"), (ROOT_ID, "root list")):
        broken = Scene.parse(expected.text())
        broken.docs[ident] += "  unexpected: 1\n"
        rejects(label, lambda: compare(broken, expected, before, source, ids, contract, "Main", False))
    print("Self-tests: 9 rejection cases PASS (no writes)")


def timing_mutations(scene):
    mutations = []
    for value in ("15", "29", "31"):
        mutations.append((ACTIVE_SWITCH, lambda d, v=value: replace_timing_field(d, "timer", v), "active interval " + value))
    mutations.extend([
        (ACTIVE_SWITCH, lambda d: d + "  timer: 30\n", "duplicate active interval"),
        (ACTIVE_SWITCH, lambda d: replace_timing_field(d, "worldManager", "{fileID: 0}"), "active manager binding"),
        (ACTIVE_SWITCH, lambda d: replace_timing_field(d, "m_Enabled", "0"), "active disabled"),
        (ACTIVE_SWITCH, lambda d: replace_timing_field(d, "m_EditorClassIdentifier", "Assembly-CSharp::OtherController"), "active class"),
    ])
    for ident in LEGACY_SWITCHES:
        mutations.append((ident, lambda d: replace_timing_field(d, "timer", "30"), "legacy interval"))
    for ident in (ACTIVE_SWITCH, *LEGACY_SWITCHES):
        for key, value in (("warningDuration", "6"), ("flipDuration", "0.9")):
            mutations.append((ident, lambda d, k=key, v=value: replace_timing_field(d, k, v), key))
    finale = next(i for i, d in scene.docs.items() if re.search(r"^  finaleStartTime:", d, re.M))
    mutations.append((finale, lambda d: replace_timing_field(d, "finaleStartTime", "481"), "finale"))
    for value in ('"15s Switch"', '"31s Switch"'):
        mutations.append((switch_label(scene), lambda d, v=value: replace_timing_field(d, "m_text", v), "switch label"))
    return mutations


def structural_self_test(actual, source, ids, contract, active_interval="30"):
    actual = Scene.parse(actual.text().replace("\r\n", "\n"))
    mutations = [
        (1982272475, lambda d: d + "  unexpected: 1\n", "tile data"),
        (67443747, lambda d: d + "  unexpected: 1\n", "boundary collider"),
        (MAP_GOS[0], lambda d: d.replace("Real World", "Renamed World"), "map GameObject"),
        (MAP_ROOTS[0], lambda d: d.replace("  - {fileID: 51126212}\n", ""), "hierarchy"),
        (ROOT_ID, lambda d: d.replace("  - {fileID: 1054028104}\n", ""), "scene root"),
    ]
    if contract:
        mutations.extend([
            (WORLDS[0], lambda d: d.replace(f"map: {{fileID: {MONOS[0]}}}",
                                          f"map: {{fileID: {MONOS[1]}}}"), "World.map"),
            (MONOS[0], lambda d: d.replace(f"{contract[1]}: {{fileID: {contract[2]}}}",
                                         f"{contract[1]}: {{fileID: {MAP_ROOTS[0]}}}"), "boundary binding"),
            (MONOS[0], lambda d: d.replace(contract[0], "1" * 32), "script GUID"),
        ])
    if active_interval == "30":
        mutations.extend(timing_mutations(actual))
    for ident, mutate, label in mutations:
        broken = Scene.parse(actual.text())
        broken.docs[ident] = mutate(broken.docs[ident])
        require(broken.docs[ident] != actual.docs[ident], f"Self-test mutation ineffective: {label}")
        try:
            validate_migrated(broken, source, ids, contract, "Main", False, active_interval)
        except ValueError:
            continue
        raise ValueError(f"Structural self-test did not reject: {label}")
    crlf = Scene.parse(actual.text().replace("\r\n", "\n").replace("\n", "\r\n"))
    validate_migrated(crlf, source, ids, contract, "Main", False, active_interval)
    print(f"Structural self-tests: {len(mutations)} rejection cases + CRLF checkout PASS (no writes)")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--analyze", action="store_true")
    mode.add_argument("--migrate", action="store_true")
    mode.add_argument("--wire", action="store_true")
    mode.add_argument("--candidate", type=Path, help="Read-only identity/reference/hierarchy check of a merged scene; no pinned map comparison")
    parser.add_argument("--allow-pending", action="store_true")
    parser.add_argument("--boundary-field", default="boundaryRoot")
    parser.add_argument("--baseline", type=Path, help="Pre-edit backup directory, relative to repository root")
    parser.add_argument("--upstream", default=PIN, help="Read-only reference override (default: pinned 43a1348)")
    parser.add_argument("--check-assets", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.candidate is not None:
        candidate = Scene.parse(read(ROOT / args.candidate))
        candidate.validate()
        print(f"Candidate structural check PASS: {len(candidate.docs)} documents; gameplay/import not validated")
        return
    writing = args.migrate or args.wire
    require(not writing or args.baseline is not None, "Write operations require an explicit --baseline backup")
    if args.baseline is not None:
        args.baseline = ROOT / args.baseline
        for filename in [f"{n}.unity" for n in NAMES] + [f"{p}.prefab" for p in PREFABS]:
            require((args.baseline / filename).is_file(), f"Requested baseline missing: {args.baseline / filename}")
        print(f"Preservation baseline: {args.baseline}")
    else:
        print("Structural validation only: historical preservation not checked (supply --baseline for proof)")
    revision = git("rev-parse", "--verify", f"{args.upstream}^{{commit}}").strip()
    if writing:
        require(revision == PIN, "Write operations are pinned to upstream 43a1348")
        require(git("branch", "--show-current").strip() == "WIP-Chen-dual-world", "Not on the approved personal branch")
    source, ids = upstream(revision, args.check_assets)
    print(f"Upstream {revision[:12]}: {len(ids)} map documents; roots {MAP_ROOTS}")
    contract = None if args.allow_pending else runtime_contract(args.boundary_field)
    if contract:
        print(f"WorldMap GUID {contract[0]}; {contract[1]} -> {contract[2]}; component IDs {MONOS}")
    require(not args.wire or contract, "--wire requires the confirmed runtime contract")
    originals, outputs = {}, {}
    if args.baseline is not None:
        for prefab in PREFABS:
            path = SCENES / f"{prefab}.prefab"
            require(path.read_bytes() == (args.baseline / path.name).read_bytes(), f"Reusable prefab changed: {path.name}")
        print("Reusable prefabs: byte-for-byte preservation PASS")
    for name in NAMES:
        path = SCENES / f"{name}.unity"
        original = path.read_bytes()
        actual = Scene.parse(original.decode("utf-8"))
        before = expected = None
        if args.baseline is not None:
            before = Scene.parse(read(args.baseline / path.name))
            before.validate()
            removed = old_maps(before)
            collision_check(before, ids, removed)
            print(f"{name}: replace {len(removed)} old map docs; safe overlapping IDs {sorted(ids & removed)}; "
                  "retained collisions: none")
            if args.analyze:
                continue
            expected = build(before, source, ids, contract)
            if args.migrate:
                require(original == (args.baseline / path.name).read_bytes(),
                        f"{name}: current scene differs from backup; refusing to overwrite new user edits")
            elif args.wire:
                staged = build(before, source, ids, None)
                compare(actual, staged, before, source, ids, None, name, False)
            else:
                compare(actual, expected, before, source, ids, contract, name)
        candidate = expected if writing else actual
        active_interval = field(expected.docs[ACTIVE_SWITCH], "timer") if writing else "30"
        validate_migrated(candidate, source, ids, contract, name, active_interval=active_interval)
        if writing:
            compare(candidate, expected, before, source, ids, contract, name)
            originals[path], outputs[path] = original, candidate.text().encode("utf-8")
        if args.self_test and name == "Main":
            structural_self_test(candidate, source, ids, contract, active_interval)
            if before is not None:
                self_test(before, source, ids, expected, contract)
    # Validate both complete candidates BEFORE writing either. Recheck all input
    # bytes first to avoid overwriting concurrent scene work by another agent.
    for path, original in originals.items():
        require(path.read_bytes() == original, f"Concurrent edit: {path.name}; no writes performed")
    for path, output in outputs.items():
        path.write_bytes(output)
        require(path.read_bytes() == output, f"Write verification failed: {path.name}; backup in {args.baseline}")
    if not args.analyze:
        print("WIRED STATIC CHECK PASS; Unity import/Play Mode still required" if contract else
              "STAGED MAP CHECK PASS; World.map / WorldMap wiring still PENDING (not runtime-ready)")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
