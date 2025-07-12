<#
.SYNOPSIS
Finds the latest Git tag, increments the version, and creates/pushes a new tag.

.DESCRIPTION
This script automates the process of versioning for a project using Git tags.
It fetches all tags from the remote, identifies the highest version tag matching the 'v<Major>.<Minor>.<Build>' format,
and then increments the specified part of the version (Major, Minor, or Build).

By default, it increments the 'Build' number. When 'Major' is incremented, 'Minor' and 'Build' are reset to 0.
When 'Minor' is incremented, 'Build' is reset to 0.

If no existing version tags are found, it starts with 'v0.1.0'.

.PARAMETER Increment
Specifies which part of the version to increment.
Valid values are 'Major', 'Minor', 'Build'. The default is 'Build'.

.EXAMPLE
# Increment the build version (e.g., v1.2.3 -> v1.2.4)
.\New-Version.ps1

.EXAMPLE
# Increment the minor version (e.g., v1.2.3 -> v1.3.0)
.\New-Version.ps1 -Increment Minor

.EXAMPLE
# Increment the major version (e.g., v1.2.3 -> v2.0.0)
.\New-Version.ps1 -Increment Major
#>
[CmdletBinding()]
param(
    [ValidateSet('Major', 'Minor', 'Build')]
    [string]$Increment = 'Build'
)

# Ensure we have the latest tags from the remote repository.
Write-Host "Fetching latest tags from remote..."
git fetch --tags --force

# Get all tags that match the semantic versioning pattern 'v*.*.*'
$tags = git tag --list 'v*.*.*'

$latestVersion = [System.Version]'0.0.0'
$initialVersion = [System.Version]'0.1.0'
$foundTag = $false

if ($null -ne $tags) {
    # Convert tag strings to System.Version objects for correct sorting
    $versions = $tags | ForEach-Object {
        # Remove the 'v' prefix before parsing
        $versionString = $_.Substring(1)
        try {
            [System.Version]$versionString
        }
        catch {
            # Silently ignore tags that do not conform to the version format
        }
    } | Sort-Object

    if ($versions.Count -gt 0) {
        $latestVersion = $versions[-1]
        $foundTag = $true
    }
}

$newMajor = $latestVersion.Major
$newMinor = $latestVersion.Minor
$newBuild = $latestVersion.Build

if (-not $foundTag) {
    Write-Host "No valid version tags found. Creating initial version: v$initialVersion"
    $newMajor = $initialVersion.Major
    $newMinor = $initialVersion.Minor
    $newBuild = $initialVersion.Build
}
else {
    Write-Host "Latest version found: v$latestVersion"
    Write-Host "Incrementing '$Increment' version number..."

    switch ($Increment) {
        'Major' {
            $newMajor++
            $newMinor = 0
            $newBuild = 0
        }
        'Minor' {
            $newMinor++
            $newBuild = 0
        }
        'Build' {
            $newBuild++
        }
    }
}

$newVersionTag = "v$newMajor.$newMinor.$newBuild"

Write-Host "Creating new tag: $newVersionTag"
git tag $newVersionTag

Write-Host "Pushing tag to remote..."
git push origin $newVersionTag

Write-Host "Successfully created and pushed tag $newVersionTag."