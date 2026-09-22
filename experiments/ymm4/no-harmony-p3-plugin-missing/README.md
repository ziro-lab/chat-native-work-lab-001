# P3.5 — plugin-unavailable project preservation

Two-launch native probe.

1. With the subject IToolPlugin installed, save a project containing the real v1 FolderDocument in that plugin's ToolState.SavedState.
2. Stop YMM4 and physically remove the subject plugin.
3. Relaunch with only a controller plugin, open the saved project through public OpenProject(), and Save As a new project.
4. Compare raw project ToolStates and require the subject SavedState to survive exactly.

This proves host behavior when the folder plugin is unavailable; it does not rely on the subject plugin to preserve its own data during the second launch.
