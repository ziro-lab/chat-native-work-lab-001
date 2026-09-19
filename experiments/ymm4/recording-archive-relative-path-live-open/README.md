# Recording Archive relative path — live OpenProject portability

## Question

Even though detached LoadProjectFile keeps the original Project.FilePath, does the normal live YMM4 OpenProject(string) path rebase a relative VideoItem FilePath to the newly opened project folder after the whole folder is moved?

## Method

The probe saves a project in A with VideoItem.FilePath=`recordings/clip.mp4`, copies the full folder to B, deletes A's media, then opens B/project.ymmp through the real MainViewModel OpenProject(string) entry point used by YMM4.

It waits for the live project path to switch to B and observes the active VideoItem FilePath and ContentLength.

## PASS boundary

PASS records actual live-open behavior. Relative-path portability is supported only if the B-opened item resolves the moved media without A being present.
