# Recording Archive Native Spine — Discovery Evidence

Date: 2026-09-16  
Pinned host: **YMM4 v4.56.1.0 Lite**  
Official ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`

Source head for the final discovery runs: `5fe6ce80c814fb976befa89f2972fdf373a20c97`.

## 1. VideoItem archive surface — PASS

Workflow run: `35101418600`  
Job: `104811727136`  
Artifact: `10448840401` (`ymm4-videoitem-archive-surface`)  
Artifact ZIP SHA256: `3187703b70d9ad9b1e807d53a22a4c2816097ad53b9b19cb0fee3a22614eca1d`

Result:

```text
status=PASS_VIDEOITEM_ARCHIVE_SURFACE
all_required_members=True
public_readable_required_count=5
safe_parameterless_instance=True
```

Observed on native `YukkuriMovieMaker.Project.Items.VideoItem` / `BaseItem`:

- `FilePath : string` — public get/set
- `ContentOffset : TimeSpan` — public get/set
- `Frame : int` — public get/set
- `Length : int` — public get/set
- `PlaybackRate : double` — public get/set, default `100`
- `ContentLength : TimeSpan` — public get
- `OriginalContentLength : TimeSpan` — public get
- `PlaybackRate2 : Animation` — public get
- `ReplaceFile(string from, string to)` — public
- public parameterless and `VideoItem(string)` constructors exist

**Decision:** the recording-archive product does not need reflection merely to snapshot the core VideoItem archive-planning inputs or to replace a file reference. Exact source-time math is still a separate behavioral question, especially around `PlaybackRate2` / any non-constant rate semantics.

## 2. Scene archive surface — PASS

Workflow run: `35101418468`  
Job: `104811855182`  
Artifact: `10448900957` (`ymm4-scene-archive-surface`)  
Artifact ZIP SHA256: `ebcd8e7ddb1a621c2bec76578c2a54d0b58fc6c16684620b38f5d543219c28a2`

Result:

```text
status=PASS_SCENE_ARCHIVE_SURFACE
collection_candidate_count=9
scene_item_type=True
scene_item_reference_candidate_count=8
```

Important public surface observed:

- `MainModel.Scenes : Scenes`
- `Scenes.Timelines : ImmutableList<Timeline>` — public get/set
- `Scenes.AllScenes : IEnumerable<Scene>` — public get
- `Scenes.AddScene(Timeline)` — public
- `Scenes.DeleteScene(Timeline)` — public
- `Scenes.ClearScenes()` — public
- `Scenes.CreateScene(...)` — public
- `MainModel.Timeline : Timeline` — public get
- `MainModel.SelectScene(Timeline)` — public
- `MainModel.CreateNewScene()` — public
- native `SceneItem` exists
- `SceneItem.SceneId : Guid` — public get/set
- `Scene.ParentScenes : Guid[]` — public get
- `Scene.Scenes : Scenes` / `Scene.Timeline : Timeline` — public get

**Decision:** selected-scene enumeration and a SceneItem-based dependency graph can be designed primarily around public native model surfaces. A follow-up behavioral experiment must still prove the exact dependency closure and safe removal/save roundtrip; discovery alone is not permission to delete scenes.

## 3. Project archive save surface — PASS

Workflow run: `35101418434`  
Job: `104811529831`  
Artifact: `10448029417` (`ymm4-project-save-archive-surface`)  
Artifact ZIP SHA256: `d7936eff99a39d22832cbbd7d2df994f9b06e4864ee4b5d664b343219e3fb20f`

Result:

```text
status=PASS_PROJECT_ARCHIVE_SURFACE
save_copy_export_candidate_count=31
project_file_path_candidate_count=60
```

Important public surface observed:

### MainViewModel

- `ProjectFilePath` — public read-only reactive property
- `SaveFileDialogViewModel` / `OpenFileDialogViewModel` — public
- `SaveProject(string file)` — public
- `OpenProject(string file)` — public
- `CreateProject()` — public
- `KeepProjectPath : bool` — public get/set

### MainModel

- `ProjectFilePath : string` — public get
- `IsProjectFileSaved : bool` — public get
- `SaveProject(string file)` — public
- `LoadProjectFile(string file) -> Task<Project>` — public
- `OpenProjectAsync(string filePath, Project project, ProgressMessage)` — public
- `ChangeProjectPath(string path)` — public

**Decision:** a native save/copy route is plausible and should be tested before falling back to direct `.ymmp` JSON rewriting. The presence of `SaveProject(string)` plus `KeepProjectPath` is especially promising, but a behavioral roundtrip must prove whether an archive copy can be written without changing the active source Project identity/path.

## Discovery conclusion

All three discovery experiments passed against the exact pinned YMM4 host. The major integration surfaces required by the recording-archive MVP are present, and more of them are public than expected.

The next Lab questions should be behavioral, not additional broad reflection discovery:

1. **Source-time mapping:** prove constant 50% / 100% / 200% playback + nonzero `ContentOffset` mapping and identify whether `PlaybackRate2` must block the MVP.
2. **Save-copy roundtrip:** save source A, create archive copy B, verify A remains unchanged, verify active `ProjectFilePath` semantics, and reload B.
3. **Scene dependency roundtrip:** create at least two scenes plus a `SceneItem`, prove exact `SceneId` dependency closure, remove an unrelated scene in a disposable copy, save/reload and verify retained scenes.

Do not treat these discovery PASS results as proof of the three behavioral claims above.
