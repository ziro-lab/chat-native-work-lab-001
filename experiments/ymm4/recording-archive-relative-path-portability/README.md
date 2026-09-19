# Recording Archive relative-path portability

## Question

Does YMM4 v4.56.1.0 preserve and resolve a VideoItem relative FilePath against the project location such that a project folder can be moved as a unit?

## Method

A real-host probe creates a disposable project under folder A with a valid synthetic MP4 at `recordings/clip.mp4`, assigns the VideoItem FilePath as that relative string, saves the project, copies the whole folder to B, removes A's media file, and loads B's copied `.ymmp`.

The probe records:

- whether SaveProject preserves or normalizes the relative string;
- whether LoadProjectFile from B preserves or normalizes it;
- the loaded Project.FilePath;
- whether the moved VideoItem can still report a positive ContentLength when only B contains the media;
- the path reported by VideoItem.GetFiles().

This experiment is observational. Unsupported relative paths are a valid result; the workflow passes when the behavior is captured deterministically.

## PASS boundary

A PASS means the exact host behavior was observed and recorded. It does not assume relative-path portability unless the evidence says the moved media is actually resolved.

## Result

YMM4 preserves the relative string `recordings\clip.mp4`, but does **not** resolve it against the moved project directory in the tested paths. After copying A → B, deleting A's media, and loading B's project, the VideoItem still carried the relative string but `ContentLength` remained zero. The live OpenProject companion observation produced the same result.

Therefore simple relative-FilePath rewriting is not a proven portable-archive mechanism on v4.56.1.0.

## Evidence

- Exact host: YMM4 Lite 4.56.1.0
- Official ZIP SHA256: `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`
- Final workflow run: `35431031566`
- Native boundaries job: `105865515319`
- Native boundaries artifact: `10580747697`
- Artifact SHA256: `ef5a33b26b5a5f88d922b563aadbf89ad5d8897662e5b984e3da7e4066b0f2e4`
