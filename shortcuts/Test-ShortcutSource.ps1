$ErrorActionPreference = 'Stop'

function Assert-ShortcutCondition {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Normalize-GameName {
    param([Parameter(Mandatory)] [string] $Value)

    $builder = [System.Text.StringBuilder]::new()
    foreach ($character in $Value.ToCharArray()) {
        if ([char]::IsLetterOrDigit($character)) {
            [void] $builder.Append([char]::ToLowerInvariant($character))
        }
    }

    return $builder.ToString()
}

function Get-WorkflowActionNames {
    param([Parameter(Mandatory)] $Value)

    foreach ($item in @($Value)) {
        if ($null -eq $item) {
            continue
        }

        if ($item -is [System.Management.Automation.PSCustomObject]) {
            if ($item.PSObject.Properties.Name -contains 'action') {
                $item.action
            }

            foreach ($property in $item.PSObject.Properties) {
                Get-WorkflowActionNames $property.Value
            }
        } elseif ($item -is [System.Collections.IEnumerable] -and $item -isnot [string]) {
            Get-WorkflowActionNames $item
        }
    }
}

$sourcePath = Join-Path $PSScriptRoot 'GameReminders.shortcut-source.json'
$vectorsPath = Join-Path $PSScriptRoot 'test-vectors.json'
$source = Get-Content -Raw $sourcePath | ConvertFrom-Json -Depth 100
$vectors = Get-Content -Raw $vectorsPath | ConvertFrom-Json -Depth 100

Assert-ShortcutCondition ($source.format -eq 'game-reminders-shortcut-source') 'Unexpected Shortcut source format.'
Assert-ShortcutCondition ($source.formatVersion -eq 1) 'Unsupported Shortcut source format version.'
Assert-ShortcutCondition ($source.distribution.status -eq 'requires-apple-signing') 'Unsigned source must not claim to be importable.'
Assert-ShortcutCondition ($source.distribution.unsignedArtifact -eq 'GameReminder-unsigned.shortcut') 'Unexpected unsigned Shortcut artifact name.'
Assert-ShortcutCondition ($source.distribution.signedArtifact -eq 'GameReminder.shortcut') 'Unexpected signed Shortcut artifact name.'
Assert-ShortcutCondition ($source.distribution.sharingMode -eq 'anyone') 'The exported Shortcut must use Anyone sharing.'
Assert-ShortcutCondition ($source.configuration.iCloudContainer -eq 'Shortcuts') 'The Shortcut must use the built-in iCloud Shortcuts container.'
Assert-ShortcutCondition ($source.configuration.relativeFolder -eq 'Game Reminders') 'The Shortcut must use the fixed nested Game Reminders folder.'
Assert-ShortcutCondition ($source.configuration.importQuestions -eq $false) 'The cross-device Shortcut must not use device-specific folder import questions.'

$actions = @(Get-WorkflowActionNames $source.workflow)
Assert-ShortcutCondition ($actions -notcontains 'getParentDirectory') 'The iPhone Shortcut must not contain the macOS-only Get Parent Directory action.'
foreach ($requiredAction in @(
    'getFile',
    'parseDictionary',
    'require',
    'initializeNumber',
    'initializeText',
    'initializeBoolean',
    'incrementVariable',
    'branchOnCount',
    'dictionary',
    'serializeJson',
    'saveFile',
    'moveFile',
    'renameFile',
    'showResult')) {
    Assert-ShortcutCondition ($actions -contains $requiredAction) "Missing required action '$requiredAction'."
}

$dictionary = $source.workflow | Where-Object action -eq 'dictionary' | Select-Object -First 1
$expectedKeys = @('schemaVersion', 'id', 'gameId', 'gameNameAtCreation', 'message', 'createdAt')
$actualKeys = @($dictionary.entries.PSObject.Properties.Name)
Assert-ShortcutCondition ($dictionary.entries.schemaVersion -eq 1) 'Reminder schemaVersion must be 1.'
Assert-ShortcutCondition (($expectedKeys | Where-Object { $_ -notin $actualKeys }).Count -eq 0) 'Reminder dictionary is missing a protocol field.'
Assert-ShortcutCondition (($actualKeys | Where-Object { $_ -notin $expectedKeys }).Count -eq 0) 'Reminder dictionary contains an unexpected field.'

$save = $source.workflow | Where-Object action -eq 'saveFile' | Select-Object -First 1
$inbox = $source.workflow | Where-Object { $_.action -eq 'getFile' -and $_.relativePath -eq 'Game Reminders/inbox' } | Select-Object -First 1
$move = $source.workflow | Where-Object action -eq 'moveFile' | Select-Object -First 1
$rename = $source.workflow | Where-Object action -eq 'renameFile' | Select-Object -First 1
$success = $source.workflow | Where-Object action -eq 'showResult' | Select-Object -First 1
$catalog = $source.workflow | Where-Object { $_.action -eq 'getFile' -and $_.relativePath -eq 'Game Reminders/games.json' } | Select-Object -First 1
Assert-ShortcutCondition ($catalog.folder -eq 'Shortcuts') 'Catalog resolution must use the built-in Shortcuts container with the nested path in relativePath.'
Assert-ShortcutCondition ($save.folder -eq 'Shortcuts') 'Reminder must first be staged in the private Shortcuts folder.'
Assert-ShortcutCondition ($save.name -eq '<reminderId>.tmp') 'Reminder must use a visible UUID name with a non-JSON staging extension.'
Assert-ShortcutCondition ($save.overwrite -eq $false) 'Staging must not overwrite an existing file.'
Assert-ShortcutCondition ($inbox.folder -eq 'Shortcuts') 'Inbox resolution must use the built-in Shortcuts container with the nested path in relativePath.'
Assert-ShortcutCondition ($move.input -eq 'stagedFile' -and $move.folder -eq 'inboxFolder') 'The completed staging file must be moved to the resolved inbox folder.'
Assert-ShortcutCondition ($move.overwrite -eq $false) 'Moving the staging file must not overwrite an existing file.'
Assert-ShortcutCondition ($rename.input -eq 'inboxStagedFile') 'Finalization must rename the temporary file only after it reaches inbox.'
Assert-ShortcutCondition ($rename.name -eq '<reminderId>.json') 'Final reminder filename must use its UUID.'
Assert-ShortcutCondition (-not $rename.name.StartsWith('.')) 'Final reminder filename must not be hidden.'
Assert-ShortcutCondition ($rename.overwrite -eq $false) 'Finalization must not overwrite an existing file.'
Assert-ShortcutCondition ($success.onlyAfter -eq 'savedReminder') 'Success must follow finalization.'

$matchCounter = $source.workflow | Where-Object action -eq 'initializeNumber' | Select-Object -First 1
$sentinels = @($source.workflow | Where-Object action -eq 'initializeText')
$matchBranches = $source.workflow | Where-Object action -eq 'branchOnCount' | Select-Object -First 1
Assert-ShortcutCondition ($matchCounter.value -eq 0 -and $matchCounter.output -eq 'matchCount') 'Matching must start from an explicit numeric zero.'
Assert-ShortcutCondition ($sentinels.Count -eq 2) 'Both matched-game outputs must have explicit sentinels.'
Assert-ShortcutCondition (@($sentinels | Where-Object value -ne 'NO_MATCH').Count -eq 0) 'Matched-game sentinels must be nonblank.'
Assert-ShortcutCondition ($matchBranches.branches.zero.show -eq 'No game alias found for “<requestedGameName>”.') 'The unknown-game error must repeat the original submitted name.'

foreach ($case in $vectors.cases) {
    $requestedKey = Normalize-GameName $case.input
    $matches = @()

    if ($requestedKey.Length -gt 0) {
        foreach ($game in $vectors.catalog.games) {
            $matched = $false
            foreach ($candidate in @($game.name) + @($game.aliases)) {
                if ((Normalize-GameName ([string] $candidate)) -eq $requestedKey) {
                    $matched = $true
                }
            }

            if ($matched) {
                $matches += $game
            }
        }
    }

    $actualStatus = if ($requestedKey.Length -eq 0) {
        'emptyGameName'
    } elseif ($matches.Count -eq 0) {
        'unknownGame'
    } elseif ($matches.Count -gt 1) {
        'ambiguousGame'
    } else {
        'created'
    }

    Assert-ShortcutCondition ($actualStatus -eq $case.expectedStatus) "Test vector '$($case.name)' expected '$($case.expectedStatus)' but got '$actualStatus'."
    if ($actualStatus -eq 'unknownGame') {
        $actualError = $matchBranches.branches.zero.show.Replace('<requestedGameName>', $case.input)
        Assert-ShortcutCondition ($actualError -eq $case.expectedError) "Test vector '$($case.name)' produced the wrong unknown-game error."
    }
    if ($actualStatus -eq 'created') {
        Assert-ShortcutCondition ($matches[0].id -eq $case.expectedGameId) "Test vector '$($case.name)' resolved the wrong stable game id."
    }
}

Write-Host "Validated Shortcut source and $($vectors.cases.Count) matching vectors."
