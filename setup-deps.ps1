
param([string]$ckan="ckan.exe")

if (Test-Path -Path .\deps\installs) {
    Remove-Item -Path .\deps\installs -Recurse -Force
}

Get-ChildItem .\deps -Filter *.ckan | Foreach-Object {
    $name = $_.Basename
    $installdir = ".\deps\installs\$name"

    & $ckan instance fake "BRP-$name" $installdir 1.12.5 --game KSP --MakingHistory 1.9.1 --BreakingGround 1.7.1
    & $ckan instance forget "BRP-$name"

    & $ckan update  --gamedir "$installdir"
    & $ckan install --gamedir "$installdir" --headless --no-recommends -c $_.FullName
}
