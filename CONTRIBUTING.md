# Contributing to Background Resource Processing

## Editing MM Patches
The MM patch files are located under `src/GameData/Patches`.
- The convention for naming patch files is to put them in a `<ModName>`
  subfolder so they can be easily navigated.
- If you are creating a new compatibility patch make sure to add attribution
  on the first line as a comment, e.g. `// RemoteTech patch by Phantomical`.

If you're doing a lot of editing of patches you may want to follow the steps
on setting up the build system so that copying all the files to the mod
directory is handled for you.

## Building
In order to build the mod you will need:
- the `dotnet` CLI

Next, you will want to create a `BackgroundResourceProcessing.props.user` file
in the repository root, like this one:
```xml
<?xml version="1.0" encoding="UTF-8"?>

<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
    <PropertyGroup>
        <ReferencePath>$KSP_ROOT_PATH</ReferencePath>
    </PropertyGroup>
</Project>
```

Make sure to replace `$KSP_ROOT_PATH` with the path to your KSP installation.
If you have an install made via steam then you might be able to skip this step.

Finally, you can build by running either:
- `dotnet build` (for a debug build), or,
- `dotnet build -c Release` (for a release build)

This will create a `GameData\BackgroundResourceProcessing` folder which you
can then drop into your KSP install's `GameData` folder.

> ### Linking the output into your `GameData` folder
> If you're iterating on patches/code/whatever then you'll find that manually
> copying stuff into the `GameData` folder will get old really quickly. You can
> instead create a junction (on windows) or a symlink (on mac/linux) so that
> KSP will just look into the build artifact directory.
>
> To do this you will need to run the following command in an admin `cmd.exe`
> prompt (for windows) in your `GameData` directory:
> ```batch
> mklink /j BackgroundResourceProcessing C:\path\to\BRP\repo\GameData\BackgroundResourceProcessing
> ```
>
> On Linux or MacOS you should be able to accomplish the same thing using `ln`.

## Testing
There are some tests for some of the more tricky parts of the codebase.
You can run these by running
```sh
dotnet test
```
in the repository root.

Note that the tests only cover a subset of the code. It is not possible to test
parts that require accessing game state, since we don't have that present.
However, the various data structures and solvers are independent of KSP and can
be tested fairly easily.

## Formatting
We use `csharpier` to format C# code. You can first install it by running
```sh
dotnet tool restore
```

Once you have done that you can then format everything by running
```sh
dotnet csharpier format .
```
