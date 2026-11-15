# build.cmd runs this before a release build: the shared library (the vSuite.Core submodule) must be the latest one, and that
# exact version committed in vView - otherwise the release would ship an old (or unsaved) library. Exit 1 stops the build.
# Offline (the fetch fails): a warning, and the build goes on with what's here.
$ErrorActionPreference = 'Continue'
Set-Location (Split-Path $PSScriptRoot)
$lib = 'vSuite.Core'
if (-not (Test-Path "$lib\.git")) { Write-Host "The shared library ($lib) is missing - run: git submodule update --init"; exit 1 }

$problems = @()
git -C $lib fetch -q origin 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "NOTE: couldn't reach $lib's repository - building with the library that's here, unchecked."
} else {
    $branch = (git -C $lib rev-parse --abbrev-ref origin/HEAD 2>$null)
    if (-not $branch) { $branch = 'origin/main' }
    $behind = [int](git -C $lib rev-list --count "HEAD..$branch")
    $ahead = [int](git -C $lib rev-list --count "$branch..HEAD")
    if ($behind -gt 0) {
        $problems += "$lib is $behind update(s) behind its repository:"
        $problems += (git -C $lib log --oneline "HEAD..$branch" | ForEach-Object { "    $_" })
        $problems += "  Update it: git -C $lib pull   (then build, test, and commit it with vView's version bump)"
    }
    if ($ahead -gt 0) { $problems += "$lib has $ahead commit(s) that aren't pushed to its repository - push them from $lib first." }
}
if (git -C $lib status --porcelain) { $problems += "$lib has changes that aren't committed - make them in vSuite.Core's own repo, push, then pull here." }
if (git status --porcelain -- $lib) { $problems += "vView's pointer to $lib has changed but isn't committed - commit it (with vView's version bump)." }

if ($problems.Count -gt 0) {
    Write-Host ""
    Write-Host "NOT BUILT - the shared library isn't up to date:"
    $problems | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host ("Shared library: $lib " + (git -C $lib log -1 --format='%s'))
exit 0
