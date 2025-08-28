# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

<!--
Note: Spacedock's markdown doesn't recognize lists using `-`, so make sure to
      use `*` for all list entries.
-->

## Unreleased
### Added
* Added support for Nehemiah Engineering Orbital Science.

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
