$ErrorActionPreference = 'Stop'
function Assert-ShortcutCondition {
    param([Parameter(Mandatory)] [bool] $Condition, [Parameter(Mandatory)] [string] $Message)
    if (-not $Condition) { throw $Message }
}
function Get-WorkflowActionNames {
    param([Parameter(Mandatory)] $Value)
    foreach ($item in @($Value)) {
        if ($null -eq $item) { continue }
        if ($item -is [System.Management.Automation.PSCustomObject]) {
            if ($item.PSObject.Properties.Name -contains 'action') { $item.action }
            foreach ($property in $item.PSObject.Properties) { Get-WorkflowActionNames $property.Value }
        } elseif ($item -is [System.Collections.IEnumerable] -and $item -isnot [string]) {
            Get-WorkflowActionNames $item
        }
    }
}
$source = Get-Content -Raw (Join-Path $PSScriptRoot 'GameReminders.shortcut-source.json') | ConvertFrom-Json -Depth 100
$vectors = Get-Content -Raw (Join-Path $PSScriptRoot 'test-vectors.json') | ConvertFrom-Json -Depth 100
Assert-ShortcutCondition ($source.format -eq 'game-reminders-shortcut-source' -and $source.formatVersion -eq 1) 'Unexpected Shortcut source format.'
Assert-ShortcutCondition ($source.distribution.status -eq 'requires-apple-signing') 'Unsigned source must not claim to be importable.'
Assert-ShortcutCondition ($source.distribution.unsignedArtifact -eq 'GameReminder-unsigned.shortcut') 'Unexpected unsigned artifact name.'
Assert-ShortcutCondition ($source.distribution.signedArtifact -eq 'Game Reminder.shortcut') 'Signed distribution metadata changed.'
Assert-ShortcutCondition ($source.configuration.iCloudContainer -eq 'Shortcuts' -and $source.configuration.relativeFolder -eq 'Game Reminders') 'The fixed Shortcuts container layout changed.'
Assert-ShortcutCondition ($source.configuration.importQuestions -eq $false) 'The Shortcut must not use import questions.'
$actions = @(Get-WorkflowActionNames $source.workflow)
foreach ($required in @('getFile','parseDictionary','require','chooseFromList','askForText','dictionary','serializeJson','createFolder','saveFile','moveFile','renameFile','showResult')) {
    Assert-ShortcutCondition ($actions -contains $required) "Missing required action '$required'."
}
foreach ($forbidden in @('normalize','branchOnCount','initializeNumber','initializeBoolean','incrementVariable')) {
    Assert-ShortcutCondition ($actions -notcontains $forbidden) "Voice/text matching action '$forbidden' remains."
}
$choose = @($source.workflow | Where-Object action -eq 'chooseFromList')
Assert-ShortcutCondition ($choose.Count -eq 1) 'The workflow must contain exactly one Choose from List action.'
Assert-ShortcutCondition ($choose[0].input -eq 'gameNames' -and $choose[0].selectMultiple -eq $false) 'The chooser must present canonical names with native single selection.'
$prompts = @($source.workflow | Where-Object action -eq 'askForText')
Assert-ShortcutCondition ($prompts.Count -eq 1 -and $prompts[0].prompt -eq 'What should I remind you?') 'The only text prompt must request the reminder message.'
$sourceText = Get-Content -Raw (Join-Path $PSScriptRoot 'GameReminders.shortcut-source.json')
Assert-ShortcutCondition ($sourceText -notmatch 'alias|requestedGame|Which game') 'The unsigned flow must not parse names or access aliases.'
$catalogNames = @($vectors.catalog.games | ForEach-Object name)
Assert-ShortcutCondition ($catalogNames.Count -eq $vectors.catalog.games.Count) 'Every catalog entry must contribute one canonical name.'
Assert-ShortcutCondition (@($catalogNames | Select-Object -Unique).Count -eq $catalogNames.Count) 'Test catalog canonical names must be unique.'
foreach ($case in $vectors.cases) {
    $game = $vectors.catalog.games | Where-Object name -ceq $case.selectedName | Select-Object -First 1
    Assert-ShortcutCondition ($null -ne $game) "Selection '$($case.selectedName)' is not in the native list."
    Assert-ShortcutCondition ($game.id -eq $case.expectedGameId) "Selection '$($case.selectedName)' resolved the wrong stable ID."
}
$dictionary = $source.workflow | Where-Object action -eq 'dictionary' | Select-Object -First 1
$expectedKeys = @('schemaVersion','id','gameId','gameNameAtCreation','message','createdAt')
$actualKeys = @($dictionary.entries.PSObject.Properties.Name)
Assert-ShortcutCondition (($expectedKeys | Where-Object { $_ -notin $actualKeys }).Count -eq 0 -and ($actualKeys | Where-Object { $_ -notin $expectedKeys }).Count -eq 0) 'Reminder dictionary fields changed.'
Write-Host "Validated native game selection for all $($catalogNames.Count) registered games and $($vectors.cases.Count) stable-ID vectors."
