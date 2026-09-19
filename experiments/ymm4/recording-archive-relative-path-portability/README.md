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
