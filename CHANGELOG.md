# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

<!--
Note: Spacedock's markdown doesn't recognize lists using `-`, so make sure to
      use `*` for all list entries.
-->

## Unreleased
### Changed
* Rewrote the code that computes when a vessel will next enter a planet's shadow.
  This should now work in vastly more cases.
* Updated day/night simulation to be applied to orbiting vessels by default.

### Fixed
* Fixed a solver crash when a vessel with resource constraints crosses a SOI boundary.
* Fixed incorrect AoA calculation for solar panels.

## 0.1.18
### Added
* Resources mined in the background will now count for ISRU contracts.

### Fixed
* Fixed another nullref exception when recording vessel state.

## 0.1.17
### Fixed
* Ensured `BackgroundResourceProcessing.dll` is actually included in the release.

## 0.1.16
### Changed
* KSPBurst-Full is now marked as a recommended dependency.

### Fixed
* Fixed a nullref when a vessel is saved in a state where parts have been
  destroyed.

## 0.1.15
### Added
* Added support for PersistentThrust (v1.7.5 only).
* Added background behaviour for SystemHeat `ModuleSystemHeatFissionEngine`.

### Changed
* Optimized the solver to be about 5x faster.

### Fixed
* Fixed a bug where efficiency multipliers were not being applied to resource
  converters in the background.
* Fixed a bug where efficiency multipliers were only being applied to resource
  converter inputs.
* Fixed a bug where EC usage of asteroid/comet drills was multiplied by the
  drill efficiency when it wasn't supposed to.

## 0.1.14
### Fixed
* Removed unintended debug logging from TAC-LS integration.
* Fixed a build issue causing import errors with TAC-LS integration.

## 0.1.13
### Added
* Patched TAC-LS status window to show estimates using BRP's background
  simulation.

### Fixed
* Fixed a bug where an item might get removed twice the cache, causing a
  solver crash.
* Fixed some issues in `ResourceSimulator` that were causing USI-LS to show
  indefinite time left on resources when that should not be the case.

## 0.1.12
### Added
* Added support for `KSP-WarpDrive`.
* Added support for `Snacks!`

### Changed
* The `WildBlueTools` integration now fully integrates with its existing
  background simulation.

## 0.1.11
### Added
* Added support for Nehemiah Engineering Orbital Science.

### Fixed
* Fixed a bug where FFT antimatter tanks were not being simulated in the
  background due to a missing inventory.

## 0.1.10
### Added
* Add support for `AutomaticLabHousekeeper`.

### Fixed
* Fixed non-discarded log statements causing KSPBurst to fail to compile bursted code.

## 0.1.9
### Added
* Added a config option to disable background simulation of science labs
  specifically.

### Fixed
* Fixed a bug where the background simulation of science labs was running
  at twice the rate it was supposed to be.

### Changed
* Changed the log level of messages during vessel restore so that they are
  printed even if the debug logging setting is disabled.

## 0.1.8
This version now has a dependency on `KSPBurst`.

### Added
* Added support for `FusionReactor` modules from FFT.

### Changed
* Some core solver methods are now compiled using burst and will use AVX2 if
  supported on your processor.

## 0.1.7
### Added
* Added support for `Background Resource` - BRP disables it if present and then
  patches `UnloadedResourceProcessing` methods to call into BRP.
* Added support for `DeepFreeze`.
* Added support for `TAC Life Support`.

## 0.1.6
### Added
* Added support for `SpaceDust`.

### Fixed
* Fixed a solver crash when presolve manages to completely elide an OR-constraint.
* Fixed a bug where a behaviour that fails to deserialize could cause the
  background simulation to make no progress.
* Fixed a bug where the USI-LS status panel would show a vessel as being out of
  EC despite the vessel still having EC available.
* Fixed a bug where the USI-LS integration was not suppressing catch-up code
  appropriately, resulting in extra supplies being consumed.

## 0.1.5
### Added
* Added support for `Near Future Solar` - specifically: `ModuleCurvedSolarPanel`.

### Changed
* Significantly optimized the background simulation.

## 0.1.4
### Fixed
* Fixed a bug where vessels that crossed a planet day/night boundary would never
  update their shadow state, resulting in the vessel being updated every frame.

## 0.1.3
### Added
* Added support for `CryoTanks`.
* Added support for `WildBlueTools`.
* Added support for `Far Future Technologies`.

### Changed
* Significantly improved performance when looking at the USI-LS status panel
  during timewarp.
* The USI-LS panel now displays `indefinite` for resources instead of
  `Infinityy (+15d)`.

### Fixed
* SystemHeat fission reactors now generate EC as an output instead of an input.

## 0.1.2
### Changed
* Changed ckan metadata to use `suggests` instead of `supports`.

## 0.1.1
### Added
* Automated release workflows for actions and spacedock.

## 0.1.0
This is the first release of Background Resource Processing.
