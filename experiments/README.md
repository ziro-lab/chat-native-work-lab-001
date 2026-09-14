# Experiments

Each directory under this tree is an isolated experiment with its own goal, environment, assertions, PASS boundary, NOT PROVEN section and evidence.

Current seeds:

- `ymm4/plugin-host-validation` — minimal proof that a .NET 10 plugin built in GitHub Actions is loaded by the real YMM4 4.55.1.1 Lite host on a Windows runner.

Do not turn `experiments/` into a shared application framework. Promote helpers only after repeated cross-experiment use.
