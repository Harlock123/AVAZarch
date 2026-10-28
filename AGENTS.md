# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game and its controls;
this file covers how the code is put together and the rules that keep it working.

## What this is

A lander shooter in the style of David Braben's 1987 Acorn Archimedes game Zarch (Virus on other machines), in
C# on .NET 10 and Avalonia 12. The flat-shaded 3D landscape and lander are projected and drawn in code with
Avalonia's `DrawingContext`, with no 3D library. One self-contained executable per platform (Windows, macOS and
Linux, x64 and ARM). The sounds and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAZarch.slnx                        # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAZarch.slnx                         # every test runs headless: no window opens
dotnet run --project src/AVAZarch                 # play it; --no-audio for silence
ZARCH_SHOTS=$PWD/Images dotnet test tests/AVAZarch.Tests --filter ScreenshotTests   # refresh the README's pictures
ZARCH_PROBE=1 dotnet test tests/AVAZarch.Tests --filter PilotProbe   # how the computer pilot gets on
python3 tools/make_sounds.py                      # rebuild src/AVAZarch/Sounds (standard-library Python only)
```

`ZARCH_PROBE` takes `1` to start from the first wave, or a wave number to start there. `ZARCH_SEEDS` picks the
games (default `1,2,3`).

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings for
every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAZarch/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores), the waves and the fixed-step loop.
  - `Sim.cs` is the rules, one 60 Hz step at a time: the lander's flight (gravity, inertia, thrust, fuel,
    landing and crashing), shots, the aliens and what they drop, and the virus spreading over the land.
  - `World.cs` has `Controls`, the game's measures (`Field`) and everything in a world.
  - `Land.cs` has `Vec3` and the wrapping 64 by 64 landscape: hills, sea, beaches, the pad and the virus.
  - `View.cs` is the camera, behind and above the lander and always facing north.
  - `Pilot.cs` is the computer player, flying with the lean and the engine like a player. The demonstration and
    the tests use it.
  - `InputHandler.cs` (keys, mouse) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAZarch/Views/`: `GameCanvas` (split over partial files: world, radar, overlays) draws the fixed
  1024x768 frame and runs the 60 Hz timer. Behind the title it runs a world of its own, flown by a `Pilot`.
- `src/AVAZarch/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAZarch.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `Harness.cs` sets up worlds and games.
  - The tests are split by subject: flight, combat, land, view, pilot, the game round it, and input.
  - `ScreenshotTests.cs` draws the README's pictures. `ShiftReleaseTests.cs` covers the Shift key quirk below.
  - `PilotProbe.cs` is a tuning probe that does nothing unless `ZARCH_PROBE` is set.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances the game in fixed 1/60 s steps (`Field.Step`, `Sim.Step`),
  independent of the frame rate, so a seed always plays the same game. Draw random numbers from the world's
  seeded `w.Rng` (set by `Sim.Start` from the game's seed), never from `Random.Shared`. The land is built from
  its own `Random(seed)`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** The lean is `Controls.Tx` (east is +1) and `Controls.Tz` (north, up the
  screen and into it, is +1), each from -1 to 1. This is the opposite of the screen's y axis, so the stick's Y is
  negated, and the mouse's lean comes from where the pointer is. `InvertY` swaps up and down. `InputTests` drives
  the keys and mouse through the handler; test new controls the same way.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock (Shift is an engine key here). `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder
  (`SpecialFolder.ApplicationData/AVAZarch`). Tests load the table from a temporary folder
  (`Harness.TempFolder()`) and set `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features and controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site (Harlock123.github.io) to rebuild, if `SITE_REFRESH_TOKEN` is set.
