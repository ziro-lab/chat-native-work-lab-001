# P4.2b — metadata-only YMM4 history boundary

Focused V1 question:

Can a FolderDocument-only mutation be registered as one YMM4 UndoRedoManager transaction, mark the project unsaved, and round-trip through real Ctrl+Z / Ctrl+Y without changing Timeline content?

The probe uses the real pure P4 ToggleCollapsed command as the metadata mutation.

If green, the product can wrap create / rename / collapse / ungroup behind one small folder-history adapter instead of inventing a separate dirty-state mechanism.
