# Shared health UI validation

## Scope and pins

- Local base/unchanged HEAD: `e7e150f5d91c72f8f5e65187391570f7e9022a0f`.
- Upstream Main: `486e9f3c100018048b033e54687188bbceef1292` (local `origin/main` verified).
- Production writes: only `Assets/Scenes/Main.unity` and `Assets/Scenes/DebugRun.unity`, via raw-document text projection. Neither source scene was opened or saved by the validation runner in Unity.
- New files: `Tools/HealthUiMergeChecks.py`, `Tools/HealthUiPlayChecks.cs`, `Tools/HealthUiPlayChecksBatch.cs`, `Tools/Run-HealthUiChecks.ps1`, this handoff.
- No runtime/weapon code, assets, packages, map inheritance, inventory, Buff/audio, boss600 or 30/5/.8/480 changes. No commits.
- `MainUiMergeChecks.py` now composes this pinned health projection with its existing HUD/gameplay projection; `MainBaselineChecks.py` consumes that result. Both existing static gates pass with their self-tests, without broad document exclusions.

| Scene | Before documents | After | Existing changed | Added | Existing exact |
|---|---:|---:|---:|---:|---:|
| Main | 586 | 590 | 14 | 4 | 572 |
| DebugRun | 588 | 592 | 14 | 4 | 574 |

The shared health subtree is 26 documents (previously 22). Upstream common visual changes are exactly `1847001156`, `1856098901`, `1945700037`, `2107953942`. New frame documents are `720703110/111/112/113`; only the new Image raycast flag differs from upstream.

Canonical canvas transform `2000288233` is removed from Material transform `5576024` and inserted as a SceneRoot immediately before HUD `179380105`. Canvas/Scaler/sorting and its authored transform values otherwise remain unchanged; native Unity sets its overlay transform. Both PlayerHealth components (`5576027`, `1488475399`) bind Slider `1945700038` and hidden TMP `1847001157`. Authored current storage remains 0, max100; production initialization gives shared 100/100.

Echo canvas transform `2621169`, GameObject `2621168`, remains under its original owner. Only its GameObject active flag is disabled. Its entire 22-document subtree remains, including the unused Slider/Text and their references. No assets deleted. Canonical Slider is noninteractive/navigation None; all four shared graphics are non-raycast; canonical GraphicRaycaster disabled.

## Exact projection API for existing gates

```python
import HealthUiMergeChecks as health

# Both arguments and result are MapMergeChecks.Scene instances.
# scene is the parent's PRE-health local expected scene, not the edited scene.
# upstream is Main parsed from the pinned UPSTREAM revision.
expected = health.project_health_ui(scene, upstream)
health.compare_health_ui(actual, expected, name="Main")
```

- `project_health_ui(scene, upstream) -> MapMergeChecks.Scene`: pure, non-idempotent, fails closed on unexpected pre-health inputs. Keeps all unrelated documents and caller-owned map/assets/gameplay projections; no Git, disk, or asset writes inside the function. Does not replace the parent's existing inheritance rules.
- `compare_health_ui(actual, expected, name="scene") -> None`: whole-document bytes, document IDs/order/types, YAML prefix and structural references/ownership/roots. Normalizes checkout CRLF/LF only; raises `ValueError` on drift.
- `scene_at(revision, name="Main") -> MapMergeChecks.Scene`: loads from local Git objects; no fetch/ref changes.
- `parse(data: bytes | str) -> MapMergeChecks.Scene`: raw-document parser with checkout EOL normalization.
- `verify_artwork() -> list[(path, byte_count, sha256)]`: exact importer comparison and upstream LFS content identity check; no copying/writes.

Read-only gate: `python -B Tools/HealthUiMergeChecks.py --self-test`.
The one-time `--apply` mode requires BOTH scene inputs still equal the pinned base and HEAD still equals BASE before writing only those scene files; it preserves checkout newline convention. Do not reapply to already-projected scenes.

Latest read-only run passed: 623 mutation rejections for Main and 625 for DebugRun (1,248 total), plus input purity and CRLF checks. Tests reject unrelated bytes in every original document; secondary activation; either health binding; HP; timing/boss/layout; frame asset; sorting/root order; interactive/navigation/raycast flags; missing/extra/reordered documents and prefix changes.

`git diff --check` reports one trailing-space `m_Name: ` line per scene in new Image document `720703112`. This is deliberately retained from the pinned upstream raw document, not a Unity reserialization; no other whitespace issue was reported.

## Source hashes

SHA-256 of exact checkout bytes:

| File | Before projection | After projection |
|---|---|---|
| Main.unity | `cf657a7910d7a61ddaf00f4809a7fd12b2884a6be12527c6e7f16b3a2ba87b79` | `467b05cf20129cc78461199c19b57054b10a129b67de1b8d29109196c1985abe` |
| DebugRun.unity | `b2fada132a7562bece1be7500631807f5b21f87da6aa9309fd96c4564caa93b1` | `13289ba158fcc598929dcbd16400ba816d83d9895fbbd41ed347e693afeeb4be` |

Existing `Assets/Vampire Survival Assets/Art/Hp bar.png` matches upstream LFS OID/size, not the SHA of the Git pointer blob:

- PNG: 2,776 bytes, `0f375c8638974fe60bb2bb7d7af1975150d76c29c70297b251ece7bdf2c0c65c`.
- Meta: 3,629 bytes, `b171e7b8f106d041b0b2f206462980021ddbed380fcaaed2140d8b68477c2c45`, exact remote bytes.
- Native imported Sprite: GUID `d8665a9e010ab704c8a5c6f1e6784dd7`, fileID `21300000`, texture `280x160`.

All 2,319 protected source file SHA-256/Windows attribute records are identical from the pre-native preparation snapshot through the final launch. Coverage includes Assets (dirty fonts/RP/global settings/Web profiles), all ProjectSettings, Packages, Data, `.vscode`, `.vsconfig`. No new source files appeared in those trees. Existing source editor was not stopped or controlled. Exact inventories:

- `Library/DebugRunValidationProject/Evidence/HealthUiChecks/attempt-01/source-before.json`
- `Library/DebugRunValidationProject/Evidence/HealthUiChecks/attempt-03/source-after.json`

Selected dirty font hashes, unchanged:

- `Kenney Pixel SDF.asset`: `5828dc9741cdc27676c29ab3f1f81681c3117f69b5d93bfe71d13b0594689e73`.
- `Kenney Pixel SDF - Outline.asset`: `65abf53d7772e2cf22b450c6d9a21eadf10452bbc5240a9aefb052ba021acd93`.

## Validation status: partial, NOT a clean native pass

Unity: `E:/Unity/Editor/6000.4.6f1/Editor/Unity.exe`. Only exact warmed `Library/DebugRunValidationProject` used. One scene per graphical launch; 270-second external bound, no timeouts, no killed processes, no automatic retries. Two-native-launch budget exhausted. Test stages removed after confirmed isolated exit. Old FiringFlip stage preserved outside Assets in attempt-01 evidence.

| Evidence attempt | Mode | Result |
|---|---|---|
| attempt-01 | Offline/preparation only | Roslyn passed: 78 runtime sources; 7 production editor + 2 new harness sources |
| attempt-02 | Main AssetDatabase + Play; 56.61s, PID45468 | 1,940 passed, 1 failed; 7 scenario groups complete. First-frame GameOver Restart raycast assertion failed after successful reload. One SearchDatabase startup exception; exit2 |
| attempt-03 | DebugRun AssetDatabase + Play; 43.06s, PID5228 | 1,945 passed, 0 failed, all 8 groups complete. One SearchDatabase startup exception; exit3 (explicitly NOT clean) |

Both native attempts recompiled offline successfully first. Both resolved 506 native dependency file/meta hashes identical to source; no compiler/import/missing-script or unexpected gameplay errors reported. Startup audit includes the known SearchDatabase exception rather than suppressing it.

Main completed actual full100/half50 screenshots; Echo switch inherited50 and damage reduced BOTH heroes/one Slider to40; unchanged overlay screen geometry during real camera capture/blur/flip; both heroes active in fusion with one live health canvas; settled fusion; Victory sorting/first-hit button raycasts; native pointer Restart; fresh unfused/unpaused reload at100.

Main's immediate defeat check ran before a rendered-panel capture. The harness now captures the newly enabled result panel before testing pointer geometry and records all hits. DebugRun passed this settled check for Restart/MainMenu/Quit, with each first hit on the correct button's TMP child, and no health subtree hits. This supports a first-frame test timing explanation but Main was NOT rerun with the corrected harness. Main's full GameOver subtree remains byte-for-byte pinned-base-identical. Do not report the original Main assertion as a pass.

DebugRun additionally completed all the same Play scenarios and the stable defeat screenshot/raycast group. This exceeds its minimum native preflight requirement without cross-scene loading (only same-scene restart).

Exclusions: no player builds or Web build validation. Existing isolated logs record `Native extension for WebGL target not found`; the NEW Web Build Profiles folder/meta were moved out of isolated Assets only, with copies retained in each attempt's evidence. Source profiles/settings unchanged. Warm isolated packages were retained instead of copying dirty source package configuration; an optional editor helper was excluded from isolated/offline compilation. The current runner instead mirrors source Packages and includes all source editor scripts; no new native run is claimed for that updated configuration. See each attempt's `validation-exclusions.json`, `prior-build-target-failure-logs.txt`, `runner-result.json`, `log-audit.json`, and archived `executed-tools` for exact executed versions. No source font/importer/attribute workarounds were applied.

## Actual visual evidence

All screenshots are graphical Unity GameView captures at `683x384`, not authoring previews or synthesized mockups. Paths are relative to the project root:

- Main full HP: `Library/DebugRunValidationProject/Evidence/HealthUiChecks/attempt-02/main-full-hp.png`
- Main half HP: `Library/DebugRunValidationProject/Evidence/HealthUiChecks/attempt-02/main-half-hp.png`
- Health sharp while world is blurred/flipping: `Library/DebugRunValidationProject/Evidence/HealthUiChecks/attempt-02/main-flip-health-untransformed.png`
- Echo shared damage: `Library/DebugRunValidationProject/Evidence/HealthUiChecks/attempt-02/echo-shared-damage.png`
- Fusion settled, one health bar: `Library/DebugRunValidationProject/Evidence/HealthUiChecks/attempt-02/fusion-settled-one-health-bar.png`
- Victory above health: `Library/DebugRunValidationProject/Evidence/HealthUiChecks/attempt-02/victory-above-health.png`
- Reload full HP: `Library/DebugRunValidationProject/Evidence/HealthUiChecks/attempt-02/reloaded-full-health.png`
- DebugRun full/half: `Library/DebugRunValidationProject/Evidence/HealthUiChecks/attempt-03/debugrun-full-hp.png`, `debugrun-half-hp.png`
- DebugRun defeat above health: `Library/DebugRunValidationProject/Evidence/HealthUiChecks/attempt-03/defeat-above-health.png`

The imported frame's native screen bounds are `(13.87,300.50)..(122.02,371.64)`; health stays outside world capture, no canvas rotation/blur/flip inheritance, no numeric text, and one active health canvas through fusion. Frame and fill use exact upstream layout; root overlay/Scaler remain authored and Unity supplies effective scale (~0.36 at this GameView size).
