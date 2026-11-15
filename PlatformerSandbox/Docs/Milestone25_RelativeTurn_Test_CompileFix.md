# Milestone 25 relative-turn test compilation fix

Replaces only `PlatformerSandbox.Tests/Simulation/CharacterMotor/VerticalElevatorRelativeTurnRegressionTests.cs`.

The original test used `HorizontalDirection.Neutral` in two places without importing `PlatformerSandbox.Simulation.Motion.Profiles`, the namespace defining `HorizontalDirection`.

Added the missing `using` directive. No test assertions or production source files have been changed.

Rebuild and rerun the complete test suite in Visual Studio.
