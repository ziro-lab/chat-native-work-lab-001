namespace Ymm4TemplateCloneProbe;

// Compile-only sentinel. YMM4's real Project.MainModel is internal in 4.55.1.1 and is
// still discovered by the assembly scan in Plugin.cs (run #2 already recorded
// AddTemplateItemAsync). This local type prevents a direct compile-time dependency
// while the probe continues with ItemTemplate and effect-fidelity observations.
internal sealed class MainModel { }
